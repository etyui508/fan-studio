// StudioTheme.cs —— 暗色编辑器视觉系统与轻量自绘控件

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FanStudio
{
    public static class StudioTheme
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);

        public static readonly Color Canvas = Color.FromArgb(23, 24, 28);
        public static readonly Color Panel = Color.FromArgb(30, 32, 38);
        public static readonly Color PanelRaised = Color.FromArgb(38, 41, 48);
        public static readonly Color PanelHover = Color.FromArgb(45, 48, 56);
        public static readonly Color Border = Color.FromArgb(44, 47, 55);
        public static readonly Color BorderStrong = Color.FromArgb(74, 79, 91);
        public static readonly Color Text = Color.FromArgb(230, 232, 236);
        public static readonly Color Muted = Color.FromArgb(154, 160, 171);
        public static readonly Color Dim = Color.FromArgb(106, 112, 123);
        public static readonly Color Accent = Color.FromArgb(242, 163, 58);
        public static readonly Color AccentDark = Color.FromArgb(66, 49, 30);
        public static readonly Color Teal = Color.FromArgb(63, 184, 175);
        public static readonly Color Purple = Color.FromArgb(169, 155, 255);
        public static readonly Color Pink = Color.FromArgb(240, 123, 160);
        public static readonly Color Danger = Color.FromArgb(255, 92, 97);
        public static readonly Color DangerSoft = Color.FromArgb(91, 38, 43);
        public static readonly Color SuccessSoft = Color.FromArgb(28, 72, 70);

        public static readonly Font Ui = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font UiSmall = new Font("Microsoft YaHei UI", 8F);
        public static readonly Font UiBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        public static readonly Font Title = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        public static readonly Font MonoSmall = new Font("Consolas", 8F);
        public static readonly Font Mono = new Font("Consolas", 9F);

        public static Color FamilyColor(string family)
        {
            if (family == "轴流风机") return Accent;
            if (family == "离心风机") return Color.FromArgb(239, 188, 80);
            if (family == "电刷直流" || family == "永磁直流" || family == "励磁直流") return Teal;
            if (family == "无刷直流" || family == "步进电机") return Purple;
            if (family == "伺服舵机") return Pink;
            if (family == "磁阻电机") return Color.FromArgb(116, 186, 255);
            if (family == "交流感应" || family == "同步电机") return Color.FromArgb(140, 204, 116);
            return Color.FromArgb(139, 148, 164);
        }

        public static Color MotorColor(Motor motor)
        {
            if (motor == null) return Color.FromArgb(139, 148, 164);
            switch (motor.Key)
            {
                case "fan40": return Color.FromArgb(242, 163, 58);
                case "fan60": return Color.FromArgb(63, 184, 175);
                case "fan80": return Color.FromArgb(169, 155, 255);
                case "fan92": return Color.FromArgb(240, 123, 160);
                case "fan120": return Color.FromArgb(116, 186, 255);
                case "fan140": return Color.FromArgb(140, 204, 116);
                case "fan12038": return Color.FromArgb(245, 196, 84);
                case "centrifugal": return Color.FromArgb(91, 211, 183);
                case "blower": return Color.FromArgb(255, 145, 120);
                case "laptop": return Color.FromArgb(198, 148, 255);
            }
            Color[] palette = {
                Color.FromArgb(242, 163, 58),
                Color.FromArgb(63, 184, 175),
                Color.FromArgb(169, 155, 255),
                Color.FromArgb(240, 123, 160),
                Color.FromArgb(116, 186, 255),
                Color.FromArgb(140, 204, 116),
                Color.FromArgb(245, 196, 84),
                Color.FromArgb(91, 211, 183),
                Color.FromArgb(198, 148, 255),
                Color.FromArgb(255, 145, 120),
                Color.FromArgb(113, 170, 240),
                Color.FromArgb(166, 214, 98)
            };
            uint hash = 2166136261u;
            string key = motor.Key == null ? motor.Model : motor.Key;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619u;
            }
            return palette[(int)(hash % (uint)palette.Length)];
        }

        public static string BeatTime(double beat, double bpm)
        {
            double seconds = Math.Max(0, beat) * 60.0 / Math.Max(1, bpm);
            int minutes = (int)(seconds / 60.0);
            double rest = seconds - minutes * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00.0}", minutes, rest);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color color, Rectangle r, int radius)
        {
            using (var path = RoundRect(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void StrokeRound(Graphics g, Color color, Rectangle r, int radius)
        {
            using (var path = RoundRect(r, radius))
            using (var pen = new Pen(color))
                g.DrawPath(pen, path);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color,
            Rectangle bounds, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color,
            Point point)
        {
            TextRenderer.DrawText(g, text, font, point, color);
        }

        public static Label Label(string text, Color color, Font font)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = color;
            l.Font = font;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static void StyleInput(Control c)
        {
            c.BackColor = PanelRaised;
            c.ForeColor = Text;
            c.Font = Ui;
        }

        public static void DarkenNativeScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch { }
        }

        public static void HideNativeScrollbars(Control c)
        {
            try
            {
                if (c.IsHandleCreated) ShowScrollBar(c.Handle, 3, false);
            }
            catch { }
        }
    }

    public class StudioButton : Button
    {
        bool _hover;
        bool _down;
        public bool Accent;
        public bool Danger;
        public bool Compact;

        public StudioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = StudioTheme.Text;
            Font = StudioTheme.Ui;
            Cursor = Cursors.Hand;
            TabStop = false;
            BackColor = Color.Transparent;
            Margin = new Padding(0);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            Color fill;
            Color border;
            Color text;
            if (!Enabled)
            {
                fill = StudioTheme.Panel;
                border = StudioTheme.Border;
                text = StudioTheme.Dim;
            }
            else if (Danger)
            {
                fill = _down ? Color.FromArgb(115, 47, 52) : (_hover ? Color.FromArgb(88, 39, 44) : StudioTheme.PanelRaised);
                border = StudioTheme.Danger;
                text = StudioTheme.Danger;
            }
            else if (Accent)
            {
                fill = _down ? Color.FromArgb(215, 139, 42) : (_hover ? Color.FromArgb(250, 175, 72) : StudioTheme.Accent);
                border = fill;
                text = StudioTheme.Canvas;
            }
            else
            {
                fill = _down ? Color.FromArgb(31, 33, 39) : (_hover ? StudioTheme.PanelHover : StudioTheme.PanelRaised);
                border = _hover ? StudioTheme.BorderStrong : StudioTheme.Border;
                text = StudioTheme.Text;
            }
            StudioTheme.FillRound(g, fill, r, Compact ? 5 : 7);
            StudioTheme.StrokeRound(g, border, r, Compact ? 5 : 7);
            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    public class SectionHeader : Control
    {
        public string Detail = "";

        public SectionHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            Height = 36;
            BackColor = Color.Transparent;
            Font = StudioTheme.UiBold;
            ForeColor = StudioTheme.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(StudioTheme.Panel);
            Rectangle textRect = new Rectangle(14, 0, Math.Max(0, Width - 28), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Detail.Length > 0)
            {
                Size s = TextRenderer.MeasureText(Detail, StudioTheme.UiSmall);
                Rectangle detailRect = new Rectangle(Math.Max(14, Width - s.Width - 14), 0,
                    s.Width, Height);
                TextRenderer.DrawText(e.Graphics, Detail, StudioTheme.UiSmall, detailRect,
                    StudioTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            using (var pen = new Pen(StudioTheme.Border))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    public class Separator : Control
    {
        public Separator()
        {
            Height = 1;
            BackColor = StudioTheme.Border;
        }
    }
}
