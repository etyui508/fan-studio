// DarkNumeric.cs —— 统一暗色数值输入，避免原生 NumericUpDown 的亮色主题破坏界面

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FanStudio
{
    public class DarkNumeric : UserControl
    {
        readonly TextBox _edit;
        readonly StudioButton _up;
        readonly StudioButton _down;
        decimal _value;
        decimal _minimum;
        decimal _maximum = 100;
        decimal _increment = 1;
        int _decimals;

        public event EventHandler ValueChanged;

        public decimal Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = value;
                if (_maximum < _minimum) _maximum = _minimum;
                Value = _value;
            }
        }

        public decimal Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = value;
                if (_minimum > _maximum) _minimum = _maximum;
                Value = _value;
            }
        }

        public decimal Increment { get { return _increment; } set { _increment = Math.Max(0.000001M, value); } }

        public int DecimalPlaces
        {
            get { return _decimals; }
            set { _decimals = Math.Max(0, Math.Min(6, value)); SetDisplay(); }
        }

        public decimal Value
        {
            get { return _value; }
            set
            {
                decimal clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                bool changed = clamped != _value;
                _value = clamped;
                SetDisplay();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SetValueSilent(decimal value)
        {
            _value = Math.Max(_minimum, Math.Min(_maximum, value));
            SetDisplay();
        }

        public DarkNumeric()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            Height = 26;
            BackColor = StudioTheme.PanelRaised;
            ForeColor = StudioTheme.Text;
            Font = StudioTheme.MonoSmall;
            Margin = new Padding(0);

            _edit = new TextBox();
            _edit.BorderStyle = BorderStyle.None;
            _edit.BackColor = StudioTheme.PanelRaised;
            _edit.ForeColor = StudioTheme.Text;
            _edit.Font = StudioTheme.MonoSmall;
            _edit.TextAlign = HorizontalAlignment.Right;
            _edit.Text = "0";
            _edit.KeyDown += OnEditKeyDown;
            _edit.Leave += delegate { CommitText(); };
            Controls.Add(_edit);

            _up = new StudioButton();
            _up.Text = "▲";
            _up.Compact = true;
            _up.Font = new Font("Segoe UI Symbol", 5F, FontStyle.Bold);
            _up.Click += delegate { Value = _value + _increment; };
            Controls.Add(_up);

            _down = new StudioButton();
            _down.Text = "▼";
            _down.Compact = true;
            _down.Font = new Font("Segoe UI Symbol", 5F, FontStyle.Bold);
            _down.Click += delegate { Value = _value - _increment; };
            Controls.Add(_down);
            Resize += delegate { LayoutParts(); };
            MouseWheel += delegate(object s, MouseEventArgs e)
            {
                Value = _value + (e.Delta > 0 ? _increment : -_increment);
            };
            LayoutParts();
        }

        void LayoutParts()
        {
            int arrowW = 17;
            int h = Math.Max(20, Height);
            int editH = Math.Max(15, TextRenderer.MeasureText("0", Font).Height + 2);
            _edit.Bounds = new Rectangle(5, (h - editH) / 2, Math.Max(10, Width - arrowW - 9), editH);
            int half = h / 2;
            _up.Bounds = new Rectangle(Width - arrowW - 1, 1, arrowW, Math.Max(8, half - 1));
            _down.Bounds = new Rectangle(Width - arrowW - 1, half, arrowW, Math.Max(8, h - half - 2));
        }

        void OnEditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitText();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                Value = _value + _increment;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                Value = _value - _increment;
                e.SuppressKeyPress = true;
            }
        }

        void CommitText()
        {
            decimal parsed;
            if (decimal.TryParse(_edit.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
                Value = parsed;
            else
                SetDisplay();
        }

        void SetDisplay()
        {
            _edit.Text = _value.ToString("F" + _decimals.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(StudioTheme.PanelRaised);
            using (var pen = new Pen(StudioTheme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            base.OnPaint(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _edit.Enabled = Enabled;
            _up.Enabled = Enabled;
            _down.Enabled = Enabled;
            base.OnEnabledChanged(e);
        }
    }
}
