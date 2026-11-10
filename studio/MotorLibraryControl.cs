// MotorLibraryControl.cs —— 左侧型号库，可按族系分组、搜索并拖入卷帘

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace FanStudio
{
    public class MotorLibraryControl : UserControl
    {
        readonly List<Motor> _catalog;
        readonly TextBox _search;
        readonly FlowLayoutPanel _list;
        Motor _selected;
        Point _dragStart;
        bool _cueActive;

        public event EventHandler<MotorLibraryEventArgs> MotorSelected;
        public event EventHandler<MotorLibraryEventArgs> MotorActivated;

        public Motor MotorSelectedItem { get { return _selected; } }

        public MotorLibraryControl(List<Motor> catalog)
        {
            _catalog = catalog;
            BackColor = StudioTheme.Panel;
            Padding = new Padding(0);

            var head = new SectionHeader();
            head.Text = "电机库";
            head.Detail = "拖入卷帘";
            head.Dock = DockStyle.Top;
            Controls.Add(head);

            var searchWrap = new Panel();
            searchWrap.Dock = DockStyle.Top;
            searchWrap.Height = 42;
            searchWrap.Padding = new Padding(10, 7, 10, 5);
            searchWrap.BackColor = StudioTheme.Panel;
            Controls.Add(searchWrap);

            _search = new TextBox();
            _search.Dock = DockStyle.Fill;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = StudioTheme.Ui;
            _search.TabStop = false;
            _search.BackColor = StudioTheme.PanelRaised;
            _search.TextChanged += delegate { Fill(); };
            searchWrap.Controls.Add(_search);
            _search.GotFocus += delegate
            {
                if (_cueActive)
                {
                    _search.Text = "";
                    _cueActive = false;
                }
                _search.ForeColor = StudioTheme.Text;
            };
            _search.LostFocus += delegate
            {
                if (_search.Text.Length == 0) SetCue();
            };
            SetCue();

            _list = new FlowLayoutPanel();
            _list.Dock = DockStyle.Fill;
            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.AutoScroll = true;
            _list.BackColor = StudioTheme.Panel;
            _list.Padding = new Padding(9, 3, 7, 12);
            _list.Resize += delegate { ResizeCards(); };
            _list.HandleCreated += delegate { StudioTheme.DarkenNativeScrollbars(_list); };
            _list.Layout += delegate { StudioTheme.HideNativeScrollbars(_list); };
            _list.MouseWheel += delegate { BeginInvoke((MethodInvoker)delegate { StudioTheme.HideNativeScrollbars(_list); }); };
            Controls.Add(_list);
            _list.BringToFront();
            Fill();
        }

        void Fill()
        {
            if (_list == null) return;
            _list.SuspendLayout();
            _list.Controls.Clear();
            string query = _cueActive ? "" : _search.Text.Trim().ToLowerInvariant();
            string currentFamily = null;
            int shown = 0;

            var groups = new List<string>();
            var byGroup = new Dictionary<string, List<Motor>>();
            foreach (var m in _catalog)
            {
                if (m.Kind != "revolve") continue;
                string hay = (m.Key + " " + m.Model + " " + m.Family + " " + m.Role + " " + m.Note).ToLowerInvariant();
                if (query.Length > 0 && hay.IndexOf(query, StringComparison.Ordinal) < 0) continue;
                if (!byGroup.ContainsKey(m.Family))
                {
                    groups.Add(m.Family);
                    byGroup[m.Family] = new List<Motor>();
                }
                byGroup[m.Family].Add(m);
            }

            foreach (string family in groups)
            {
                currentFamily = family;
                var lbl = new Label();
                lbl.Text = currentFamily;
                lbl.Font = StudioTheme.UiSmall;
                lbl.ForeColor = StudioTheme.Muted;
                lbl.Height = 25;
                lbl.Margin = new Padding(3, 8, 0, 0);
                lbl.BackColor = Color.Transparent;
                _list.Controls.Add(lbl);
                foreach (var m in byGroup[family])
                {
                    var card = new MotorCard(m);
                    card.Width = Math.Max(150, _list.ClientSize.Width - 30);
                    card.Margin = new Padding(2, 2, 2, 2);
                    card.MotorSelected += delegate(object s, MotorLibraryEventArgs e)
                    {
                        _selected = e.Motor;
                        foreach (Control c in _list.Controls)
                        {
                            MotorCard mc = c as MotorCard;
                            if (mc != null) mc.Selected = mc.Motor == _selected;
                        }
                        if (MotorSelected != null) MotorSelected(this, e);
                    };
                    card.MotorActivated += delegate(object s, MotorLibraryEventArgs e)
                    {
                        if (MotorActivated != null) MotorActivated(this, e);
                    };
                    card.DragStarted += delegate(object s, MouseEventArgs e)
                    {
                        _dragStart = e.Location;
                        card.BeginDrag();
                    };
                    _list.Controls.Add(card);
                    shown++;
                }
            }

            if (shown == 0)
            {
                var empty = new Label();
                empty.Text = "没有匹配的可定音高电机";
                empty.AutoSize = false;
                empty.Height = 70;
                empty.Width = Math.Max(150, _list.ClientSize.Width - 30);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.ForeColor = StudioTheme.Dim;
                empty.Font = StudioTheme.Ui;
                _list.Controls.Add(empty);
            }
            _list.ResumeLayout();
            StudioTheme.HideNativeScrollbars(_list);
        }

        void ResizeCards()
        {
            int width = Math.Max(150, _list.ClientSize.Width - 28);
            foreach (Control c in _list.Controls)
            {
                if (c is MotorCard || (c is Label && c.Height == 70)) c.Width = width;
            }
        }

        public void FocusSearch()
        {
            _search.Focus();
            _search.SelectAll();
        }

        void SetCue()
        {
            _cueActive = true;
            _search.Text = "搜索型号 / 族系 / 角色";
            _search.ForeColor = StudioTheme.Muted;
        }
    }

    public class MotorLibraryEventArgs : EventArgs
    {
        public readonly Motor Motor;
        public MotorLibraryEventArgs(Motor motor) { Motor = motor; }
    }

    public class MotorCard : Control
    {
        public readonly Motor Motor;
        bool _hover;
        bool _selected;
        Point _down;
        bool _dragging;
        ToolTip _tip;

        public event EventHandler<MotorLibraryEventArgs> MotorSelected;
        public event EventHandler<MotorLibraryEventArgs> MotorActivated;
        public event EventHandler<MouseEventArgs> DragStarted;

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public MotorCard(Motor motor)
        {
            Motor = motor;
            Height = 66;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _tip = new ToolTip();
            _tip.SetToolTip(this, motor.Note);
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _down = e.Location;
                _dragging = false;
                if (MotorSelected != null) MotorSelected(this, new MotorLibraryEventArgs(Motor));
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != 0 && !_dragging)
            {
                int dx = Math.Abs(e.X - _down.X);
                int dy = Math.Abs(e.Y - _down.Y);
                if (dx + dy > 6)
                {
                    _dragging = true;
                    if (DragStarted != null) DragStarted(this, e);
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            if (MotorActivated != null) MotorActivated(this, new MotorLibraryEventArgs(Motor));
            base.OnDoubleClick(e);
        }

        public void BeginDrag()
        {
            if (!_dragging) _dragging = true;
            DoDragDrop(Motor.Key, DragDropEffects.Copy);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _selected ? Color.FromArgb(48, 43, 33) : (_hover ? StudioTheme.PanelHover : StudioTheme.PanelRaised);
            StudioTheme.FillRound(g, fill, r, 7);
            if (_selected || _hover)
                StudioTheme.StrokeRound(g, _selected ? StudioTheme.Accent : StudioTheme.BorderStrong, r, 7);

            Color fam = StudioTheme.MotorColor(Motor);
            using (var brush = new SolidBrush(fam))
                g.FillRectangle(brush, 10, 13, 8, 8);
            TextRenderer.DrawText(g, Motor.Model, StudioTheme.UiBold,
                new Rectangle(25, 7, Width - 34, 21), StudioTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            string range = RangeText(Motor);
            double minBeats = 3.5 * Motor.Glide / 0.5;
            string detail = range + " · 最短 " + minBeats.ToString("0.00", CultureInfo.InvariantCulture) + " 拍";
            TextRenderer.DrawText(g, detail, StudioTheme.UiSmall,
                new Rectangle(25, 29, Width - 34, 18), StudioTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Motor.Role, StudioTheme.UiSmall,
                new Rectangle(10, 49, Width - 20, 15), StudioTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        static string RangeText(Motor m)
        {
            if (m.Kind != "revolve") return "—";
            return HzName(m.LoHz) + "–" + HzName(m.HiHz);
        }

        static string HzName(double hz)
        {
            if (hz <= 0) return "—";
            double midi = 69 + 12 * Math.Log(hz / 440.0, 2);
            return ScoreParser.MidiToName((int)Math.Round(midi));
        }
    }
}
