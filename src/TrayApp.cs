// Tray icon, its menu, and the small volume panel that opens on left click.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ConsoleSwitch
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(32, 32, 36);
        public static readonly Color Panel = Color.FromArgb(44, 44, 50);
        public static readonly Color Fore = Color.FromArgb(236, 236, 240);
        public static readonly Color Muted = Color.FromArgb(160, 160, 172);
        public static readonly Color Accent = Color.FromArgb(124, 107, 224);

        public static void Apply(Form f)
        {
            f.BackColor = Back;
            f.ForeColor = Fore;
            f.HandleCreated += (s, e) =>
            {
                int on = 1;
                try { Native.DwmSetWindowAttribute(f.Handle, 20, ref on, 4); Native.DwmSetWindowAttribute(f.Handle, 19, ref on, 4); } catch { }
            };
        }

        public static void ApplyChildren(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button)
                {
                    var b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Panel;
                    b.ForeColor = Fore;
                    b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80);
                }
                else if (c is ComboBox) { c.BackColor = Panel; c.ForeColor = Fore; ((ComboBox)c).FlatStyle = FlatStyle.Flat; }
                else if (c is CheckBox || c is Label) { if (c.ForeColor != Muted) c.ForeColor = Fore; }
                ApplyChildren(c);
            }
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Back; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Back; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Back; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Back; } }
        public override Color MenuItemSelected { get { return Theme.Panel; } }
        public override Color MenuItemBorder { get { return Theme.Panel; } }
        public override Color MenuBorder { get { return Color.FromArgb(60, 60, 68); } }
        public override Color SeparatorDark { get { return Color.FromArgb(60, 60, 68); } }
        public override Color SeparatorLight { get { return Color.FromArgb(60, 60, 68); } }
        public override Color CheckBackground { get { return Theme.Panel; } }
        public override Color CheckSelectedBackground { get { return Theme.Panel; } }
    }

    public class TrayApp : ApplicationContext
    {
        public const string SupportUrl = "https://buymeacoffee.com/mehmeteminf";
        public const string ProjectUrl = "https://github.com/mehmeteminakkaya/console-switch";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Config cfg;
        Switcher switcher;
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem statusItem, autoItem, startupItem;
        readonly VolumePanel panel;
        readonly Control ui = new Control();

        public TrayApp(Config cfg)
        {
            this.cfg = cfg;
            ui.CreateControl(); // for marshalling events from the switcher thread

            menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColors()), ForeColor = Theme.Fore, ShowImageMargin = false };
            statusItem = new ToolStripMenuItem("Console Switch") { Enabled = false, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Show console now", null, (s, e) => Background(() => switcher.SwitchToConsole())));
            menu.Items.Add(new ToolStripMenuItem("Show PC now", null, (s, e) => Background(() => switcher.SwitchToPc())));
            menu.Items.Add(new ToolStripMenuItem("Fix console audio", null, (s, e) => Background(() => switcher.FixConsoleAudio())));
            menu.Items.Add(new ToolStripSeparator());
            autoItem = new ToolStripMenuItem("Switch automatically", null, (s, e) =>
            {
                cfg.AutoSwitch = !cfg.AutoSwitch; cfg.Save(); RefreshMenu();
            });
            menu.Items.Add(autoItem);
            startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => { SetStartup(!IsStartup()); RefreshMenu(); });
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("Open log", null, (s, e) => Open(Log.FilePath)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("☕ Support this project", null, (s, e) => Open(SupportUrl)));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitThread()));
            foreach (ToolStripItem it in menu.Items) it.ForeColor = Theme.Fore;
            statusItem.ForeColor = Theme.Muted;

            icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Console Switch" };
            try { icon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { icon.Icon = SystemIcons.Application; }

            panel = new VolumePanel(cfg, () => Background(() => switcher.FixConsoleAudio()));
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) panel.Toggle(); };

            SystemEvents.DisplaySettingsChanged += (s, e) => Ddc.ClearCache();

            if (!cfg.IsComplete && !RunSetup())
            {
                icon.Visible = false;
                Environment.Exit(0);
            }
            StartSwitcher();
            RefreshMenu();
        }

        void StartSwitcher()
        {
            switcher = new Switcher(cfg);
            switcher.StateChanged += () => { try { ui.BeginInvoke(new Action(RefreshMenu)); } catch { } };
            switcher.Start();
        }

        bool RunSetup()
        {
            using (var w = new SetupWizard(cfg)) return w.ShowDialog() == DialogResult.OK;
        }

        void OpenSettings()
        {
            if (switcher != null) switcher.Paused = true;
            bool changed = RunSetup();
            if (changed)
            {
                if (switcher != null) switcher.Stop();
                StartSwitcher();
            }
            else if (switcher != null) switcher.Paused = false;
            RefreshMenu();
        }

        void RefreshMenu()
        {
            string where = switcher == null ? "" : switcher.Mode == Mode.Console ? "showing console" : switcher.Mode == Mode.Pc ? "showing PC" : "starting…";
            statusItem.Text = (string.IsNullOrEmpty(cfg.ConsoleName) ? "Console" : cfg.ConsoleName) + ": " +
                (switcher != null && switcher.ConsoleOnline ? "on" : "off") + "  ·  " + where;
            autoItem.Checked = cfg.AutoSwitch;
            startupItem.Checked = IsStartup();
            string tip = "Console Switch – " + where;
            icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        static void Background(Action a)
        {
            new Thread(() => { try { a(); } catch (Exception ex) { Log.Write("Action failed: " + ex.Message); } }) { IsBackground = true }.Start();
        }

        static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        static bool IsStartup()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("ConsoleSwitch") != null;
        }

        static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("ConsoleSwitch", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("ConsoleSwitch", false);
            }
        }

        protected override void ExitThreadCore()
        {
            if (switcher != null) switcher.Stop();
            icon.Visible = false;
            icon.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>Small panel above the tray: monitor speaker volume, mute, and the audio fix.</summary>
    public class VolumePanel : Form
    {
        readonly Config cfg;
        readonly TrackBar slider;
        readonly Label title, value;
        readonly Button muteBtn, fixBtn;
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 150 };
        int beforeMute = -1;
        DateTime hiddenAt = DateTime.MinValue;
        volatile int pending = -1;
        volatile bool writing;

        public VolumePanel(Config cfg, Action fixAudio)
        {
            this.cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(300, 132);
            Font = new Font("Segoe UI", 9.5f);
            Theme.Apply(this);

            title = new Label { Text = "Monitor volume", Left = 14, Top = 12, Width = 200, Font = new Font("Segoe UI Semibold", 10f) };
            value = new Label { Text = "–", Left = 230, Top = 12, Width = 56, TextAlign = ContentAlignment.TopRight, Font = new Font("Segoe UI Semibold", 10f) };
            slider = new TrackBar { Left = 8, Top = 40, Width = 284, Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, BackColor = Theme.Back };
            muteBtn = new Button { Text = "Mute", Left = 14, Top = 88, Width = 90, Height = 30 };
            fixBtn = new Button { Text = "Fix console audio", Left = 116, Top = 88, Width = 170, Height = 30 };
            Controls.AddRange(new Control[] { title, value, slider, muteBtn, fixBtn });
            Theme.ApplyChildren(this);

            slider.ValueChanged += (s, e) => { value.Text = slider.Value + "%"; debounce.Stop(); debounce.Start(); };
            debounce.Tick += (s, e) => { debounce.Stop(); QueueWrite(slider.Value); };
            muteBtn.Click += (s, e) => ToggleMute();
            fixBtn.Click += (s, e) => { Hide(); fixAudio(); };
            Deactivate += (s, e) => { hiddenAt = DateTime.UtcNow; Hide(); };
        }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            if ((DateTime.UtcNow - hiddenAt).TotalMilliseconds < 250) return; // the click that just closed it
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Min(Cursor.Position.X - Width / 2, area.Right - Width - 8), area.Bottom - Height - 8);
            if (Location.X < area.Left) Location = new Point(area.Left + 8, Location.Y);
            Show();
            Activate();
            ReadVolume();
        }

        void ReadVolume()
        {
            value.Text = "…";
            new Thread(() =>
            {
                int v = Ddc.Get(cfg.MonitorModel, Ddc.VCP_VOLUME);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        bool ok = v >= 0 && v <= 100;
                        slider.Enabled = ok; muteBtn.Enabled = ok;
                        if (ok) { slider.Value = v; value.Text = v + "%"; }
                        else value.Text = "n/a";
                        muteBtn.Text = ok && v == 0 && beforeMute > 0 ? "Unmute" : "Mute";
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void ToggleMute()
        {
            if (slider.Value == 0 && beforeMute > 0) { slider.Value = beforeMute; beforeMute = -1; muteBtn.Text = "Mute"; }
            else { beforeMute = slider.Value; slider.Value = 0; muteBtn.Text = "Unmute"; }
        }

        // Only the latest slider position is sent; one write at a time keeps DDC from choking.
        void QueueWrite(int v)
        {
            pending = v;
            if (writing) return;
            writing = true;
            new Thread(() =>
            {
                try
                {
                    while (pending >= 0)
                    {
                        int next = pending; pending = -1;
                        Ddc.Set(cfg.MonitorModel, Ddc.VCP_VOLUME, next);
                        Thread.Sleep(60);
                    }
                }
                finally { writing = false; }
            }) { IsBackground = true }.Start();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // WS_EX_TOOLWINDOW: not in Alt+Tab
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Color.FromArgb(70, 70, 80))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
