﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SetBrightness
{
    public partial class TabPageTemplate : UserControl
    {
        private readonly Monitor _monitor;
        private readonly MonitorsManager _monitorsManager;
        private readonly SetMonitorQueue _setQueue;
        public readonly MonitorType MonitorType;
        public event EventHandler<RenameMonitorEvent> ChangeMonitorNameEventHandler;
        public readonly int Uuid;

        public TabPageTemplate(Monitor monitor, string name, MonitorsManager monitorsManager, int uuid)
        {
            InitializeComponent();
            _monitor = monitor;
            _monitorsManager = monitorsManager;
            Uuid = uuid;
            Name = name;
            MonitorType = monitor.Type;

            _setQueue = new SetMonitorQueue(_monitor);
            PreWork();
        }

        private void PreWork()
        {
            if (!_monitor.SupportContrast)
            {
                contrastTrackbar.Enabled = false;

                var right = contrastNameLabel.Right;
                contrastNameLabel.Text += @"(不支持)";
                contrastNameLabel.Left = right - contrastNameLabel.Width;
                contrastNameLabel.ForeColor = Color.DarkGray;
                contrastLabel.ForeColor = Color.DarkGray;
            }

            brightLabel.DataBindings.Add("Text", brightTrackbar, "Value");
            contrastLabel.DataBindings.Add("Text", contrastTrackbar, "Value");

            brightTrackbar.ValueChanged += BrightTrackbar_ValueChanged;
            contrastTrackbar.ValueChanged += ContrastTrackbar_ValueChanged;
        }

        private void ContrastTrackbar_ValueChanged(object sender, EventArgs e)
        {
            var value = contrastTrackbar.Value;
            new Thread(() => _setQueue.Add(value, false)).Start();
        }

        private void BrightTrackbar_ValueChanged(object sender, EventArgs e)
        {
            var value = brightTrackbar.Value;
            new Thread(() => _setQueue.Add(value, true)).Start();
        }

        public bool UseContrast
        {
            set
            {
                if (!_monitor.SupportContrast)
                {
                    value = false;
                }

                contrastTrackbar.Enabled = value;
            }
        }

        public int Brightness
        {
            get { return brightTrackbar.Value; }
            set
            {
                if (brightTrackbar.Value == value)
                {
                    return;
                }

                brightTrackbar.Value = value;
                new Thread(() => { _setQueue.Add(value, true); }).Start();
            }
        }

        private int Contrast
        {
            set
            {
                if (contrastTrackbar.Value == value)
                {
                    return;
                }

                contrastTrackbar.Value = value;
                new Thread(() => { _setQueue.Add(value, false); }).Start();
            }
        }

        public int BrightnessMax => 100;

        public int BrightnessMin => 0;

        public async void UpdateValues()
        {
            try
            {
                await Task.WhenAll(
                    Task.Run(() =>
                        SafeSetTrackBar(brightTrackbar, _monitor.GetBrightness(), BrightTrackbar_ValueChanged)),
                    Task.Run(() =>
                        SafeSetTrackBar(contrastTrackbar, _monitor.GetContrast(), ContrastTrackbar_ValueChanged)));
            }
            catch (InvalidMonitorException e)
            {
                _monitorsManager.RefreshMonitors();
                Debug.WriteLine(e);
            }
        }

        private void SafeSetTrackBar(TrackBar trackBar, int value, EventHandler handler)
        {
            if (trackBar.InvokeRequired)
            {
                Invoke(new Action<TrackBar, int, EventHandler>(SafeSetTrackBar), trackBar, value, handler);
            }
            else
            {
                trackBar.ValueChanged -= handler;
                trackBar.Value = value;
                trackBar.ValueChanged += handler;
            }
        }

        // kind like producer/customer model ?
        private class SetMonitorQueue
        {
            private readonly Queue<int> _brightnessQueue = new Queue<int>();
            private readonly Queue<int> _contrastQueue = new Queue<int>();
            private readonly object _brightnessLock = new object();
            private readonly object _contrastLock = new object();
            private readonly Monitor _monitor;

            public SetMonitorQueue(Monitor monitor)
            {
                _monitor = monitor;
            }

            public void Add(int value, bool isBrightness)
            {
                var @lock = isBrightness ? _brightnessLock : _contrastLock;
                var queue = isBrightness ? _brightnessQueue : _contrastQueue;
                var action = isBrightness ? (Action<int>) _monitor.SetBrightness : _monitor.SetContrast;
                if (queue.Count > 0)
                {
                    queue.Clear();
                }

                queue.Enqueue(value);

                lock (@lock)
                {
                    if (queue.Count == 0)
                    {
                        return;
                    }

                    var des = queue.Dequeue();
                    action.Invoke(des);
                }
            }
        }

        public bool OwnTheMonitor(Monitor monitor)
        {
            return monitor.IsSameMonitor(_monitor);
        }

        public bool IsValide()
        {
            return _monitor.IsValide();
        }

        #region mouse click set trackbar value directly

        private static int GetTrackbarClickValue(TrackBar trackBar, double x)
        {
            int max = trackBar.Maximum, min = trackBar.Minimum;
            // guess so much, not found in:
            // https://referencesource.microsoft.com/#System.Windows.Forms/winforms/Managed/System/WinForms/TrackBarRenderer.cs
            const int padding = 8;
            var value = (x - padding) / (trackBar.Width - 2 * padding) * (max - min);
            value = value > max ? max : value < min ? min : value;
            return Convert.ToInt32(value);
        }

        private void brightTrackbar_MouseDown(object sender, MouseEventArgs e)
        {
            Brightness = GetTrackbarClickValue(brightTrackbar, e.X);
        }

        private void contrastTrackbar_MouseDown(object sender, MouseEventArgs e)
        {
            Contrast = GetTrackbarClickValue(contrastTrackbar, e.X);
        }

        #endregion

        public void ShowTabPageContextMenuStrip()
        {
            preferMonitorToolStripMenuItem.CheckState = MonitorsManager.PreferMonitorId().Equals(_monitor.Id)
                ? CheckState.Checked
                : CheckState.Unchecked;

            tabPageContextMenuStrip.Show(Cursor.Position);
        }

        private void pinTabPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var haveSetPrefer = preferMonitorToolStripMenuItem.CheckState == CheckState.Checked;
            MonitorsManager.SetPreferMonitor(haveSetPrefer ? _monitor.Id : "");
        }

        public bool IsPreferred(string id)
        {
            return _monitor.Id == id;
        }

        private static bool _hasOpenPrompt;

        private void renameMonitorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_hasOpenPrompt)
            {
                return;
            }

            _hasOpenPrompt = true;
            var currentName = string.IsNullOrWhiteSpace(_monitor.UserDefineName)
                ? _monitor.Name
                : _monitor.UserDefineName;
            var input = Prompt.ShowDialog("重命名此设备", currentName);
            _hasOpenPrompt = false;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            if (_monitor.Name.Equals(input.Trim()))
            {
                return;
            }

            _monitor.UserDefineName = input;
            SettingManager.AddRenameMonitor(_monitor.Id, input);
            ChangeMonitorNameEventHandler?.Invoke(sender, new RenameMonitorEvent(input, Uuid));
        }

        public void LoadUserName()
        {
            var dictionary = SettingManager.GetReNameMonitors();
            if (!dictionary.ContainsKey(_monitor.Id))
            {
                return;
            }

            _monitor.UserDefineName = dictionary[_monitor.Id];
            ChangeMonitorNameEventHandler?.Invoke(null, new RenameMonitorEvent(_monitor.UserDefineName, Uuid));
        }

        private void tabPageContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            restoreMonitorNameToolStripMenuItem.Visible = !string.IsNullOrWhiteSpace(_monitor.UserDefineName);
        }

        private void restoreMonitorNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_monitor.UserDefineName))
            {
                return;
            }

            _monitor.UserDefineName = "";
            SettingManager.RemoveMonitorName(_monitor.Id);
            ChangeMonitorNameEventHandler?.Invoke(sender, new RenameMonitorEvent(_monitor.Name, Uuid));
        }
    }

    public class RenameMonitorEvent : EventArgs
    {
        public string NewName { get; }
        public int Uuid { get; }

        public RenameMonitorEvent(string newName, int uuid)
        {
            NewName = newName;
            Uuid = uuid;
        }
    }
}