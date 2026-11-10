// DarkScrollBar.cs —— 自绘深色滚动条，支持拖动滑块和翻页

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FanStudio
{
    public class DarkScrollBar : Control
    {
        Orientation _orientation;
        int _minimum;
        int _maximum = 100;
        int _largeChange = 10;
        int _value;
        bool _dragging;
        int _dragOffset;

        public event EventHandler ValueChanged;

        public Orientation Orientation
        {
            get { return _orientation; }
            set
            {
                _orientation = value;
                if (value == Orientation.Vertical)
                {
                    Dock = DockStyle.Right;
                    Width = 12;
                }
                else
                {
                    Dock = DockStyle.Bottom;
                    Height = 12;
                }
                Invalidate();
            }
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; Clamp(); Invalidate(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = Math.Max(_minimum, value); Clamp(); Invalidate(); }
        }

        public int LargeChange
        {
            get { return _largeChange; }
            set { _largeChange = Math.Max(1, value); Clamp(); Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int old = _value;
                _value = Math.Max(_minimum, Math.Min(ScrollableMax, value));
                Invalidate();
                if (old != _value && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        int ScrollableMax { get { return Math.Max(_minimum, _maximum - _largeChange + 1); } }

        public DarkScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = StudioTheme.Panel;
            TabStop = false;
            Orientation = Orientation.Vertical;
        }

        void Clamp()
        {
            if (_value > ScrollableMax) _value = ScrollableMax;
            if (_value < _minimum) _value = _minimum;
        }

        Rectangle ThumbRect()
        {
            int track = _orientation == Orientation.Vertical ? Height : Width;
            int extent = _orientation == Orientation.Vertical ? Width : Height;
            int range = Math.Max(1, _maximum - _minimum + 1);
            int thumb = (int)Math.Max(22.0, track * Math.Min(1.0, _largeChange / (double)range));
            int travel = Math.Max(0, track - thumb);
            int movable = Math.Max(1, ScrollableMax - _minimum);
            int pos = _minimum >= ScrollableMax ? 0 :
                (int)Math.Round((_value - _minimum) * travel / (double)movable);
            if (_orientation == Orientation.Vertical)
                return new Rectangle(2, pos + 1, Math.Max(4, extent - 4), Math.Max(16, thumb - 2));
            return new Rectangle(pos + 1, 2, Math.Max(16, thumb - 2), Math.Max(4, extent - 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(StudioTheme.Panel);
            using (var track = new SolidBrush(Color.FromArgb(24, 26, 31)))
            {
                if (_orientation == Orientation.Vertical)
                    e.Graphics.FillRectangle(track, 1, 0, Width - 2, Height);
                else
                    e.Graphics.FillRectangle(track, 0, 1, Width, Height - 2);
            }
            Rectangle r = ThumbRect();
            Color fill = _dragging ? StudioTheme.BorderStrong : Color.FromArgb(72, 77, 89);
            StudioTheme.FillRound(e.Graphics, fill, r, 4);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Rectangle r = ThumbRect();
                if (r.Contains(e.Location))
                {
                    _dragging = true;
                    _dragOffset = _orientation == Orientation.Vertical ? e.Y - r.Y : e.X - r.X;
                    Capture = true;
                }
                else
                {
                    bool before = _orientation == Orientation.Vertical ? e.Y < r.Y : e.X < r.X;
                    Value += before ? -_largeChange : _largeChange;
                }
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Rectangle r = ThumbRect();
                int track = _orientation == Orientation.Vertical ? Height : Width;
                int thumb = _orientation == Orientation.Vertical ? r.Height : r.Width;
                int travel = Math.Max(1, track - thumb);
                int pos = _orientation == Orientation.Vertical ? e.Y - _dragOffset : e.X - _dragOffset;
                pos = Math.Max(0, Math.Min(travel, pos));
                Value = _minimum + (int)Math.Round(pos * (ScrollableMax - _minimum) / (double)travel);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
            base.OnMouseUp(e);
        }
    }
}
