// Tray icon, its right-click menu, and the control panel that opens on left click.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ConsoleSwitch
{
    // Colours shared with the setup window.
    static class Theme
    {
        public static readonly Color Back = Ui.Back;
        public static readonly Color Panel = Ui.Card;
        public static readonly Color Fore = Ui.Fore;
        public static readonly Color Muted = Ui.Muted;

        public static void Apply(Form f)
        {
            f.BackColor = Back;
            f.ForeColor = Fore;
            Ui.Window(f);
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
                    b.FlatAppearance.BorderColor = Ui.Line;
                    b.FlatAppearance.MouseOverBackColor = Ui.CardHover;
                    b.Cursor = Cursors.Hand;
                }
                else if (c is ComboBox) { c.BackColor = Panel; c.ForeColor = Fore; ((ComboBox)c).FlatStyle = FlatStyle.Flat; }
                else if (c is CheckBox || c is Label) { if (c.ForeColor != Muted) c.ForeColor = Fore; }
                ApplyChildren(c);
            }
        }
    }

    /// <summary>Dark, rounded right-click menu.</summary>
    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Ui.Smooth(e.Graphics);
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (var b = new SolidBrush(Ui.CardHover)) using (var p = Ui.Round(r, 5)) e.Graphics.FillPath(b, p);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.Fore : Ui.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Ui.Smooth(e.Graphics);
            var r = new RectangleF(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            using (var b = new SolidBrush(Ui.Accent)) using (var p = Ui.Round(r, 4)) e.Graphics.FillPath(b, p);
            using (var f = Ui.Icons(8f)) Ui.Center(e.Graphics, "", f, Color.White, r);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Ui.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Ui.Back; } }
            public override Color ImageMarginGradientBegin { get { return Ui.Back; } }
            public override Color ImageMarginGradientMiddle { get { return Ui.Back; } }
            public override Color ImageMarginGradientEnd { get { return Ui.Back; } }
            public override Color MenuBorder { get { return Ui.Line; } }
        }
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
        readonly ControlPanel panel;
        readonly Control ui = new Control();

        public TrayApp(Config cfg)
        {
            this.cfg = cfg;
            ui.CreateControl(); // for marshalling events from the switcher thread

            menu = new ContextMenuStrip
            {
                Renderer = new MenuRenderer(),
                Font = new Font("Segoe UI", 9.5f),
                Padding = new Padding(2, 6, 2, 6),
                ShowCheckMargin = false,
                ShowImageMargin = true,
                ImageScalingSize = new Size(16, 16)
            };
            statusItem = Item("Console Switch", null, null);
            statusItem.Enabled = false;
            statusItem.Font = new Font("Segoe UI Semibold", 9.5f);
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Show console", Ui.GlyphGame, () => Background(() => switcher.SwitchToConsole())));
            menu.Items.Add(Item("Show PC", Ui.GlyphPc, () => Background(() => switcher.SwitchToPc())));
            menu.Items.Add(Item("Fix console audio", Ui.GlyphRepair, () => Background(() => switcher.FixConsoleAudio())));
            menu.Items.Add(new ToolStripSeparator());
            autoItem = Item("Switch automatically", Ui.GlyphAuto, () => { cfg.AutoSwitch = !cfg.AutoSwitch; cfg.Save(); RefreshAll(); });
            menu.Items.Add(autoItem);
            startupItem = Item("Start with Windows", "", () => { SetStartup(!IsStartup()); RefreshAll(); });
            menu.Items.Add(startupItem);
            menu.Items.Add(Item("Settings", Ui.GlyphSettings, OpenSettings));
            menu.Items.Add(Item("Open log", "", () => Open(Log.FilePath)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Support this project", Ui.GlyphHeart, () => Open(SupportUrl)));
            menu.Items.Add(Item("Exit", "", ExitThread));

            icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Console Switch" };
            try { icon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { icon.Icon = SystemIcons.Application; }

            panel = new ControlPanel(cfg, () => switcher, OpenSettings, () => { cfg.Save(); RefreshAll(); });
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) panel.Toggle(); };

            SystemEvents.DisplaySettingsChanged += (s, e) => Ddc.ClearCache();
            MigrateStartup();

            if (!cfg.IsComplete && !RunSetup())
            {
                icon.Visible = false;
                Environment.Exit(0);
            }
            StartSwitcher();
            RefreshAll();
        }

        static ToolStripMenuItem Item(string text, string glyph, Action click)
        {
            var it = new ToolStripMenuItem(text) { Padding = new Padding(4, 3, 4, 3) };
            if (glyph != null) it.Image = GlyphImage(glyph);
            if (click != null) it.Click += (s, e) => click();
            return it;
        }

        static Bitmap GlyphImage(string glyph)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            using (var f = Ui.Icons(10.5f))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Ui.Center(g, glyph, f, Ui.Fore, new RectangleF(0, 0, 16, 16));
            }
            return bmp;
        }

        void StartSwitcher()
        {
            switcher = new Switcher(cfg);
            switcher.StateChanged += () => { try { ui.BeginInvoke(new Action(RefreshAll)); } catch { } };
            switcher.Start();
        }

        bool RunSetup()
        {
            using (var w = new SetupWizard(cfg)) return w.ShowDialog() == DialogResult.OK;
        }

        void OpenSettings()
        {
            panel.Hide();
            if (switcher != null) switcher.Paused = true;
            bool changed = RunSetup();
            if (changed)
            {
                if (switcher != null) switcher.Stop();
                StartSwitcher();
            }
            else if (switcher != null) switcher.Paused = false;
            RefreshAll();
        }

        public static string StatusText(Config cfg, Switcher sw)
        {
            string name = string.IsNullOrEmpty(cfg.ConsoleName) ? "Console" : cfg.ConsoleName;
            if (sw == null) return name;
            string where = sw.Mode == Mode.Console ? "showing console" : sw.Mode == Mode.Pc ? "showing PC" : "starting…";
            return name + " " + (sw.ConsoleOnline ? "on" : "off") + "  ·  " + where;
        }

        void RefreshAll()
        {
            statusItem.Text = StatusText(cfg, switcher);
            autoItem.Checked = cfg.AutoSwitch;
            startupItem.Checked = IsStartup();
            string tip = "Console Switch – " + StatusText(cfg, switcher);
            icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            panel.RefreshState();
        }

        static void Background(Action a)
        {
            new Thread(() => { try { a(); } catch (Exception ex) { Log.Write("Action failed: " + ex.Message); } }) { IsBackground = true }.Start();
        }

        internal static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        // Start with Windows through a shortcut in the Startup folder. (A Run registry value was skipped by
        // Explorer on some boots; Startup-folder shortcuts have been reliable.)
        static string StartupShortcut
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Console Switch.lnk"); }
        }

        static bool IsStartup() { return System.IO.File.Exists(StartupShortcut); }

        internal static void SetStartup(bool on)
        {
            // Older versions used the Run key; clean it up either way.
            try { using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue("ConsoleSwitch", false); } catch { }
            try
            {
                if (!on) { if (System.IO.File.Exists(StartupShortcut)) System.IO.File.Delete(StartupShortcut); return; }
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object lnk = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { StartupShortcut });
                var t = lnk.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { System.IO.Path.GetDirectoryName(Application.ExecutablePath) });
                t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "Console Switch" });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception ex) { Log.Write("Could not change start-with-Windows: " + ex.Message); }
        }

        /// <summary>Moves users of the old Run-key startup to the Startup-folder shortcut.</summary>
        internal static void MigrateStartup()
        {
            try
            {
                bool hadRunKey;
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) hadRunKey = k != null && k.GetValue("ConsoleSwitch") != null;
                if (hadRunKey || IsStartup()) SetStartup(true); // also refreshes the shortcut's target if the exe moved
            }
            catch { }
        }

        protected override void ExitThreadCore()
        {
            if (switcher != null) switcher.Stop();
            icon.Visible = false;
            icon.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>Quick panel above the tray: PC | Console switch, monitor volume, audio fix and auto mode.</summary>
    public class ControlPanel : Form
    {
        readonly Config cfg;
        readonly Func<Switcher> getSwitcher;
        readonly Action changed;
        readonly Label title, status, valueLabel, volumeLabel;
        readonly Segmented source;
        readonly Slider slider;
        readonly IconButton muteBtn, settingsBtn, fixBtn, autoBtn, supportBtn;
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 120 };
        int beforeMute = -1;
        bool loading;
        DateTime hiddenAt = DateTime.MinValue;
        volatile int pending = -1;
        volatile bool writing;

        public ControlPanel(Config cfg, Func<Switcher> getSwitcher, Action openSettings, Action changed)
        {
            this.cfg = cfg; this.getSwitcher = getSwitcher; this.changed = changed;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(340, 252);
            BackColor = Ui.Back;
            ForeColor = Ui.Fore;
            DoubleBuffered = true;
            Ui.Window(this);

            title = new Label { Left = 18, Top = 16, Width = 250, Height = 24, Font = new Font("Segoe UI Semibold", 11.5f), ForeColor = Ui.Fore, BackColor = Ui.Back };
            status = new Label { Left = 18, Top = 40, Width = 260, Height = 20, Font = new Font("Segoe UI", 9f), ForeColor = Ui.Muted, BackColor = Ui.Back };
            settingsBtn = new IconButton { Glyph = Ui.GlyphSettings, Left = 288, Top = 16 };
            settingsBtn.Click += (s, e) => openSettings();

            source = new Segmented { Left = 16, Top = 72, Width = 308 };
            source.Picked += Pick;

            volumeLabel = new Label { Text = "Monitor volume", Left = 18, Top = 150, Width = 200, Height = 20, Font = new Font("Segoe UI", 9f), ForeColor = Ui.Muted, BackColor = Ui.Back };
            muteBtn = new IconButton { Glyph = Ui.GlyphVolume, Left = 12, Top = 172 };
            slider = new Slider { Left = 50, Top = 176, Width = 228 };
            valueLabel = new Label { Left = 278, Top = 176, Width = 46, Height = 28, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Ui.Fore, BackColor = Ui.Back };

            fixBtn = new IconButton { Pill = true, Glyph = Ui.GlyphRepair, Text = "Fix audio", Left = 16, Top = 212, Width = 100, Height = 30 };
            autoBtn = new IconButton { Pill = true, Glyph = Ui.GlyphAuto, Text = "Auto", Left = 122, Top = 212, Width = 80, Height = 30 };
            supportBtn = new IconButton { Pill = true, Glyph = Ui.GlyphHeart, Text = "Support", Left = 208, Top = 212, Width = 116, Height = 30 };
            Controls.AddRange(new Control[] { title, status, settingsBtn, source, volumeLabel, muteBtn, slider, valueLabel, fixBtn, autoBtn, supportBtn });

            slider.ValueChanged += (s, e) =>
            {
                valueLabel.Text = slider.Value + "%";
                muteBtn.Glyph = slider.Value == 0 ? Ui.GlyphMute : Ui.GlyphVolume; muteBtn.Invalidate();
                if (!loading) { debounce.Stop(); debounce.Start(); }
            };
            debounce.Tick += (s, e) => { debounce.Stop(); QueueWrite(slider.Value); };
            muteBtn.Click += (s, e) => ToggleMute();
            fixBtn.Click += (s, e) =>
            {
                var sw = getSwitcher(); if (sw == null) return;
                status.Text = "Fixing console audio…";
                Run(() => sw.FixConsoleAudio());
            };
            autoBtn.Click += (s, e) => { cfg.AutoSwitch = !cfg.AutoSwitch; changed(); };
            supportBtn.Click += (s, e) => { Hide(); TrayApp.Open(TrayApp.SupportUrl); };
            Deactivate += (s, e) => { hiddenAt = DateTime.UtcNow; Hide(); };
            // Switching changes which display is primary and where it sits; keep the panel in the tray corner.
            SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                try { if (IsHandleCreated) BeginInvoke(new Action(() => { if (Visible) PlaceNearTray(); })); } catch { }
            };
        }

        void Pick(int index)
        {
            var sw = getSwitcher(); if (sw == null) return;
            source.Busy = true;
            source.Selected = index;
            status.Text = index == 1 ? "Switching to the console…" : "Switching to the PC…";
            Run(() => { if (index == 1) sw.SwitchToConsole(); else sw.SwitchToPc(); });
        }

        void Run(Action a)
        {
            new Thread(() =>
            {
                try { a(); } catch (Exception ex) { Log.Write("Panel action failed: " + ex.Message); }
                try { BeginInvoke(new Action(() => { source.Busy = false; RefreshState(); ReadVolume(); })); } catch { }
            }) { IsBackground = true }.Start();
        }

        public void RefreshState()
        {
            if (InvokeRequired) { BeginInvoke(new Action(RefreshState)); return; }
            var sw = getSwitcher();
            title.Text = string.IsNullOrEmpty(cfg.MonitorModel) ? "Console Switch" : cfg.MonitorModel;
            if (!source.Busy)
            {
                status.Text = TrayApp.StatusText(cfg, sw);
                source.Selected = sw == null ? -1 : sw.Mode == Mode.Console ? 1 : sw.Mode == Mode.Pc ? 0 : -1;
            }
            source.Labels = new[] { "PC", string.IsNullOrEmpty(cfg.ConsoleName) || cfg.ConsoleName == "Console" ? "Console" : Short(cfg.ConsoleName) };
            source.Invalidate();
            autoBtn.Active = cfg.AutoSwitch; autoBtn.Invalidate();
            Invalidate();
        }

        /// <summary>For screenshots: shows the panel in the middle of the screen with sample state.</summary>
        public void ShowPreview()
        {
            title.Text = "VG249Q3A";
            status.Text = "PlayStation on  ·  showing console";
            source.Labels = new[] { "PC", "PlayStation" };
            source.Selected = 1;
            loading = true; slider.Value = 40; loading = false;
            valueLabel.Text = "40%";
            autoBtn.Active = true;
            previewDot = true;
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }
        bool previewDot;

        static string Short(string name) { return name.StartsWith("Xbox") ? "Xbox" : name; }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            if ((DateTime.UtcNow - hiddenAt).TotalMilliseconds < 250) return; // the click that just closed it
            PlaceNearTray();
            RefreshState();
            Show();
            Activate();
            ReadVolume();
        }

        // Bottom-right corner above the tray, like Windows' own flyouts. The tray lives on the primary display.
        void PlaceNearTray()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        }

        void ReadVolume()
        {
            valueLabel.Text = "…";
            new Thread(() =>
            {
                int v = Ddc.Get(cfg.MonitorModel, Ddc.VCP_VOLUME);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        bool ok = v >= 0 && v <= 100;
                        slider.Enabled = ok; muteBtn.Enabled = ok;
                        loading = true;
                        if (ok) slider.Value = v;
                        loading = false;
                        valueLabel.Text = ok ? v + "%" : "n/a";
                        volumeLabel.Text = ok ? "Monitor volume" : "Volume not available on this monitor right now";
                        muteBtn.Glyph = ok && v == 0 ? Ui.GlyphMute : Ui.GlyphVolume;
                        muteBtn.Invalidate();
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void ToggleMute()
        {
            if (slider.Value == 0) { slider.Value = beforeMute > 0 ? beforeMute : 20; beforeMute = -1; }
            else { beforeMute = slider.Value; slider.Value = 0; }
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
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ClassStyle |= 0x20000; return cp; } // tool window, drop shadow
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.Smooth(e.Graphics);
            // status dot next to the subtitle
            var sw = getSwitcher();
            Color dot = previewDot || sw != null && sw.ConsoleOnline ? Ui.Green : Ui.Muted;
            using (var b = new SolidBrush(dot)) e.Graphics.FillEllipse(b, 8, 46, 6, 6);
            using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 16, 140, Width - 16, 140);
        }
    }
}
