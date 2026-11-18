// First-run setup (also reachable from the tray as Settings). One window, three steps top to bottom:
// pick the monitor and its two inputs, find the console on the network, choose how Windows displays behave.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ConsoleSwitch
{
    public class SetupWizard : Form
    {
        readonly Config cfg;
        ComboBox monitorBox, pcBox, consoleInputBox, deviceBox, layoutBox;
        Label monitorNote, deviceNote;
        Button scanBtn, saveBtn, testConsoleBtn, testPcBtn;
        CheckBox showAll;
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        List<LanDevice> devices = new List<LanDevice>();

        class InputItem
        {
            public int Code;
            public bool IsCurrent;
            public override string ToString() { return Ddc.InputName(Code) + (IsCurrent ? "  (showing now)" : ""); }
        }

        public SetupWizard(Config cfg)
        {
            this.cfg = cfg;
            Text = "Console Switch setup";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 600);
            Theme.Apply(this);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 16;
            AddHeading("1. Your monitor", ref y);
            AddText("Console Switch talks to the monitor over DDC/CI. If your monitor isn't listed, turn on DDC/CI in its on-screen menu.", ref y);
            monitorBox = AddCombo(ref y);
            monitorBox.SelectedIndexChanged += (s, e) => FillInputs();
            monitorNote = AddText("", ref y);
            AddLabel("Input your PC uses:", y + 4);
            pcBox = AddCombo(ref y, 220);
            AddLabel("Input your console uses:", y + 4);
            consoleInputBox = AddCombo(ref y, 220);

            y += 8;
            AddHeading("2. Your console", ref y);
            AddText("Turn the console on (connected to the same network), then press Scan.", ref y);
            scanBtn = new Button { Text = "Scan network", Left = 20, Top = y, Width = 130, Height = 30 };
            scanBtn.Click += (s, e) => Scan();
            Controls.Add(scanBtn);
            showAll = new CheckBox { Text = "Show all devices", Left = 165, Top = y + 5, AutoSize = true };
            showAll.CheckedChanged += (s, e) => FillDevices();
            Controls.Add(showAll);
            y += 38;
            deviceBox = AddCombo(ref y);
            deviceNote = AddText("", ref y);

            y += 8;
            AddHeading("3. Windows displays", ref y);
            layoutBox = AddCombo(ref y);
            layoutBox.Items.Add("Automatic (recommended): on a laptop, mirror while the console is on");
            layoutBox.Items.Add("Always mirror while the console is on");
            layoutBox.Items.Add("Don't change my display layout");
            layoutBox.SelectedIndex = cfg.Layout == LayoutMode.Laptop ? 1 : cfg.Layout == LayoutMode.DontTouch ? 2 : 0;

            y += 12;
            testConsoleBtn = new Button { Text = "Test: show console", Left = 20, Top = y, Width = 160, Height = 32 };
            testPcBtn = new Button { Text = "Test: show PC", Left = 190, Top = y, Width = 130, Height = 32 };
            saveBtn = new Button { Text = "Save", Left = 440, Top = y, Width = 100, Height = 32 };
            testConsoleBtn.Click += (s, e) => TestInput(true);
            testPcBtn.Click += (s, e) => TestInput(false);
            saveBtn.Click += (s, e) => SaveAndClose();
            Controls.Add(testConsoleBtn); Controls.Add(testPcBtn); Controls.Add(saveBtn);
            AcceptButton = saveBtn;
            ClientSize = new Size(560, y + 50);
            Theme.ApplyChildren(this);

            Shown += (s, e) => LoadMonitors();
        }

        // ---------- layout helpers ----------
        void AddHeading(string text, ref int y)
        {
            Controls.Add(new Label { Text = text, Left = 16, Top = y, AutoSize = true, Font = new Font("Segoe UI Semibold", 11f) });
            y += 30;
        }
        Label AddText(string text, ref int y)
        {
            var l = new Label { Text = text, Left = 20, Top = y, Width = 520, Height = 38, ForeColor = Theme.Muted };
            Controls.Add(l);
            y += 40;
            return l;
        }
        void AddLabel(string text, int y) { Controls.Add(new Label { Text = text, Left = 20, Top = y, Width = 180 }); }
        ComboBox AddCombo(ref int y, int left = 20)
        {
            var c = new ComboBox { Left = left, Top = y, Width = 540 - left, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(c);
            y += 36;
            return c;
        }

        // ---------- step 1 ----------
        void LoadMonitors()
        {
            monitorBox.Items.Clear();
            monitorNote.Text = "Looking for monitors…";
            UseWaitCursor = true;
            RunInBackground(() => Ddc.Discover(), found =>
            {
                UseWaitCursor = false;
                monitors = found ?? new List<MonitorInfo>();
                foreach (var m in monitors) monitorBox.Items.Add(m);
                if (monitors.Count == 0)
                {
                    monitorNote.Text = "No monitor answered over DDC/CI. Turn on DDC/CI in the monitor's menu, connect it, and reopen setup.";
                    return;
                }
                int pick = monitors.FindIndex(m => string.Equals(m.Model, cfg.MonitorModel, StringComparison.OrdinalIgnoreCase));
                monitorBox.SelectedIndex = pick >= 0 ? pick : 0;
            });
        }

        void FillInputs()
        {
            var m = monitorBox.SelectedItem as MonitorInfo;
            pcBox.Items.Clear(); consoleInputBox.Items.Clear();
            if (m == null) return;
            int current = Ddc.Get(m.Model, Ddc.VCP_INPUT);
            foreach (int code in m.Inputs)
            {
                pcBox.Items.Add(new InputItem { Code = code, IsCurrent = code == current });
                consoleInputBox.Items.Add(new InputItem { Code = code, IsCurrent = code == current });
            }
            monitorNote.Text = (m.HasVolume ? "Speaker volume control: supported." : "This monitor doesn't report speaker volume over DDC/CI.")
                + "  The input showing now is usually your PC's.";
            SelectInput(pcBox, cfg.PcInput > 0 ? cfg.PcInput : current);
            int consoleDefault = cfg.ConsoleInput;
            // Consoles use HDMI, so prefer an HDMI input that isn't the PC's.
            if (consoleDefault <= 0) foreach (int code in m.Inputs) if (code != current && code >= 0x11 && code <= 0x13) { consoleDefault = code; break; }
            if (consoleDefault <= 0) foreach (int code in m.Inputs) if (code != current) { consoleDefault = code; break; }
            SelectInput(consoleInputBox, consoleDefault);
        }

        static void SelectInput(ComboBox box, int code)
        {
            for (int i = 0; i < box.Items.Count; i++)
                if (((InputItem)box.Items[i]).Code == code) { box.SelectedIndex = i; return; }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        // ---------- step 2 ----------
        void Scan()
        {
            scanBtn.Enabled = false;
            deviceNote.Text = "Scanning your network (a few seconds)…";
            RunInBackground(() => Network.Scan(), found =>
            {
                scanBtn.Enabled = true;
                devices = found ?? new List<LanDevice>();
                FillDevices();
            });
        }

        void FillDevices()
        {
            deviceBox.Items.Clear();
            foreach (var d in devices)
                if (showAll.Checked || d.Vendor.Length > 0 || d.Mac == cfg.ConsoleMac) deviceBox.Items.Add(d);
            int consoles = 0;
            foreach (var d in devices) if (d.Vendor.Length > 0) consoles++;
            if (devices.Count == 0) deviceNote.Text = "Nothing answered. Is this PC on the same network as the console?";
            else if (consoles == 0 && !showAll.Checked)
                deviceNote.Text = "No PlayStation, Xbox or Nintendo found. Is the console fully on (not in rest mode)? Or tick 'Show all devices'.";
            else deviceNote.Text = "Pick your console. It is remembered by its MAC address, so a changing IP is fine.";
            for (int i = 0; i < deviceBox.Items.Count; i++)
                if (((LanDevice)deviceBox.Items[i]).Mac == cfg.ConsoleMac) { deviceBox.SelectedIndex = i; return; }
            if (deviceBox.Items.Count > 0) deviceBox.SelectedIndex = 0;
        }

        // ---------- actions ----------
        void TestInput(bool console)
        {
            var m = monitorBox.SelectedItem as MonitorInfo;
            var item = (console ? consoleInputBox.SelectedItem : pcBox.SelectedItem) as InputItem;
            if (m == null || item == null) return;
            Ddc.Set(m.Model, Ddc.VCP_INPUT, item.Code);
        }

        void SaveAndClose()
        {
            var m = monitorBox.SelectedItem as MonitorInfo;
            var pc = pcBox.SelectedItem as InputItem;
            var con = consoleInputBox.SelectedItem as InputItem;
            var dev = deviceBox.SelectedItem as LanDevice;
            if (m == null) { MessageBox.Show(this, "Pick a monitor first.", Text); return; }
            if (pc == null || con == null || pc.Code == con.Code) { MessageBox.Show(this, "Pick two different inputs for the PC and the console.", Text); return; }
            if (dev == null && cfg.ConsoleMac.Length != 12) { MessageBox.Show(this, "Turn the console on and press Scan, then pick it.", Text); return; }
            cfg.MonitorModel = m.Model;
            cfg.PcInput = pc.Code;
            cfg.ConsoleInput = con.Code;
            if (dev != null)
            {
                cfg.ConsoleMac = dev.Mac;
                cfg.ConsoleIp = dev.Ip;
                cfg.ConsoleName = dev.Vendor.Length > 0 ? dev.Vendor : "Console";
            }
            cfg.Layout = layoutBox.SelectedIndex == 1 ? LayoutMode.Laptop : layoutBox.SelectedIndex == 2 ? LayoutMode.DontTouch : LayoutMode.Auto;
            cfg.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        void RunInBackground<T>(Func<T> work, Action<T> done)
        {
            var t = new Thread(() =>
            {
                T result = default(T);
                try { result = work(); } catch (Exception ex) { Log.Write("Setup: " + ex.Message); }
                try { BeginInvoke(new Action(() => done(result))); } catch { }
            }) { IsBackground = true };
            t.Start();
        }
    }
}
