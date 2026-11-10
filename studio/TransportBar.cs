// TransportBar.cs —— 顶部运输控制与文件动作

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace FanStudio
{
    public class TransportBar : UserControl
    {
        readonly Label _project;
        readonly StudioButton _play;
        readonly StudioButton _stop;
        readonly Label _time;
        readonly DarkNumeric _bpm;
        readonly StudioButton _import;
        readonly StudioButton _open;
        readonly StudioButton _save;
        readonly StudioButton _render;
        readonly StudioButton _help;
        readonly Panel _left;
        readonly FlowLayoutPanel _right;
        readonly ToolTip _tips;

        public event EventHandler PlayRequested;
        public event EventHandler StopRequested;
        public event EventHandler ImportMidiRequested;
        public event EventHandler OpenRequested;
        public event EventHandler SaveRequested;
        public event EventHandler RenderRequested;
        public event EventHandler ShowHelpRequested;
        public event EventHandler<BpmEventArgs> BpmChanged;

        public decimal Bpm
        {
            get { return _bpm.Value; }
            set
            {
                decimal d = Math.Max(_bpm.Minimum, Math.Min(_bpm.Maximum, value));
                _bpm.SetValueSilent(d);
            }
        }

        public TransportBar()
        {
            Height = 58;
            BackColor = StudioTheme.Panel;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _left = new Panel();
            _left.Dock = DockStyle.Left;
            _left.Width = 475;
            _left.BackColor = Color.Transparent;
            Controls.Add(_left);

            _project = new Label();
            _project.Text = "fan_studio";
            _project.AutoSize = false;
            _project.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            _project.ForeColor = StudioTheme.Text;
            _project.BackColor = Color.Transparent;
            _project.TextAlign = ContentAlignment.MiddleLeft;
            _project.Bounds = new Rectangle(16, 0, 126, Height);
            _left.Controls.Add(_project);

            _play = new StudioButton();
            _play.Text = "▶";
            _play.Accent = true;
            _play.Font = new Font("Segoe UI Symbol", 10F, FontStyle.Bold);
            _play.Bounds = new Rectangle(143, 12, 36, 34);
            _play.Click += delegate { Fire(PlayRequested); };
            _left.Controls.Add(_play);

            _stop = new StudioButton();
            _stop.Text = "■";
            _stop.Font = new Font("Segoe UI Symbol", 8F, FontStyle.Bold);
            _stop.Bounds = new Rectangle(185, 12, 36, 34);
            _stop.Click += delegate { Fire(StopRequested); };
            _left.Controls.Add(_stop);

            _time = new Label();
            _time.Text = "00:00.0";
            _time.Font = new Font("Consolas", 10.5F);
            _time.ForeColor = StudioTheme.Text;
            _time.BackColor = Color.Transparent;
            _time.TextAlign = ContentAlignment.MiddleLeft;
            _time.Bounds = new Rectangle(232, 0, 76, Height);
            _left.Controls.Add(_time);

            var bpmWrap = new Panel();
            bpmWrap.Bounds = new Rectangle(315, 12, 140, 34);
            bpmWrap.BackColor = Color.Transparent;
            var bpmLabel = new Label();
            bpmLabel.Text = "BPM";
            bpmLabel.Font = StudioTheme.UiSmall;
            bpmLabel.ForeColor = StudioTheme.Muted;
            bpmLabel.BackColor = Color.Transparent;
            bpmLabel.Bounds = new Rectangle(10, 0, 35, 34);
            bpmLabel.TextAlign = ContentAlignment.MiddleLeft;
            _bpm = new DarkNumeric();
            _bpm.Minimum = 20;
            _bpm.Maximum = 400;
            _bpm.DecimalPlaces = 2;
            _bpm.Increment = 1;
            _bpm.Value = 120;
            _bpm.Bounds = new Rectangle(47, 4, 82, 27);
            _bpm.ValueChanged += delegate
            {
                if (BpmChanged != null) BpmChanged(this, new BpmEventArgs((double)_bpm.Value));
            };
            bpmWrap.Controls.Add(bpmLabel);
            bpmWrap.Controls.Add(_bpm);
            _left.Controls.Add(bpmWrap);

            _right = new FlowLayoutPanel();
            _right.Dock = DockStyle.Right;
            _right.Width = 432;
            _right.FlowDirection = FlowDirection.RightToLeft;
            _right.WrapContents = false;
            _right.Padding = new Padding(0, 12, 14, 0);
            _right.BackColor = Color.Transparent;
            Controls.Add(_right);

            _render = MakeRight("渲染并播放", 106, true);
            _render.Click += delegate { Fire(RenderRequested); };
            _save = MakeRight("保存", 66, false);
            _save.Click += delegate { Fire(SaveRequested); };
            _open = MakeRight("打开 .fscore", 104, false);
            _open.Click += delegate { Fire(OpenRequested); };
            _import = MakeRight("导入 MIDI", 88, false);
            _import.Click += delegate { Fire(ImportMidiRequested); };
            _help = MakeRight("?", 34, false);
            _help.Font = StudioTheme.UiBold;
            _help.Click += delegate { Fire(ShowHelpRequested); };
            _right.Controls.Add(_render);
            _right.Controls.Add(_save);
            _right.Controls.Add(_open);
            _right.Controls.Add(_import);
            _right.Controls.Add(_help);

            _tips = new ToolTip();
            _tips.SetToolTip(_play, "Space：播放/停止");
            _tips.SetToolTip(_stop, "Esc：停止播放");
            _tips.SetToolTip(_import, "Ctrl+I：导入 MIDI");
            _tips.SetToolTip(_open, "Ctrl+O：打开 .fscore");
            _tips.SetToolTip(_save, "Ctrl+S：保存；Ctrl+Shift+S：另存为");
            _tips.SetToolTip(_render, "F5：渲染并播放");
            _tips.SetToolTip(_help, "F1：使用说明、协议与快捷键");
        }

        StudioButton MakeRight(string text, int width, bool accent)
        {
            var b = new StudioButton();
            b.Text = text;
            b.Width = width;
            b.Height = 34;
            b.Accent = accent;
            b.Margin = new Padding(6, 0, 0, 0);
            if (accent) b.Font = StudioTheme.UiBold;
            return b;
        }

        void Fire(EventHandler h)
        {
            if (h != null) h(this, EventArgs.Empty);
        }

        public void SetPlaying(bool playing)
        {
            _play.Text = playing ? "Ⅱ" : "▶";
            _play.Accent = !playing;
            if (playing)
            {
                _play.BackColor = Color.Transparent;
                _play.ForeColor = StudioTheme.Text;
            }
            _play.Invalidate();
        }

        public void SetTime(double seconds)
        {
            int min = (int)(Math.Max(0, seconds) / 60.0);
            double sec = Math.Max(0, seconds) - min * 60.0;
            _time.Text = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00.0}", min, sec);
        }

        public void SetProject(string name)
        {
            _project.Text = "fan_studio";
            _project.Tag = name;
        }

        public void SetBusy(bool busy)
        {
            _render.Enabled = !busy;
            _import.Enabled = !busy;
            _save.Enabled = !busy;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(StudioTheme.Border))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    public class BpmEventArgs : EventArgs
    {
        public readonly double Bpm;
        public BpmEventArgs(double bpm) { Bpm = bpm; }
    }
}
