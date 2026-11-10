// TimelineControl.cs —— 钢琴卷帘时间线、命中测试、拖动/拉伸/框选与物理诊断

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace FanStudio
{
    public class TimelineControl : UserControl
    {
        EditorDocument _doc;
        EditorVoice _voice;
        readonly List<EditorNote> _selected = new List<EditorNote>();
        readonly DarkScrollBar _vbar;
        readonly DarkScrollBar _hbar;
        double _playheadBeat;
        bool _playing;
        double _zoom = 1.0;
        int _topMidi = 88;
        int _dragStartMidi;
        double _dragStartBeat;
        Point _mouseDown;
        DragMode _dragMode;
        EditorNote _activeNote;
        Rectangle _marquee;
        string _dragBefore;
        int _rowHeight = 12;
        int _hoverMidi = -1;
        Point _hoverPoint;
        EditorNote _hoverNote;
        EditorVoice _hoverVoice;
        readonly List<NoteOrigin> _origins = new List<NoteOrigin>();

        const int KeyWidth = 58;
        const int RulerHeight = 29;
        const double MinBeats = 0.25;

        enum DragMode { None, Move, Resize, Marquee, Playhead }

        public event EventHandler SelectionChanged;
        public event EventHandler<DocumentEditEventArgs> DocumentChanged;
        public event EventHandler PlayheadMoved;
        public event EventHandler<MotorDropEventArgs> MotorDropped;
        public event EventHandler<StatusEventArgs> StatusRequested;
        public event EventHandler<MidiEventArgs> PitchAuditionRequested;

        public EditorVoice SelectedVoice { get { return _voice; } }
        public List<EditorNote> SelectedNotes { get { return _selected; } }
        public double PlayheadBeat { get { return _playheadBeat; } }
        public double BeatWidth { get { return 50.0 * _zoom; } }
        public int HorizontalOffset { get { return _hbar.Value; } }
        public int TimelineLeft { get { return KeyWidth; } }
        public int TopMidi { get { return _topMidi; } }
        public int VisibleRows { get { return Math.Max(1, (ContentRect.Height) / VisibleRowHeight); } }
        int VisibleRowHeight { get { return Math.Max(8, Math.Min(22, _rowHeight)); } }

        public TimelineControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            BackColor = StudioTheme.Canvas;
            AllowDrop = true;
            TabStop = true;

            _vbar = new DarkScrollBar();
            _vbar.Dock = DockStyle.Right;
            _vbar.Width = 12;
            _vbar.Minimum = 0;
            _vbar.Maximum = 127;
            _vbar.LargeChange = 20;
            _vbar.ValueChanged += delegate { _topMidi = 127 - _vbar.Value; Invalidate(); };
            Controls.Add(_vbar);

            _hbar = new DarkScrollBar();
            _hbar.Orientation = Orientation.Horizontal;
            _hbar.Dock = DockStyle.Bottom;
            _hbar.Height = 12;
            _hbar.Minimum = 0;
            _hbar.LargeChange = 200;
            _hbar.ValueChanged += delegate { Invalidate(); };
            Controls.Add(_hbar);

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.Text)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string key = e.Data.GetData(DataFormats.Text) as string;
                if (key == null) return;
                Point p = PointToClient(new Point(e.X, e.Y));
                Rectangle content = ContentRect;
                if (!content.Contains(p)) return;
                double beat = SnapBeat(XToBeat(p.X));
                int midi = YToMidi(p.Y);
                if (MotorDropped != null)
                    MotorDropped(this, new MotorDropEventArgs(key, beat, midi));
            };
            Resize += delegate { UpdateScrollbars(); };
            MouseWheel += OnTimelineWheel;
        }

        Rectangle ContentRect
        {
            get
            {
                int w = Math.Max(1, ClientSize.Width - _vbar.Width);
                int h = Math.Max(1, ClientSize.Height - _hbar.Height);
                return new Rectangle(KeyWidth, RulerHeight, Math.Max(1, w - KeyWidth),
                    Math.Max(1, h - RulerHeight));
            }
        }

        void OnTimelineWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                double beatAt = XToBeat(e.X);
                _zoom = Math.Max(0.45, Math.Min(2.8, _zoom * (e.Delta > 0 ? 1.18 : 0.85)));
                _rowHeight = Math.Max(8, Math.Min(22, (int)Math.Round(12.0 * _zoom)));
                UpdateScrollbars();
                int newX = (int)Math.Round(TimelineLeft + beatAt * BeatWidth);
                _hbar.Value = Math.Max(_hbar.Minimum, Math.Min(ScrollMax(_hbar),
                    _hbar.Value + newX - e.X));
                Invalidate();
                return;
            }
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                _hbar.Value = Math.Max(_hbar.Minimum, Math.Min(ScrollMax(_hbar),
                    _hbar.Value - e.Delta));
            }
            else
            {
                _vbar.Value = Math.Max(_vbar.Minimum, Math.Min(ScrollMax(_vbar),
                    _vbar.Value - e.Delta / 4));
            }
        }

        public void SetDocument(EditorDocument doc)
        {
            _doc = doc;
            _selected.Clear();
            _voice = (doc != null && doc.Voices.Count > 0) ? doc.Voices[0] : null;
            _playheadBeat = 0;
            UpdateScrollbars();
            Invalidate();
        }

        public void SetSelectedVoice(EditorVoice voice)
        {
            _voice = voice;
            _selected.Clear();
            ScrollToVoice(voice);
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            Invalidate();
        }

        public void SetPlaying(bool playing)
        {
            _playing = playing;
            Invalidate();
        }

        public void SetPlayhead(double beat, bool notify)
        {
            _playheadBeat = Math.Max(0, beat);
            EnsurePlayheadVisible();
            Invalidate();
            if (notify && PlayheadMoved != null) PlayheadMoved(this, EventArgs.Empty);
        }

        public void SelectAll()
        {
            _selected.Clear();
            if (_doc != null)
                foreach (var v in _doc.Voices)
                    _selected.AddRange(v.Notes);
            NotifySelection();
        }

        public void ClearSelection()
        {
            _selected.Clear();
            NotifySelection();
        }

        public void SelectNote(EditorVoice voice, EditorNote note)
        {
            bool changedVoice = _voice != voice;
            _voice = voice;
            _selected.Clear();
            if (note != null) _selected.Add(note);
            if (changedVoice) ScrollToVoice(voice);
            NotifySelection();
            Invalidate();
        }

        public void DeleteSelection()
        {
            if (_doc == null || _selected.Count == 0) return;
            string before = _doc.Serialize();
            foreach (var v in _doc.Voices)
                foreach (var n in new List<EditorNote>(_selected))
                    v.Notes.Remove(n);
            _selected.Clear();
            _doc.Normalize();
            FinishEdit(before);
            NotifySelection();
        }

        public void DuplicateSelection()
        {
            if (_doc == null || _selected.Count == 0) return;
            string before = _doc.Serialize();
            var copies = new List<EditorNote>();
            foreach (var v in _doc.Voices)
            {
                foreach (var n in v.Notes)
                {
                    if (!_selected.Contains(n)) continue;
                    var c = n.Copy();
                    c.StartBeat = n.EndBeat;
                    copies.Add(c);
                    v.Notes.Add(c);
                }
            }
            _selected.Clear();
            _selected.AddRange(copies);
            _doc.Normalize();
            FinishEdit(before);
            NotifySelection();
        }

        public void NudgeSelection(double beats, int semitones)
        {
            if (_doc == null || _selected.Count == 0) return;
            string before = _doc.Serialize();
            double minStart = double.MaxValue;
            int minMidi = int.MaxValue, maxMidi = int.MinValue;
            foreach (var n in _selected)
            {
                minStart = Math.Min(minStart, n.StartBeat);
                minMidi = Math.Min(minMidi, n.Midi);
                maxMidi = Math.Max(maxMidi, n.Midi);
            }
            if (minStart + beats < 0) beats = -minStart;
            if (minMidi + semitones < 0) semitones = -minMidi;
            if (maxMidi + semitones > 127) semitones = 127 - maxMidi;
            foreach (var n in _selected)
            {
                n.StartBeat = Math.Max(0, n.StartBeat + beats);
                n.Midi = Math.Max(0, Math.Min(127, n.Midi + semitones));
            }
            _doc.Normalize();
            FinishEdit(before);
        }

        public void ZoomIn()
        {
            _zoom = Math.Min(2.8, _zoom * 1.2);
            _rowHeight = Math.Max(8, Math.Min(22, (int)Math.Round(12.0 * _zoom)));
            UpdateScrollbars();
            Invalidate();
        }

        public void ZoomOut()
        {
            _zoom = Math.Max(0.45, _zoom / 1.2);
            _rowHeight = Math.Max(8, Math.Min(22, (int)Math.Round(12.0 * _zoom)));
            UpdateScrollbars();
            Invalidate();
        }

        public void Fit()
        {
            if (_doc == null) return;
            double firstBeat = double.MaxValue, lastBeat = 0;
            int lowMidi = 127, highMidi = 0;
            foreach (var v in _doc.Voices)
                foreach (var n in v.Notes)
                {
                    firstBeat = Math.Min(firstBeat, n.StartBeat);
                    lastBeat = Math.Max(lastBeat, n.EndBeat);
                    lowMidi = Math.Min(lowMidi, n.Midi);
                    highMidi = Math.Max(highMidi, n.Midi);
                }
            if (lowMidi > highMidi)
            {
                lowMidi = 48; highMidi = 84;
                firstBeat = 0; lastBeat = 16;
            }
            double beatSpan = Math.Max(4, lastBeat - firstBeat + 1);
            _zoom = Math.Max(0.45, Math.Min(2.8, (ContentRect.Width - 28) / (beatSpan * 50.0)));
            int midiSpan = Math.Max(8, highMidi - lowMidi + 5);
            _rowHeight = Math.Max(9, Math.Min(22, ContentRect.Height / midiSpan));
            int centerMidi = (lowMidi + highMidi) / 2;
            int top = centerMidi + VisibleRows / 2;
            _topMidi = Math.Max(VisibleRows - 1, Math.Min(127, top));
            _vbar.Value = Math.Max(0, 127 - _topMidi);
            UpdateScrollbars();
            double firstBar = Math.Floor(Math.Max(0, firstBeat) / 4.0) * 4.0;
            _hbar.Value = Math.Max(0, (int)Math.Round(firstBar * BeatWidth));
            Invalidate();
        }

        public void RefreshDiagnostics()
        {
            if (_doc != null) _doc.BuildScore();
            Invalidate();
        }

        public void SetHoverForSnapshot(int midi)
        {
            _hoverMidi = midi;
            _hoverNote = null;
            _hoverVoice = _voice;
            Rectangle content = ContentRect;
            _hoverPoint = new Point(content.Left + content.Width / 2,
                MidiToY(midi) + VisibleRowHeight / 2);
            Invalidate();
        }

        public void ScrollToVoice(EditorVoice voice)
        {
            if (voice == null) return;
            int low = (int)Math.Floor(voice.LoMidi);
            int high = (int)Math.Ceiling(voice.HiMidi);
            if (high < low) return;
            int bottomMidi = _topMidi - VisibleRows + 1;
            if (low >= bottomMidi && high <= _topMidi) return;
            int center = (low + high) / 2;
            int top = Math.Max(VisibleRows - 1, Math.Min(127, center + VisibleRows / 2));
            _vbar.Value = Math.Max(0, 127 - top);
            _topMidi = 127 - _vbar.Value;
            Invalidate();
        }

        void UpdateScrollbars()
        {
            if (_doc != null)
            {
                double beats = Math.Max(16, _doc.DurationBeats + 4);
                int maxH = Math.Max(0, (int)Math.Ceiling(beats * BeatWidth) - ContentRect.Width + 30);
                _hbar.Maximum = maxH + Math.Max(1, ContentRect.Width);
                _hbar.LargeChange = Math.Max(20, ContentRect.Width);
                if (_hbar.Value > ScrollMax(_hbar)) _hbar.Value = ScrollMax(_hbar);
            }
            _vbar.LargeChange = Math.Max(5, VisibleRows);
            if (_vbar.Value > ScrollMax(_vbar)) _vbar.Value = ScrollMax(_vbar);
        }

        static int ScrollMax(DarkScrollBar bar)
        {
            return Math.Max(bar.Minimum, bar.Maximum - bar.LargeChange + 1);
        }

        void EnsurePlayheadVisible()
        {
            int x = (int)(TimelineLeft + _playheadBeat * BeatWidth - _hbar.Value);
            if (x < TimelineLeft + 12) _hbar.Value = Math.Max(0, (int)(_playheadBeat * BeatWidth) - 30);
            else if (x > ClientSize.Width - _vbar.Width - 30)
                _hbar.Value = Math.Min(ScrollMax(_hbar),
                    (int)(_playheadBeat * BeatWidth - ContentRect.Width + 80));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(StudioTheme.Canvas);
            Rectangle content = ContentRect;
            DrawRuler(g, content);
            DrawGrid(g, content);
            DrawSoundRange(g, content);
            DrawNotes(g, content);
            DrawPlayhead(g, content);
            DrawHoverInfo(g, content);
            DrawMarquee(g);
            using (var pen = new Pen(StudioTheme.Border))
            {
                g.DrawLine(pen, content.Left, content.Top, content.Right, content.Top);
                g.DrawLine(pen, content.Left, content.Top, content.Left, content.Bottom);
            }
            if (_doc == null || _doc.Voices.Count == 0)
            {
                string text = "把左侧电机拖进这里，或双击创建声部";
                Size s = TextRenderer.MeasureText(text, StudioTheme.Ui);
                TextRenderer.DrawText(g, text, StudioTheme.Ui,
                    new Point(content.Left + (content.Width - s.Width) / 2,
                        content.Top + content.Height / 2 - 10), StudioTheme.Dim);
            }
        }

        void DrawRuler(Graphics g, Rectangle content)
        {
            using (var brush = new SolidBrush(StudioTheme.Panel))
                g.FillRectangle(brush, 0, 0, ClientSize.Width, RulerHeight);
            using (var brush = new SolidBrush(Color.FromArgb(25, 27, 32)))
                g.FillRectangle(brush, 0, 0, KeyWidth, RulerHeight);
            TextRenderer.DrawText(g, "4/4", StudioTheme.UiSmall,
                new Rectangle(0, 0, KeyWidth - 8, RulerHeight), StudioTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            int firstBeat = Math.Max(0, (int)Math.Floor(_hbar.Value / BeatWidth));
            int lastBeat = firstBeat + (int)Math.Ceiling(content.Width / BeatWidth) + 2;
            for (int b = firstBeat; b <= lastBeat; b++)
            {
                int x = XForBeat(b);
                if (x < KeyWidth - 1 || x > content.Right) continue;
                Color color = b % 4 == 0 ? StudioTheme.BorderStrong : StudioTheme.Border;
                using (var pen = new Pen(color))
                    g.DrawLine(pen, x, b % 4 == 0 ? 18 : 22, x, RulerHeight - 1);
                if (b % 4 == 0)
                {
                    TextRenderer.DrawText(g, (b / 4 + 1).ToString(CultureInfo.InvariantCulture),
                        StudioTheme.UiSmall, new Point(x + 5, 5), StudioTheme.Muted);
                }
            }
            using (var pen = new Pen(StudioTheme.Border))
                g.DrawLine(pen, 0, RulerHeight - 1, content.Right, RulerHeight - 1);
        }

        void DrawGrid(Graphics g, Rectangle content)
        {
            using (var brush = new SolidBrush(Color.FromArgb(26, 28, 33)))
                g.FillRectangle(brush, content);
            using (var brush = new SolidBrush(Color.FromArgb(25, 27, 32)))
                g.FillRectangle(brush, 0, content.Top, KeyWidth, content.Height);
            for (int row = 0; row < VisibleRows + 1; row++)
            {
                int midi = _topMidi - row;
                if (midi < 0 || midi > 127) continue;
                int y = content.Top + row * VisibleRowHeight;
                bool black = IsBlackKey(midi);
                Color rowColor = black ? Color.FromArgb(19, 21, 25) : Color.FromArgb(29, 31, 37);
                using (var brush = new SolidBrush(rowColor))
                    g.FillRectangle(brush, content.Left, y, content.Width, VisibleRowHeight);
                using (var brush = new SolidBrush(black ? Color.FromArgb(20, 22, 26) :
                    Color.FromArgb(28, 30, 35)))
                    g.FillRectangle(brush, 0, y, KeyWidth, VisibleRowHeight);
                if (black)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(38, 42, 50)))
                        g.FillRectangle(brush, KeyWidth - 5, y + 2, 3, Math.Max(2, VisibleRowHeight - 4));
                }
                if (midi % 12 == 0)
                {
                    using (var pen = new Pen(Color.FromArgb(88, 94, 108)))
                        g.DrawLine(pen, content.Left, y, content.Right, y);
                }
                else
                {
                    using (var pen = new Pen(black ? Color.FromArgb(33, 36, 43) :
                        Color.FromArgb(43, 46, 54)))
                        g.DrawLine(pen, content.Left, y, content.Right, y);
                }
                bool inRange = _voice != null && midi + 0.01 >= _voice.LoMidi &&
                    midi - 0.01 <= _voice.HiMidi;
                bool rangeEdge = _voice != null &&
                    (midi == (int)Math.Floor(_voice.LoMidi) || midi == (int)Math.Ceiling(_voice.HiMidi));
                bool showLabel = midi % 12 == 0 || VisibleRowHeight >= 13 || rangeEdge;
                if (showLabel)
                {
                    Color label = (inRange || rangeEdge)
                        ? (_voice == null ? StudioTheme.Text : StudioTheme.MotorColor(_voice.Motor))
                        : StudioTheme.Dim;
                    TextRenderer.DrawText(g, ScoreParser.MidiToName(midi), StudioTheme.MonoSmall,
                        new Rectangle(2, y, KeyWidth - 9, VisibleRowHeight), label,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            using (var pen = new Pen(StudioTheme.BorderStrong))
                g.DrawLine(pen, KeyWidth - 1, content.Top, KeyWidth - 1, content.Bottom);

            int firstBeat = Math.Max(0, (int)Math.Floor(_hbar.Value / BeatWidth));
            int lastBeat = firstBeat + (int)Math.Ceiling(content.Width / BeatWidth) + 1;
            for (int b = firstBeat; b <= lastBeat; b++)
            {
                int x = XForBeat(b);
                if (x < content.Left || x > content.Right) continue;
                using (var pen = new Pen(b % 4 == 0 ? Color.FromArgb(74, 79, 91) :
                    Color.FromArgb(44, 47, 55)))
                    g.DrawLine(pen, x, content.Top, x, content.Bottom);
            }
            if (BeatWidth >= 68)
            {
                for (double b = firstBeat; b <= lastBeat; b += 0.5)
                {
                    int x = XForBeat(b);
                    if (x < content.Left || x > content.Right) continue;
                    if (Math.Abs(b - Math.Round(b)) < 0.01) continue;
                    using (var pen = new Pen(Color.FromArgb(34, 36, 42)))
                        g.DrawLine(pen, x, content.Top, x, content.Bottom);
                }
            }
        }

        void DrawSoundRange(Graphics g, Rectangle content)
        {
            if (_voice == null) return;
            Color color = StudioTheme.MotorColor(_voice.Motor);
            int yTop = MidiToY((int)Math.Ceiling(_voice.HiMidi));
            int yBottom = MidiToY((int)Math.Floor(_voice.LoMidi)) + VisibleRowHeight;
            yTop = Math.Max(content.Top, yTop);
            yBottom = Math.Min(content.Bottom, yBottom);
            if (yBottom <= yTop) return;
            using (var brush = new SolidBrush(Color.FromArgb(22, color)))
                g.FillRectangle(brush, content.Left, yTop, content.Width, yBottom - yTop);
            using (var pen = new Pen(Color.FromArgb(115, color)))
            {
                g.DrawLine(pen, content.Left, yTop, content.Right, yTop);
                g.DrawLine(pen, content.Left, yBottom - 1, content.Right, yBottom - 1);
            }
            string text = _voice.Label + " 可唱 " + ScoreParser.MidiToName((int)Math.Round(_voice.LoMidi)) +
                "–" + ScoreParser.MidiToName((int)Math.Round(_voice.HiMidi));
            Size s = TextRenderer.MeasureText(text, StudioTheme.UiSmall);
            if (content.Width > s.Width + 24 && yTop + 22 < content.Bottom)
            {
                Rectangle r = new Rectangle(Math.Max(content.Left + 9, content.Right - s.Width - 18),
                    Math.Max(content.Top + 4, yTop + 4),
                    s.Width + 12, 20);
                if (r.Y + r.Height < yBottom)
                {
                    StudioTheme.FillRound(g, color, r, 4);
                    TextRenderer.DrawText(g, text, StudioTheme.UiSmall, r, StudioTheme.Canvas,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        void DrawNotes(Graphics g, Rectangle content)
        {
            if (_doc == null) return;
            for (int vi = 0; vi < _doc.Voices.Count; vi++)
            {
                EditorVoice v = _doc.Voices[vi];
                Color color = StudioTheme.MotorColor(v.Motor);
                foreach (var n in v.Notes)
                {
                    Rectangle r = NoteRect(n);
                    if (r.Right < content.Left || r.Left > content.Right ||
                        r.Bottom < content.Top || r.Top > content.Bottom) continue;
                    r = Rectangle.Intersect(r, content);
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    bool selected = _selected.Contains(n);
                    int alpha = _voice != null && _voice != v ? 150 : 255;
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
                        g.FillRectangle(brush, r);
                    bool shortNote = n.Beats + 1e-6 < v.MinimumBeats(_doc.Bpm);
                    bool invalid = n.LastWarn.Length > 0 || shortNote;
                    if (invalid)
                    {
                        using (var pen = new Pen(StudioTheme.Danger, 1.4f))
                            g.DrawRectangle(pen, r.X, r.Y, Math.Max(1, r.Width - 1), Math.Max(1, r.Height - 1));
                    }
                    if (selected)
                    {
                        using (var pen = new Pen(Color.White, 2f))
                            g.DrawRectangle(pen, r.X - 1, r.Y - 1, Math.Max(2, r.Width + 1), Math.Max(2, r.Height + 1));
                        using (var brush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                            g.FillRectangle(brush, r.Right - 4, r.Y + 1, 3, Math.Max(1, r.Height - 2));
                    }
                    Size labelSize = TextRenderer.MeasureText(v.Label, StudioTheme.MonoSmall);
                    if (r.Width >= labelSize.Width + 8 && r.Height >= 9)
                    {
                        TextRenderer.DrawText(g, v.Label, StudioTheme.MonoSmall,
                            new Rectangle(r.X + 4, r.Y, r.Width - 7, r.Height), Color.FromArgb(23, 24, 28),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }
        }

        void DrawHoverInfo(Graphics g, Rectangle content)
        {
            if (_hoverMidi < 0 || _dragMode != DragMode.None || !content.Contains(_hoverPoint)) return;
            EditorVoice voice = _hoverVoice == null ? _voice : _hoverVoice;
            if (voice == null && _doc != null && _doc.Voices.Count > 0) voice = _doc.Voices[0];
            double hz = ScoreParser.MidiToFreq(_hoverMidi, 0);
            string text = ScoreParser.MidiToName(_hoverMidi) + " · " +
                hz.ToString("0.#", CultureInfo.InvariantCulture) + " Hz";
            if (voice != null && voice.Motor != null && voice.Motor.Blades > 0)
            {
                double rpm = hz * 60.0 / (voice.Motor.Blades * Math.Max(1.0, voice.Motor.Harmonic));
                text += " · " + voice.Label + " 需 " + rpm.ToString("0", CultureInfo.InvariantCulture) + " rpm";
            }
            if (_hoverNote != null)
                text = _hoverVoice.Label + "  " + ScoreParser.MidiToName(_hoverNote.Midi) + " · " +
                    hz.ToString("0.#", CultureInfo.InvariantCulture) + " Hz" +
                    (voice != null && voice.Motor != null && voice.Motor.Blades > 0
                        ? " · " + (hz * 60.0 / (voice.Motor.Blades * Math.Max(1.0, voice.Motor.Harmonic)))
                            .ToString("0", CultureInfo.InvariantCulture) + " rpm"
                        : "");
            Size size = TextRenderer.MeasureText(text, StudioTheme.UiSmall);
            int x = Math.Min(_hoverPoint.X + 14, content.Right - size.Width - 20);
            int y = _hoverPoint.Y + 18;
            if (y + 27 > content.Bottom) y = _hoverPoint.Y - 30;
            Rectangle r = new Rectangle(Math.Max(content.Left + 2, x), Math.Max(content.Top + 2, y),
                size.Width + 14, 25);
            StudioTheme.FillRound(g, StudioTheme.PanelRaised, r, 5);
            StudioTheme.StrokeRound(g, StudioTheme.BorderStrong, r, 5);
            TextRenderer.DrawText(g, text, StudioTheme.UiSmall, r, StudioTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        void DrawPlayhead(Graphics g, Rectangle content)
        {
            int x = XForBeat(_playheadBeat);
            if (x < content.Left - 1 || x > content.Right + 1) return;
            using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 1.25f))
                g.DrawLine(pen, x, 0, x, content.Bottom);
            Point[] tri = { new Point(x - 5, 0), new Point(x + 5, 0), new Point(x, 8) };
            using (var brush = new SolidBrush(Color.White))
                g.FillPolygon(brush, tri);
            if (_playing)
            {
                using (var brush = new SolidBrush(StudioTheme.Accent))
                    g.FillEllipse(brush, x - 3, 3, 6, 6);
            }
        }

        void DrawMarquee(Graphics g)
        {
            if (_dragMode != DragMode.Marquee || _marquee.Width <= 0 || _marquee.Height <= 0) return;
            using (var brush = new SolidBrush(Color.FromArgb(45, StudioTheme.Accent)))
                g.FillRectangle(brush, _marquee);
            using (var pen = new Pen(StudioTheme.Accent))
                g.DrawRectangle(pen, _marquee.X, _marquee.Y, _marquee.Width, _marquee.Height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }
            Rectangle content = ContentRect;
            _mouseDown = e.Location;
            if (e.X < KeyWidth && e.Y >= RulerHeight && e.Y < content.Bottom)
            {
                if (PitchAuditionRequested != null)
                    PitchAuditionRequested(this, new MidiEventArgs(YToMidi(e.Y)));
                return;
            }
            if (e.Y < RulerHeight && e.X >= KeyWidth)
            {
                _dragMode = DragMode.Playhead;
                _playheadBeat = Math.Max(0, XToBeat(e.X));
                if (PlayheadMoved != null) PlayheadMoved(this, EventArgs.Empty);
                Capture = true;
                Invalidate();
                return;
            }
            if (!content.Contains(e.Location)) return;

            HitInfo hit = HitTest(e.Location);
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            if (hit.Note != null)
            {
                _voice = hit.Voice;
                ScrollToVoice(_voice);
                if (ctrl)
                {
                    if (_selected.Contains(hit.Note)) _selected.Remove(hit.Note);
                    else _selected.Add(hit.Note);
                }
                else
                {
                    if (!_selected.Contains(hit.Note))
                    {
                        _selected.Clear();
                        _selected.Add(hit.Note);
                    }
                }
                _activeNote = hit.Note;
                NotifySelection();
                if (_selected.Count > 0)
                {
                    _dragBefore = _doc.Serialize();
                    _dragStartBeat = XToBeat(e.X);
                    _dragStartMidi = YToMidi(e.Y);
                    CaptureOrigins();
                    _dragMode = hit.Resize ? DragMode.Resize : DragMode.Move;
                    Capture = true;
                }
            }
            else
            {
                if (!ctrl) _selected.Clear();
                NotifySelection();
                _dragMode = DragMode.Marquee;
                _marquee = new Rectangle(e.X, e.Y, 0, 0);
                Capture = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Rectangle content = ContentRect;
            if (!Capture)
            {
                int oldMidi = _hoverMidi;
                EditorNote oldNote = _hoverNote;
                _hoverMidi = content.Contains(e.Location) ? YToMidi(e.Y) : -1;
                _hoverPoint = e.Location;
                HitInfo hover = content.Contains(e.Location) ? HitTest(e.Location) : new HitInfo();
                _hoverNote = hover.Note;
                _hoverVoice = hover.Voice;
                if (oldMidi != _hoverMidi || oldNote != _hoverNote || _hoverMidi >= 0) Invalidate();
            }
            if (_dragMode == DragMode.Playhead && Capture)
            {
                _playheadBeat = Math.Max(0, XToBeat(e.X));
                if (PlayheadMoved != null) PlayheadMoved(this, EventArgs.Empty);
                Invalidate();
            }
            else if (_dragMode == DragMode.Move || _dragMode == DragMode.Resize)
            {
                double currentBeat = SnapBeat(XToBeat(e.X));
                double delta = currentBeat - SnapBeat(_dragStartBeat);
                if (_dragMode == DragMode.Move)
                {
                    int deltaMidi = YToMidi(e.Y) - _dragStartMidi;
                    double minBeat = double.MaxValue;
                    int lo = int.MaxValue, hi = int.MinValue;
                    foreach (var n in _selected)
                    {
                        minBeat = Math.Min(minBeat, n.StartBeat);
                        lo = Math.Min(lo, n.Midi);
                        hi = Math.Max(hi, n.Midi);
                    }
                    if (minBeat + delta < 0) delta = -minBeat;
                    if (lo + deltaMidi < 0) deltaMidi = -lo;
                    if (hi + deltaMidi > 127) deltaMidi = 127 - hi;
                    foreach (var o in _origins)
                    {
                        o.Note.StartBeat = Math.Max(0, SnapBeat(o.StartBeat + delta));
                        o.Note.Midi = Math.Max(0, Math.Min(127, o.Midi + deltaMidi));
                    }
                }
                else if (_activeNote != null)
                {
                    NoteOrigin origin = null;
                    foreach (var o in _origins) if (o.Note == _activeNote) origin = o;
                    if (origin != null)
                        _activeNote.Beats = Math.Max(MinBeats, SnapBeat(origin.Beats + delta));
                }
                Invalidate();
            }
            else if (_dragMode == DragMode.Marquee && Capture)
            {
                int x = Math.Min(_mouseDown.X, e.X);
                int y = Math.Min(_mouseDown.Y, e.Y);
                int w = Math.Abs(e.X - _mouseDown.X);
                int h = Math.Abs(e.Y - _mouseDown.Y);
                _marquee = new Rectangle(x, y, w, h);
                SelectInMarquee((ModifierKeys & Keys.Control) != 0);
                Invalidate();
            }
            else
            {
                HitInfo hit = content.Contains(e.Location) ? HitTest(e.Location) :
                    new HitInfo();
                Cursor = hit.Note != null ? (hit.Resize ? Cursors.SizeWE : Cursors.SizeAll) :
                    Cursors.Default;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverMidi = -1;
            _hoverNote = null;
            _hoverVoice = null;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragMode == DragMode.Move || _dragMode == DragMode.Resize)
            {
                _doc.Normalize();
                FinishEdit(_dragBefore);
            }
            else if (_dragMode == DragMode.Marquee)
            {
                SelectInMarquee((ModifierKeys & Keys.Control) != 0);
                NotifySelection();
            }
            _dragMode = DragMode.None;
            _origins.Clear();
            _activeNote = null;
            _marquee = Rectangle.Empty;
            Capture = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (_doc != null && ContentRect.Contains(e.Location))
            {
                if (_voice == null && _doc.Voices.Count > 0) _voice = _doc.Voices[0];
                if (_voice != null)
                {
                    string before = _doc.Serialize();
                    var n = new EditorNote();
                    n.Midi = YToMidi(e.Y);
                    n.StartBeat = SnapBeat(XToBeat(e.X));
                    n.Beats = Math.Max(0.25, Math.Ceiling(_voice.MinimumBeats(_doc.Bpm) * 4.0) / 4.0);
                    _voice.Notes.Add(n);
                    _doc.Normalize();
                    _selected.Clear();
                    _selected.Add(n);
                    FinishEdit(before);
                    NotifySelection();
                    if (StatusRequested != null)
                        StatusRequested(this, new StatusEventArgs("已创建音符 " +
                            ScoreParser.MidiToName(n.Midi) + " · Ctrl+D 复制 · Delete 删除"));
                }
            }
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right ||
                keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Delete)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                DeleteSelection();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                SelectAll();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.D && e.Control)
            {
                DuplicateSelection();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ClearSelection();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                double beat = (e.KeyCode == Keys.Left ? -0.25 : (e.KeyCode == Keys.Right ? 0.25 : 0));
                int semi = (e.KeyCode == Keys.Up ? 1 : (e.KeyCode == Keys.Down ? -1 : 0));
                if (e.Shift && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && _selected.Count > 0)
                {
                    string before = _doc.Serialize();
                    foreach (var n in _selected) n.Beats = Math.Max(MinBeats, n.Beats + beat);
                    FinishEdit(before);
                }
                else NudgeSelection(beat, semi);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        void CaptureOrigins()
        {
            _origins.Clear();
            foreach (var n in _selected) _origins.Add(new NoteOrigin(n));
        }

        void SelectInMarquee(bool add)
        {
            if (_doc == null) return;
            if (!add) _selected.Clear();
            foreach (var v in _doc.Voices)
                foreach (var n in v.Notes)
                {
                    Rectangle r = NoteRect(n);
                    if (_marquee.IntersectsWith(r) && !_selected.Contains(n)) _selected.Add(n);
                }
        }

        void FinishEdit(string before)
        {
            if (_doc == null) return;
            string after = _doc.Serialize();
            if (before != null && before != after && DocumentChanged != null)
                DocumentChanged(this, new DocumentEditEventArgs(before));
            UpdateScrollbars();
            Invalidate();
        }

        void NotifySelection()
        {
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            Invalidate();
        }

        HitInfo HitTest(Point p)
        {
            var hit = new HitInfo();
            if (_doc == null) return hit;
            for (int vi = _doc.Voices.Count - 1; vi >= 0; vi--)
            {
                EditorVoice v = _doc.Voices[vi];
                for (int i = v.Notes.Count - 1; i >= 0; i--)
                {
                    EditorNote n = v.Notes[i];
                    Rectangle r = NoteRect(n);
                    if (r.Contains(p))
                    {
                        hit.Note = n;
                        hit.Voice = v;
                        hit.Resize = p.X >= r.Right - 7;
                        return hit;
                    }
                }
            }
            return hit;
        }

        Rectangle NoteRect(EditorNote n)
        {
            int x = XForBeat(n.StartBeat);
            int y = MidiToY(n.Midi);
            int w = Math.Max(5, (int)Math.Round(n.Beats * BeatWidth));
            return new Rectangle(x, y + 1, w, Math.Max(5, VisibleRowHeight - 2));
        }

        int XForBeat(double beat)
        {
            return (int)Math.Round(KeyWidth + beat * BeatWidth - _hbar.Value);
        }

        double XToBeat(int x)
        {
            return Math.Max(0, (x - KeyWidth + _hbar.Value) / BeatWidth);
        }

        int MidiToY(int midi)
        {
            return ContentRect.Top + (_topMidi - midi) * VisibleRowHeight;
        }

        int YToMidi(int y)
        {
            int midi = _topMidi - (int)Math.Floor((y - ContentRect.Top) / (double)VisibleRowHeight);
            return Math.Max(0, Math.Min(127, midi));
        }

        static double SnapBeat(double beat)
        {
            return Math.Round(beat * 4.0) / 4.0;
        }

        static bool IsBlackKey(int midi)
        {
            int n = midi % 12;
            return n == 1 || n == 3 || n == 6 || n == 8 || n == 10;
        }

        class HitInfo
        {
            public EditorNote Note;
            public EditorVoice Voice;
            public bool Resize;
        }

        class NoteOrigin
        {
            public readonly EditorNote Note;
            public readonly double StartBeat;
            public readonly double Beats;
            public readonly int Midi;
            public NoteOrigin(EditorNote n)
            {
                Note = n;
                StartBeat = n.StartBeat;
                Beats = n.Beats;
                Midi = n.Midi;
            }
        }
    }

    public class DocumentEditEventArgs : EventArgs
    {
        public readonly string Before;
        public DocumentEditEventArgs(string before) { Before = before; }
    }

    public class MotorDropEventArgs : EventArgs
    {
        public readonly string MotorKey;
        public readonly double Beat;
        public readonly int Midi;
        public MotorDropEventArgs(string key, double beat, int midi)
        {
            MotorKey = key;
            Beat = beat;
            Midi = midi;
        }
    }

    public class StatusEventArgs : EventArgs
    {
        public readonly string Text;
        public StatusEventArgs(string text) { Text = text; }
    }

    public class MidiEventArgs : EventArgs
    {
        public readonly int Midi;
        public MidiEventArgs(int midi) { Midi = midi; }
    }

    public class DensityControl : Control
    {
        readonly TimelineControl _timeline;
        readonly StudioButton _fit;
        EditorDocument _doc;

        public DensityControl(TimelineControl timeline)
        {
            _timeline = timeline;
            Height = 98;
            BackColor = StudioTheme.Panel;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timeline.PlayheadMoved += delegate { Invalidate(); };
            timeline.SelectionChanged += delegate { Invalidate(); };
            _fit = new StudioButton();
            _fit.Text = "适应内容";
            _fit.Compact = true;
            _fit.Bounds = new Rectangle(Width - 82, 5, 72, 24);
            _fit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _fit.Click += delegate { _timeline.Fit(); };
            var tips = new ToolTip();
            tips.SetToolTip(_fit, "Ctrl+0：适应项目内容");
            Controls.Add(_fit);
        }

        public void SetDocument(EditorDocument doc)
        {
            _doc = doc;
            Invalidate();
        }

        public EditorDocument Document { set { _doc = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(StudioTheme.Panel);
            using (var pen = new Pen(StudioTheme.Border))
                g.DrawLine(pen, 0, 0, Width, 0);
            TextRenderer.DrawText(g, "同时发声数", StudioTheme.UiSmall,
                new Rectangle(6, 7, Math.Max(90, _timeline.TimelineLeft + 34), 20), StudioTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int left = _timeline.TimelineLeft + 4;
            int top = 7;
            int chartH = Math.Max(30, Height - 22);
            int count = Math.Max(4, Math.Min(24, (Width - left - 12) / 28));
            if (_doc == null || _doc.Voices.Count == 0 || count <= 0) return;
            double startBeat = _timeline.HorizontalOffset / _timeline.BeatWidth;
            double span = (Width - left) / _timeline.BeatWidth;
            int max = 1;
            var vals = new int[count];
            for (int i = 0; i < count; i++)
            {
                double beat = startBeat + span * (i + 0.5) / count;
                foreach (var v in _doc.Voices)
                    foreach (var n in v.Notes)
                        if (beat >= n.StartBeat && beat < n.EndBeat) vals[i]++;
                max = Math.Max(max, vals[i]);
            }
            max = Math.Max(max, 8);
            int barGap = 4;
            int barW = Math.Max(4, (Width - left - 12) / count - barGap);
            int thresholdY = top + chartH - (int)(chartH * 8.0 / Math.Max(10, max));
            using (var pen = new Pen(Color.FromArgb(150, StudioTheme.Danger)))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, left, thresholdY, Width - 8, thresholdY);
            }
            TextRenderer.DrawText(g, "8 台", StudioTheme.UiSmall, new Point(left + 2, top),
                StudioTheme.Danger);
            for (int i = 0; i < count; i++)
            {
                int h = Math.Max(3, (int)(chartH * vals[i] / (double)max));
                int x = left + i * ((Width - left - 12) / count);
                Rectangle r = new Rectangle(x, top + chartH - h, barW, h);
                using (var brush = new SolidBrush(vals[i] > 8 ? StudioTheme.Danger : Color.FromArgb(92, 98, 112)))
                    g.FillRectangle(brush, r);
            }
        }
    }
}
