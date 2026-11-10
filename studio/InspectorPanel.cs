// InspectorPanel.cs —— 右侧上下文检查器：音符、声部与电机物理参数

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FanStudio
{
    public class InspectorPanel : UserControl
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        readonly FlowLayoutPanel _body;
        EditorDocument _doc;
        EditorVoice _voice;
        List<EditorNote> _notes = new List<EditorNote>();
        Motor _libraryMotor;
        bool _filling;

        public event EventHandler<DocumentEditEventArgs> DocumentEdited;
        public event EventHandler<VoiceEventArgs> VoiceSelected;
        public event EventHandler<VoiceEventArgs> AuditionRequested;
        public event EventHandler<VoiceEventArgs> DeleteVoiceRequested;
        public event EventHandler<MotorLibraryEventArgs> AddMotorRequested;

        public InspectorPanel()
        {
            BackColor = StudioTheme.Panel;
            var header = new SectionHeader();
            header.Text = "检查器";
            header.Detail = "上下文";
            header.Dock = DockStyle.Top;
            Controls.Add(header);

            _body = new FlowLayoutPanel();
            _body.Dock = DockStyle.Fill;
            _body.FlowDirection = FlowDirection.TopDown;
            _body.WrapContents = false;
            _body.AutoScroll = true;
            _body.BackColor = StudioTheme.Panel;
            _body.Padding = new Padding(13, 8, 9, 14);
            _body.Resize += delegate { ResizeChildren(); };
            _body.HandleCreated += delegate { StudioTheme.DarkenNativeScrollbars(_body); };
            _body.Layout += delegate { StudioTheme.HideNativeScrollbars(_body); };
            _body.MouseWheel += delegate { BeginInvoke((MethodInvoker)delegate { StudioTheme.HideNativeScrollbars(_body); }); };
            Controls.Add(_body);
            _body.BringToFront();
        }

        void ResizeChildren()
        {
            int w = Math.Max(140, _body.ClientSize.Width - 27);
            foreach (Control c in _body.Controls) c.Width = w;
        }

        public void ShowSelection(EditorDocument doc, EditorVoice voice, List<EditorNote> selected)
        {
            _doc = doc;
            _voice = voice;
            _libraryMotor = null;
            _notes = selected == null ? new List<EditorNote>() : new List<EditorNote>(selected);
            Build();
        }

        public void ShowLibraryMotor(Motor motor)
        {
            _libraryMotor = motor;
            _voice = null;
            _notes.Clear();
            Build();
        }

        void Build()
        {
            _filling = true;
            _body.SuspendLayout();
            _body.Controls.Clear();

            AddVoicePicker();
            if (_libraryMotor != null) BuildLibrary(_libraryMotor);
            else if (_voice == null) AddEmpty();
            else
            {
                if (_notes.Count == 1) BuildNote(_notes[0]);
                else if (_notes.Count > 1) BuildMultiSelection();
                else BuildVoice();
                AddSpace(4);
                BuildMotor(_voice);
            }
            _body.ResumeLayout();
            ResizeChildren();
            StudioTheme.HideNativeScrollbars(_body);
            _filling = false;
        }

        void AddVoicePicker()
        {
            if (_doc == null) return;
            var wrap = new Panel();
            wrap.Height = 52;
            wrap.BackColor = Color.Transparent;
            wrap.Paint += delegate(object s, PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, "当前声部", StudioTheme.UiSmall,
                    new Point(0, 2), StudioTheme.Muted);
            };
            var picker = new StudioButton();
            picker.Text = _voice == null ? "选择声部  ▼" :
                _voice.Label + "  ·  " + _voice.ModelText + "  ▼";
            picker.Bounds = new Rectangle(0, 22, wrap.Width, 28);
            picker.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            picker.Click += delegate
            {
                var menu = new ContextMenuStrip();
                menu.ShowImageMargin = false;
                menu.BackColor = StudioTheme.PanelRaised;
                menu.ForeColor = StudioTheme.Text;
                menu.Font = StudioTheme.Ui;
                foreach (var v in _doc.Voices)
                {
                    var item = new ToolStripMenuItem(v.Label + "  ·  " + v.ModelText);
                    item.Tag = v;
                    item.BackColor = StudioTheme.PanelRaised;
                    item.ForeColor = StudioTheme.Text;
                    item.Click += delegate(object itemSender, EventArgs itemArgs)
                    {
                        _voice = (EditorVoice)((ToolStripMenuItem)itemSender).Tag;
                        _notes.Clear();
                        if (VoiceSelected != null) VoiceSelected(this, new VoiceEventArgs(_voice));
                    };
                    menu.Items.Add(item);
                }
                menu.Show(picker, new Point(0, picker.Height));
            };
            wrap.Controls.Add(picker);
            Add(wrap);
        }

        void AddEmpty()
        {
            AddHeading("没有选中内容");
            AddText("在卷帘里点一个音符，或从左边的电机库拖一台电机进来。", StudioTheme.Muted, 42);
            AddKeyboardHints();
        }

        void BuildLibrary(Motor m)
        {
            AddHeading("库型号");
            var grid = Grid();
            Row(grid, "型号", m.Model);
            Row(grid, "族系", m.Family);
            Row(grid, "机理", m.Semantics);
            Row(grid, "角色", m.Role);
            Row(grid, "音域", HzName(m.LoHz) + " – " + HzName(m.HiHz));
            Row(grid, "最短音符", (3.5 * m.Glide / 0.5).ToString("0.00", CI) + " 拍 @120 BPM");
            Row(grid, "惯性 τ", (m.Glide * 1000).ToString("0", CI) + " ms");
            Add(grid);
            AddText(m.Note, StudioTheme.Muted, 58);
            var add = Button("拖入卷帘 / 双击添加到当前拍位");
            add.Accent = true;
            add.Click += delegate
            {
                if (AddMotorRequested != null) AddMotorRequested(this, new MotorLibraryEventArgs(m));
            };
            Add(add, 36);
        }

        void BuildNote(EditorNote n)
        {
            AddHeading("选中的音符");
            var grid = Grid();
            Row(grid, "电机", _voice.Label + " · " + _voice.ModelText);
            Row(grid, "音高", ScoreParser.MidiToName(n.Midi) + "  (MIDI " + n.Midi + ")");

            var pitch = Number("音高", n.Midi, 0, 127, 0, 1);
            var start = Number("起始拍", (decimal)n.StartBeat, 0, 100000, 2, 0.25M);
            var length = Number("长度", (decimal)n.Beats, 0.05M, 10000, 3, 0.25M);
            Add(pitch.Parent, 29);
            Add(start.Parent, 29);
            Add(length.Parent, 29);

            pitch.ValueChanged += delegate
            {
                if (_filling) return;
                string before = _doc.Serialize();
                n.Midi = (int)pitch.Value;
                _doc.Normalize();
                Commit(before);
            };
            start.ValueChanged += delegate
            {
                if (_filling) return;
                string before = _doc.Serialize();
                n.StartBeat = (double)start.Value;
                _doc.Normalize();
                Commit(before);
            };
            length.ValueChanged += delegate
            {
                if (_filling) return;
                string before = _doc.Serialize();
                n.Beats = (double)length.Value;
                _doc.Normalize();
                Commit(before);
            };

            AddSpace(7);
            bool shortNote = n.Beats + 1e-6 < _voice.MinimumBeats(_doc.Bpm);
            bool badRange = n.LastWarn.Length > 0;
            string message;
            if (badRange) message = "✕ 超出 " + _voice.Label + " 的音域 · " + n.LastWarn;
            else if (shortNote) message = "✕ 音符过短 · 至少 " +
                _voice.MinimumBeats(_doc.Bpm).ToString("0.00", CI) + " 拍才来得及换速";
            else message = "✓ 在音域内，长度达标";
            AddNotice(message, badRange || shortNote);

            AddSpace(8);
            var derived = Grid();
            Row(derived, "谐波", n.LastHarm.ToString(CI));
            Row(derived, "叶片通过频率", n.LastFb.ToString("0.0", CI) + " Hz");
            Row(derived, "目标转速", n.LastRpm.ToString("0", CI) + " rpm");
            Row(derived, "占空比", (n.LastDuty * 100).ToString("0.0", CI) + "%");
            Add(derived);
        }

        void BuildMultiSelection()
        {
            AddHeading("已选 " + _notes.Count + " 个音符");
            int lo = 127, hi = 0;
            double first = double.MaxValue, last = 0;
            foreach (var n in _notes)
            {
                lo = Math.Min(lo, n.Midi);
                hi = Math.Max(hi, n.Midi);
                first = Math.Min(first, n.StartBeat);
                last = Math.Max(last, n.EndBeat);
            }
            var grid = Grid();
            Row(grid, "音域", ScoreParser.MidiToName(lo) + " – " + ScoreParser.MidiToName(hi));
            Row(grid, "拍位", first.ToString("0.##", CI) + " – " + last.ToString("0.##", CI));
            Row(grid, "操作", "拖动整体移动，Delete 删除");
            Add(grid);
            AddText("方向键微调：左右 1/4 拍，上下 1 个半音。Shift+左右改变长度。",
                StudioTheme.Muted, 38);
        }

        void BuildVoice()
        {
            AddHeading("声部");
            var name = new TextBox();
            name.Text = _voice.Label;
            name.Font = StudioTheme.Ui;
            name.BackColor = StudioTheme.PanelRaised;
            name.ForeColor = StudioTheme.Text;
            name.BorderStyle = BorderStyle.FixedSingle;
            name.Leave += delegate
            {
                if (_filling || name.Text.Trim() == _voice.Label) return;
                string before = _doc.Serialize();
                string wanted = name.Text.Trim();
                if (wanted.Length == 0) wanted = _voice.Motor.Key;
                _voice.Label = wanted;
                Commit(before);
            };
            Add(name, 27);
            AddSpace(5);
            var grid = Grid();
            Row(grid, "型号", _voice.ModelText);
            Row(grid, "音域", ScoreParser.MidiToName((int)Math.Round(_voice.LoMidi)) + " – " +
                ScoreParser.MidiToName((int)Math.Round(_voice.HiMidi)));
            Row(grid, "音符", _voice.Notes.Count.ToString(CI));
            Add(grid);
            var actions = new FlowLayoutPanel();
            actions.Height = 34;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.BackColor = Color.Transparent;
            var audition = new StudioButton();
            audition.Text = "试听声部";
            audition.Compact = true;
            audition.Width = 90;
            audition.Height = 30;
            audition.Margin = new Padding(0, 0, 7, 0);
            audition.Click += delegate
            {
                if (AuditionRequested != null) AuditionRequested(this, new VoiceEventArgs(_voice));
            };
            var remove = new StudioButton();
            remove.Text = "删除声部";
            remove.Compact = true;
            remove.Danger = true;
            remove.Width = 90;
            remove.Height = 30;
            remove.Click += delegate
            {
                if (DeleteVoiceRequested != null) DeleteVoiceRequested(this, new VoiceEventArgs(_voice));
            };
            actions.Controls.Add(audition);
            actions.Controls.Add(remove);
            Add(actions, 34);
        }

        void BuildMotor(EditorVoice v)
        {
            AddHeading("电机参数");
            Motor m = v.Motor;
            var p = Number("每转事件数", m.Blades, 1, 4096, 0, 1);
            var harm = Number("谐波次数", (decimal)m.Harmonic, 1, 8, 2, 0.25M);
            var min = Number("最低转速 rpm", (decimal)m.MinRpm, 0, 60000, 0, 50);
            var max = Number("最高转速 rpm", (decimal)m.MaxRpm, 1, 60000, 0, 50);
            var start = Number("启动占空比", (decimal)m.StartDuty, 0.01M, 1, 3, 0.01M);
            var glide = Number("惯性 τ (ms)", (decimal)(m.Glide * 1000), 1, 2000, 1, 5);
            var timbre = Number("谐波衰减", (decimal)m.Timbre, 0.2M, 3, 2, 0.05M);
            var noiseC = Number("噪声中心 Hz", (decimal)m.NoiseCenter, 20, 40000, 0, 50);
            var noiseG = Number("噪声量", (decimal)m.NoiseGain, 0, 3, 2, 0.05M);
            var pan = Number("声像 -1…1", (decimal)m.Pan, -1, 1, 2, 0.05M);
            var gain = Number("音量", (decimal)m.Gain, 0, 2, 2, 0.05M);
            var delay = Number("摆位延迟 ms", (decimal)m.DelayMs, 0, 200, 1, 1);
            NumericPair[] all = { p, harm, min, max, start, glide, timbre, noiseC, noiseG, pan, gain, delay };
            foreach (var num in all) Add(num.Parent, 29);

            p.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "blades"); };
            harm.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "harmonic"); };
            min.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "min"); };
            max.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "max"); };
            start.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "start"); };
            glide.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "glide"); };
            timbre.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "timbre"); };
            noiseC.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "noisec"); };
            noiseG.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "noiseg"); };
            pan.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "pan"); };
            gain.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "gain"); };
            delay.ValueChanged += delegate { if (!_filling) ApplyMotor(m, v, before(m, v), "delay"); };

            AddText("参数直接改变物理模型。改动会进入撤销历史，并立即重算音域、换速时间与风险。",
                StudioTheme.Dim, 40);
        }

        string before(Motor m, EditorVoice v)
        {
            return _doc.Serialize();
        }

        void ApplyMotor(Motor m, EditorVoice v, string before, string field)
        {
            DarkNumeric source = null;
            // 找到刚刚变化的控件：按字段直接从当前值读取，避免事件里再次依赖 sender。
            foreach (Control top in _body.Controls)
            {
                TableLayoutPanel table = top as TableLayoutPanel;
                if (table == null || table.Controls.Count < 2) continue;
                string label = table.Controls[0].Text;
                if (field == "blades" && label == "每转事件数") source = table.Controls[1] as DarkNumeric;
                if (field == "harmonic" && label == "谐波次数") source = table.Controls[1] as DarkNumeric;
                if (field == "min" && label == "最低转速 rpm") source = table.Controls[1] as DarkNumeric;
                if (field == "max" && label == "最高转速 rpm") source = table.Controls[1] as DarkNumeric;
                if (field == "start" && label == "启动占空比") source = table.Controls[1] as DarkNumeric;
                if (field == "glide" && label == "惯性 τ (ms)") source = table.Controls[1] as DarkNumeric;
                if (field == "timbre" && label == "谐波衰减") source = table.Controls[1] as DarkNumeric;
                if (field == "noisec" && label == "噪声中心 Hz") source = table.Controls[1] as DarkNumeric;
                if (field == "noiseg" && label == "噪声量") source = table.Controls[1] as DarkNumeric;
                if (field == "pan" && label == "声像 -1…1") source = table.Controls[1] as DarkNumeric;
                if (field == "gain" && label == "音量") source = table.Controls[1] as DarkNumeric;
                if (field == "delay" && label == "摆位延迟 ms") source = table.Controls[1] as DarkNumeric;
            }
            if (source == null) return;
            decimal d = source.Value;
            if (field == "blades") m.Blades = (int)d;
            else if (field == "harmonic") m.Harmonic = (double)d;
            else if (field == "min") m.MinRpm = (double)d;
            else if (field == "max") m.MaxRpm = (double)d;
            else if (field == "start") m.StartDuty = (double)d;
            else if (field == "glide") m.Glide = (double)d / 1000.0;
            else if (field == "timbre") m.Timbre = (double)d;
            else if (field == "noisec") m.NoiseCenter = (double)d;
            else if (field == "noiseg") m.NoiseGain = (double)d;
            else if (field == "pan") { m.Pan = (double)d; v.LayoutExplicit = true; }
            else if (field == "gain") { m.Gain = (double)d; v.LayoutExplicit = true; }
            else if (field == "delay") { m.DelayMs = (double)d; v.LayoutExplicit = true; }
            _doc.BuildScore();
            Commit(before);
        }

        void Commit(string before)
        {
            if (DocumentEdited != null) DocumentEdited(this, new DocumentEditEventArgs(before));
        }

        void AddKeyboardHints()
        {
            AddSpace(8);
            AddHeading("快捷键");
            AddText("Space 播放/停止    F5 渲染并播放\nCtrl+S 保存    Ctrl+Shift+S 另存为\nCtrl+F 搜索电机库    Ctrl+I 导入 MIDI\nCtrl+Z/Y 撤销/重做    Ctrl+A 全选    Ctrl+D 复制\nCtrl+上下 移八度    Home/End 播放头首尾\nCtrl+0 适应内容    Ctrl+1…9 切声部\n滚轮 纵向    Shift+滚轮 横向    Ctrl+滚轮 缩放\nF1 查看完整快捷键", StudioTheme.Muted, 132);
        }

        void AddHeading(string text)
        {
            var p = new Panel();
            p.Height = 31;
            p.BackColor = Color.Transparent;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, text, StudioTheme.Title,
                    new Rectangle(0, 4, p.Width, 24), StudioTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            Add(p);
        }

        void AddText(string text, Color color, int height)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = StudioTheme.UiSmall;
            l.AutoSize = false;
            l.Height = height;
            l.Margin = new Padding(0, 3, 0, 4);
            l.BackColor = Color.Transparent;
            Add(l);
        }

        void AddNotice(string text, bool bad)
        {
            var p = new Panel();
            p.Height = 36;
            p.BackColor = Color.Transparent;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                Rectangle r = new Rectangle(0, 2, p.Width - 1, p.Height - 5);
                StudioTheme.FillRound(e.Graphics, bad ? StudioTheme.DangerSoft : StudioTheme.SuccessSoft, r, 6);
                TextRenderer.DrawText(e.Graphics, text, StudioTheme.UiSmall,
                    new Rectangle(8, 2, p.Width - 14, p.Height - 5),
                    bad ? Color.FromArgb(255, 164, 168) : Color.FromArgb(111, 216, 207),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            Add(p);
        }

        TableLayoutPanel Grid()
        {
            var t = new TableLayoutPanel();
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.BackColor = Color.Transparent;
            t.Margin = new Padding(0, 4, 0, 4);
            return t;
        }

        void Row(TableLayoutPanel grid, string key, string value)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            var k = new Label();
            k.Text = key;
            k.ForeColor = StudioTheme.Muted;
            k.Font = StudioTheme.UiSmall;
            k.Dock = DockStyle.Fill;
            k.TextAlign = ContentAlignment.MiddleLeft;
            k.BackColor = Color.Transparent;
            var v = new Label();
            v.Text = value;
            v.ForeColor = StudioTheme.Text;
            v.Font = StudioTheme.UiSmall;
            v.Dock = DockStyle.Fill;
            v.TextAlign = ContentAlignment.MiddleRight;
            v.AutoEllipsis = true;
            v.BackColor = Color.Transparent;
            grid.Controls.Add(k, 0, row);
            grid.Controls.Add(v, 1, row);
        }

        NumericPair Number(string label, decimal value, decimal min, decimal max, int decimals, decimal step)
        {
            var t = new TableLayoutPanel();
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            t.RowCount = 1;
            t.Margin = new Padding(0, 1, 0, 1);
            t.BackColor = Color.Transparent;
            var l = new Label();
            l.Text = label;
            l.ForeColor = StudioTheme.Muted;
            l.Font = StudioTheme.UiSmall;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.BackColor = Color.Transparent;
            var n = new DarkNumeric();
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Increment = step;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.Dock = DockStyle.Fill;
            t.Controls.Add(l, 0, 0);
            t.Controls.Add(n, 1, 0);
            return new NumericPair(t, n, label);
        }

        StudioButton Button(string text)
        {
            var b = new StudioButton();
            b.Text = text;
            b.Height = 34;
            b.Font = StudioTheme.Ui;
            return b;
        }

        void Add(Control c) { Add(c, c.Height); }

        void Add(Control c, int height)
        {
            c.Height = height;
            c.Width = Math.Max(120, _body.ClientSize.Width - 27);
            c.Margin = new Padding(0, 2, 0, 2);
            _body.Controls.Add(c);
        }

        void AddSpace(int h)
        {
            var p = new Panel();
            p.Height = h;
            p.BackColor = Color.Transparent;
            Add(p, h);
        }

        static string HzName(double hz)
        {
            if (hz <= 0) return "—";
            double midi = 69 + 12 * Math.Log(hz / 440.0, 2);
            return ScoreParser.MidiToName((int)Math.Round(midi));
        }
    }

    public class VoiceEventArgs : EventArgs
    {
        public readonly EditorVoice Voice;
        public VoiceEventArgs(EditorVoice voice) { Voice = voice; }
    }

    class NumericPair
    {
        public readonly TableLayoutPanel Parent;
        public readonly DarkNumeric Input;
        public readonly string Label;

        public event EventHandler ValueChanged
        {
            add { Input.ValueChanged += value; }
            remove { Input.ValueChanged -= value; }
        }

        public decimal Value
        {
            get { return Input.Value; }
            set { Input.Value = value; }
        }

        public NumericPair(TableLayoutPanel parent, DarkNumeric input, string label)
        {
            Parent = parent;
            Input = input;
            Label = label;
        }
    }
}
