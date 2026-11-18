// Custom-drawn controls for the tray panel: a Windows 11 style slider, a segmented PC | Console switch,
// and flat icon buttons. Plain WinForms controls look dated on Windows 11, so these paint themselves.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ConsoleSwitch
{
    static class Ui
    {
        public static readonly Color Back = Color.FromArgb(28, 28, 32);
        public static readonly Color Card = Color.FromArgb(40, 40, 46);
        public static readonly Color CardHover = Color.FromArgb(52, 52, 60);
        public static readonly Color Line = Color.FromArgb(58, 58, 66);
        public static readonly Color Fore = Color.FromArgb(240, 240, 244);
        public static readonly Color Muted = Color.FromArgb(150, 150, 162);
        public static readonly Color Accent = Color.FromArgb(139, 123, 234);
        public static readonly Color AccentDark = Color.FromArgb(98, 80, 200);
        public static readonly Color Green = Color.FromArgb(110, 214, 150);

        static string iconFamily;
        /// <summary>Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10.</summary>
        public static Font Icons(float size)
        {
            if (iconFamily == null)
            {
                iconFamily = "Segoe MDL2 Assets";
                using (var fonts = new InstalledFontCollection())
                    foreach (var f in fonts.Families) if (f.Name == "Segoe Fluent Icons") { iconFamily = f.Name; break; }
            }
            return new Font(iconFamily, size);
        }

        public const string GlyphVolume = "", GlyphMute = "", GlyphGame = "", GlyphPc = "",
            GlyphSettings = "", GlyphRepair = "", GlyphHeart = "", GlyphAuto = "";

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Center(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            TextRenderer.DrawText(g, text, font, Rectangle.Round(r), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>Windows 11: dark title/frames and rounded corners for a window.</summary>
        public static void Window(Form f)
        {
            f.HandleCreated += (s, e) =>
            {
                int on = 1, round = 2;
                try
                {
                    Native.DwmSetWindowAttribute(f.Handle, 20, ref on, 4);
                    Native.DwmSetWindowAttribute(f.Handle, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE = round
                }
                catch { }
            };
        }
    }

    abstract class PaintedControl : Control
    {
        protected bool hover, down;
        protected PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    }

    /// <summary>Rounded slider with an accent fill and a ringed thumb.</summary>
    class Slider : PaintedControl
    {
        int value;
        bool dragging;
        public event EventHandler ValueChanged;
        public int Maximum = 100;

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(0, Math.Min(Maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        public Slider() { Height = 28; }

        RectangleF Track { get { return new RectangleF(10, Height / 2f - 2, Width - 20, 4); } }

        void SetFromX(int x)
        {
            var t = Track;
            Value = (int)Math.Round((x - t.X) / t.Width * Maximum);
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled) return; dragging = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { if (Enabled) Value += e.Delta > 0 ? 2 : -2; base.OnMouseWheel(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Smooth(g);
            var t = Track;
            float x = t.X + t.Width * value / Maximum;
            using (var b = new SolidBrush(Ui.Line)) using (var p = Ui.Round(t, 2)) g.FillPath(b, p);
            if (Enabled && value > 0)
                using (var b = new SolidBrush(Ui.Accent)) using (var p = Ui.Round(new RectangleF(t.X, t.Y, x - t.X, t.Height), 2)) g.FillPath(b, p);
            float r = hover || dragging ? 9 : 8;
            using (var b = new SolidBrush(Color.FromArgb(70, 70, 78))) g.FillEllipse(b, x - r, Height / 2f - r, 2 * r, 2 * r);
            float inner = dragging ? 4 : hover ? 6 : 5;
            using (var b = new SolidBrush(Enabled ? Ui.Accent : Ui.Muted)) g.FillEllipse(b, x - inner, Height / 2f - inner, 2 * inner, 2 * inner);
        }
    }

    /// <summary>Two-option switch. The selected side is filled with the accent colour.</summary>
    class Segmented : PaintedControl
    {
        public string[] Glyphs = { Ui.GlyphPc, Ui.GlyphGame };
        public string[] Labels = { "PC", "Console" };
        int selected = -1, hoverIndex = -1;
        public bool Busy;
        public event Action<int> Picked;

        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public Segmented() { Height = 64; }

        RectangleF Half(int i)
        {
            float w = (Width - 6) / 2f;
            return new RectangleF(3 + i * w, 3, w, Height - 6);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = e.X < Width / 2 ? 0 : 1;
            if (h != hoverIndex) { hoverIndex = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = e.X < Width / 2 ? 0 : 1;
            if (i != selected && !Busy) { var h = Picked; if (h != null) h(i); }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Smooth(g);
            using (var b = new SolidBrush(Ui.Card)) using (var p = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12)) g.FillPath(b, p);
            using (var iconFont = Ui.Icons(16f))
            using (var textFont = new Font("Segoe UI Semibold", 9.5f))
            {
                for (int i = 0; i < 2; i++)
                {
                    var r = Half(i);
                    bool sel = i == selected;
                    if (sel)
                        using (var b = new LinearGradientBrush(r, Ui.Accent, Ui.AccentDark, 90f)) using (var p = Ui.Round(r, 10)) g.FillPath(b, p);
                    else if (i == hoverIndex && !Busy)
                        using (var b = new SolidBrush(Ui.CardHover)) using (var p = Ui.Round(r, 10)) g.FillPath(b, p);
                    Color c = sel ? Color.White : Ui.Fore;
                    Ui.Center(g, Glyphs[i], iconFont, c, new RectangleF(r.X, r.Y + 6, r.Width, 24));
                    Ui.Center(g, Labels[i], textFont, sel ? Color.White : Ui.Muted, new RectangleF(r.X, r.Y + 30, r.Width, 22));
                }
            }
        }
    }

    /// <summary>Round icon button (mute, settings), or a pill with a glyph and text.</summary>
    class IconButton : PaintedControl
    {
        public string Glyph = "";
        public bool Pill;
        public bool Active;
        public IconButton() { Size = new Size(36, 36); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Smooth(g);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Color fill = Active ? Ui.Accent : down ? Ui.Line : hover ? Ui.CardHover : Pill ? Ui.Card : Color.Transparent;
            if (fill != Color.Transparent)
                using (var b = new SolidBrush(fill)) using (var p = Ui.Round(r, Pill ? Height / 2f : Width / 2f)) g.FillPath(b, p);
            Color c = Enabled ? (Active ? Color.White : Ui.Fore) : Ui.Muted;
            using (var f = Ui.Icons(Pill ? 11f : 13f))
            {
                if (!Pill) { Ui.Center(g, Glyph, f, c, r); return; }
                using (var tf = new Font("Segoe UI", 9f))
                {
                    var gs = TextRenderer.MeasureText(Glyph, f);
                    var ts = TextRenderer.MeasureText(Text, tf);
                    float total = gs.Width + 2 + ts.Width;
                    float x0 = (Width - total) / 2f;
                    Ui.Center(g, Glyph, f, c, new RectangleF(x0, 0, gs.Width, Height));
                    Ui.Center(g, Text, tf, c, new RectangleF(x0 + gs.Width + 2, 0, ts.Width, Height));
                }
            }
        }
    }
}
