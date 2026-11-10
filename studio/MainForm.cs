// MainForm.cs —— 四区编辑器主窗口与命令协调

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FanStudio
{
    public class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly List<Motor> _catalog;
        readonly SnapshotHistory _history = new SnapshotHistory();
        readonly SoundPlayer _player = new SoundPlayer();
        readonly System.Windows.Forms.Timer _playTimer;
        readonly Stopwatch _playWatch = new Stopwatch();
        EditorDocument _doc;
        string _scorePath;
        string _savedSnapshot = "";
        string _lastWav;
        double _previewDuration;
        bool _busy;
        bool _playing;
        bool _fillingBpm;
        bool _suppressPrompt;

        TransportBar _transport;
        MotorLibraryControl _library;
        TimelineControl _timeline;
        DensityControl _density;
        InspectorPanel _inspector;
        Label _statusText;
        Label _statusRisk;
        BackgroundWorker _bw;
        BackgroundWorker _voiceBw;

        public MainForm(List<Motor> catalog, string scorePath)
        {
            _catalog = catalog;
            _scorePath = scorePath;
            Text = I18n.L("fan_studio — 电机乐器编辑器", "fan_studio — Motor Instrument Editor");
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = StudioTheme.Canvas;
            ForeColor = StudioTheme.Text;
            Font = StudioTheme.Ui;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1400, 860);
            MinimumSize = new Size(1120, 690);
            KeyPreview = true;

            BuildUi();
            WireEvents();

            if (scorePath != null && File.Exists(scorePath)) LoadScore(scorePath, false);
            else
            {
                _doc = EditorDocument.CreateDemo(_catalog);
                _savedSnapshot = "";
            }
            _timeline.SetDocument(_doc);
            _density.SetDocument(_doc);
            _timeline.Fit();
            _timeline.RefreshDiagnostics();
            if (_doc.Voices.Count > 0 && _doc.Voices[0].Notes.Count > 0)
                _timeline.SelectNote(_doc.Voices[0], _doc.Voices[0].Notes[0]);
            RefreshAll();
            UpdateTitle();

            _bw = new BackgroundWorker();
            _bw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = DoRender((RenderRequest)e.Argument); };
            _bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e) { FinishRender(e); };

            _voiceBw = new BackgroundWorker();
            _voiceBw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = DoVoicePreview((EditorVoice)e.Argument); };
            _voiceBw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e) { FinishVoicePreview(e); };

            _playTimer = new System.Windows.Forms.Timer();
            _playTimer.Interval = 40;
            _playTimer.Tick += delegate { PlayTick(); };

            FormClosing += OnClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        void BuildUi()
        {
            var status = new Panel();
            status.Dock = DockStyle.Bottom;
            status.Height = 29;
            status.BackColor = StudioTheme.Panel;
            status.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(StudioTheme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
            };
            Controls.Add(status);

            _statusText = new Label();
            _statusText.Text = I18n.L("就绪", "Ready");
            _statusText.ForeColor = StudioTheme.Muted;
            _statusText.Font = StudioTheme.UiSmall;
            _statusText.AutoSize = false;
            _statusText.TextAlign = ContentAlignment.MiddleLeft;
            _statusText.Bounds = new Rectangle(14, 0, 500, status.Height);
            _statusText.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            status.Controls.Add(_statusText);

            _statusRisk = new Label();
            _statusRisk.ForeColor = StudioTheme.Danger;
            _statusRisk.Font = StudioTheme.UiSmall;
            _statusRisk.AutoSize = false;
            _statusRisk.TextAlign = ContentAlignment.MiddleRight;
            _statusRisk.Bounds = new Rectangle(status.Width - 640, 0, 628, status.Height);
            _statusRisk.Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            status.Controls.Add(_statusRisk);
            status.Resize += delegate
            {
                _statusText.Width = Math.Max(200, status.Width / 2 - 30);
                _statusRisk.Left = status.Width / 2;
                _statusRisk.Width = Math.Max(200, status.Width / 2 - 14);
            };

            _transport = new TransportBar();
            _transport.Dock = DockStyle.Top;
            Controls.Add(_transport);

            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = StudioTheme.Canvas;
            Controls.Add(body);
            body.BringToFront();

            var outer = new SplitContainer();
            outer.Dock = DockStyle.Fill;
            outer.Orientation = Orientation.Vertical;
            outer.SplitterWidth = 1;
            outer.FixedPanel = FixedPanel.Panel1;
            outer.BackColor = StudioTheme.Border;
            body.Controls.Add(outer);

            _library = new MotorLibraryControl(_catalog);
            _library.Dock = DockStyle.Fill;
            outer.Panel1.Controls.Add(_library);

            var inner = new SplitContainer();
            inner.Dock = DockStyle.Fill;
            inner.Orientation = Orientation.Vertical;
            inner.SplitterWidth = 1;
            inner.FixedPanel = FixedPanel.Panel2;
            inner.BackColor = StudioTheme.Border;
            outer.Panel2.Controls.Add(inner);

            var center = new Panel();
            center.Dock = DockStyle.Fill;
            center.BackColor = StudioTheme.Canvas;
            inner.Panel1.Controls.Add(center);

            _timeline = new TimelineControl();
            _timeline.Dock = DockStyle.Fill;
            center.Controls.Add(_timeline);

            _density = new DensityControl(_timeline);
            _density.Dock = DockStyle.Bottom;
            _density.Height = 98;
            center.Controls.Add(_density);
            _timeline.BringToFront();

            _inspector = new InspectorPanel();
            _inspector.Dock = DockStyle.Fill;
            inner.Panel2.Controls.Add(_inspector);

            outer.Resize += delegate { LayoutSplits(outer, inner); };
            Shown += delegate { LayoutSplits(outer, inner); };
        }

        void LayoutSplits(SplitContainer outer, SplitContainer inner)
        {
            try
            {
                int left = 238;
                if (outer.Width >= left + 520) outer.SplitterDistance = left;
                int right = Math.Max(276, Math.Min(306, inner.Width / 4));
                if (inner.Width >= 520 + right + 2)
                    inner.SplitterDistance = inner.Width - right - inner.SplitterWidth;
            }
            catch { }
        }

        void WireEvents()
        {
            _transport.PlayRequested += delegate
            {
                if (_playing) StopPlayback();
                else RenderAndPlay();
            };
            _transport.StopRequested += delegate { StopPlayback(); };
            _transport.ImportMidiRequested += delegate { ImportMidi(); };
            _transport.OpenRequested += delegate { OpenScore(); };
            _transport.SaveRequested += delegate { SaveScore(false); };
            _transport.RenderRequested += delegate { RenderAndPlay(); };
            _transport.ShowHelpRequested += delegate { ShowLegalDocuments(); };
            _transport.LanguageToggleRequested += delegate { ToggleLanguage(); };
            _transport.BpmChanged += delegate(object s, BpmEventArgs e)
            {
                if (_fillingBpm || _doc == null || Math.Abs(_doc.Bpm - e.Bpm) < 1e-9) return;
                string before = _doc.Serialize();
                _doc.Bpm = e.Bpm;
                RecordEdit(before);
                RefreshAll();
            };

            _library.MotorSelected += delegate(object s, MotorLibraryEventArgs e)
            {
                EditorVoice match = null;
                if (_doc != null)
                    foreach (var v in _doc.Voices)
                        if (v.Motor != null && v.Motor.Key == e.Motor.Key) { match = v; break; }
                if (match != null)
                {
                    _timeline.SetSelectedVoice(match);
                    _inspector.ShowSelection(_doc, match, new List<EditorNote>());
                }
                else _inspector.ShowLibraryMotor(e.Motor);
                SetStatus(I18n.MotorModel(e.Motor) +
                    I18n.L(" · 拖入卷帘或双击添加声部", " · drag into the roll or double-click to add a voice"));
            };
            _library.MotorActivated += delegate(object s, MotorLibraryEventArgs e)
            {
                AddMotorAtPlayhead(e.Motor);
            };

            _timeline.SelectionChanged += delegate { ShowInspectorSelection(); };
            _timeline.DocumentChanged += delegate(object s, DocumentEditEventArgs e)
            {
                RecordEdit(e.Before);
                RefreshAll();
                ShowInspectorSelection();
            };
            _timeline.PlayheadMoved += delegate
            {
                _transport.SetTime(_timeline.PlayheadBeat * _doc.BeatSeconds);
            };
            _timeline.StatusRequested += delegate(object s, StatusEventArgs e) { SetStatus(e.Text); };
            _timeline.PitchAuditionRequested += delegate(object s, MidiEventArgs e) { AuditionPitch(e.Midi); };
            _timeline.MotorDropped += delegate(object s, MotorDropEventArgs e)
            {
                Motor m = FindMotor(e.MotorKey);
                if (m == null) return;
                AddVoice(m, e.Beat, e.Midi);
            };

            _inspector.DocumentEdited += delegate(object s, DocumentEditEventArgs e)
            {
                RecordEdit(e.Before);
                RefreshAll();
            };
            _inspector.VoiceSelected += delegate(object s, VoiceEventArgs e)
            {
                _timeline.SetSelectedVoice(e.Voice);
                _timeline.ScrollToVoice(e.Voice);
                ShowInspectorSelection();
            };
            _inspector.AuditionRequested += delegate(object s, VoiceEventArgs e) { AuditionVoice(e.Voice); };
            _inspector.DeleteVoiceRequested += delegate(object s, VoiceEventArgs e) { DeleteVoice(e.Voice); };
            _inspector.AddMotorRequested += delegate(object s, MotorLibraryEventArgs e) { AddMotorAtPlayhead(e.Motor); };
        }

        Motor FindMotor(string key)
        {
            foreach (var m in _catalog) if (m.Key == key) return m;
            return null;
        }

        void ShowLegalDocuments()
        {
            while (true)
            {
                using (var dialog = new LegalDialog(false))
                {
                    if (dialog.ShowDialog(this) != DialogResult.Retry) break;
                }
            }
        }

        void ToggleLanguage()
        {
            if (!ConfirmDiscard()) return;
            AppSettings.SetLanguage(I18n.IsEnglish ? UiLanguage.Chinese : UiLanguage.English);
            Application.Restart();
        }

        public void LoadScore(string path, bool recordHistory)
        {
            try
            {
                string before = _doc == null ? null : _doc.Serialize();
                string text = File.ReadAllText(path, Encoding.UTF8);
                EditorDocument doc = EditorDocument.Load(text, _catalog);
                if (doc.Voices.Count == 0)
                    throw new InvalidDataException(I18n.L("文件里没有可识别的 [voice ...] 声部",
                        "No recognizable [voice ...] sections were found"));
                _doc = doc;
                _doc.Title = Path.GetFileNameWithoutExtension(path);
                _scorePath = path;
                _savedSnapshot = _doc.Serialize();
                if (recordHistory && before != null) _history.Record(before);
                else _history.Clear();
                _timeline.SetDocument(_doc);
                _density.SetDocument(_doc);
                _timeline.Fit();
                RefreshAll();
                SetStatus(I18n.L("已打开 ", "Opened ") + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, I18n.L("读取文件失败：\n\n", "Could not open the file:\n\n") + ex.Message, "fan_studio",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void OpenScore()
        {
            if (!ConfirmDiscard()) return;
            var d = new OpenFileDialog();
            d.Filter = I18n.L("风扇乐谱 (*.fscore)|*.fscore|所有文件 (*.*)|*.*",
                "fan scores (*.fscore)|*.fscore|All files (*.*)|*.*");
            d.Title = I18n.L("打开 fan_music 乐谱", "Open a fan_studio score");
            if (d.ShowDialog(this) == DialogResult.OK) LoadScore(d.FileName, false);
        }

        void SaveScore(bool saveAs)
        {
            string path = _scorePath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                var d = new SaveFileDialog();
                d.Filter = I18n.L("风扇乐谱 (*.fscore)|*.fscore", "fan scores (*.fscore)|*.fscore");
                d.FileName = string.IsNullOrEmpty(path) ? "my_fan_song.fscore" : Path.GetFileName(path);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
                if (!path.ToLowerInvariant().EndsWith(".fscore")) path += ".fscore";
            }
            try
            {
                _doc.Normalize();
                string text = _doc.Serialize();
                File.WriteAllText(path, text, new UTF8Encoding(true));
                _scorePath = path;
                _doc.Title = Path.GetFileNameWithoutExtension(path);
                _savedSnapshot = text;
                UpdateTitle();
                SetStatus(I18n.L("已保存 ", "Saved ") + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, I18n.L("保存失败：\n\n", "Could not save the file:\n\n") + ex.Message, "fan_studio",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void NewProject()
        {
            if (!ConfirmDiscard()) return;
            string before = _doc == null ? null : _doc.Serialize();
            _doc = EditorDocument.CreateDemo(_catalog);
            _scorePath = null;
            _savedSnapshot = "";
            if (before != null) _history.Record(before); else _history.Clear();
            _timeline.SetDocument(_doc);
            _density.SetDocument(_doc);
            RefreshAll();
            SetStatus(I18n.L("已新建项目", "Created a new project"));
        }

        void AddMotorAtPlayhead(Motor m)
        {
            if (_doc == null || m == null) return;
            int pitch = 60;
            EditorVoice selected = _timeline.SelectedVoice;
            if (selected != null)
                pitch = Math.Max(0, Math.Min(127, (int)Math.Round((selected.LoMidi + selected.HiMidi) / 2.0)));
            AddVoice(m, Math.Round(_timeline.PlayheadBeat * 4.0) / 4.0, pitch);
        }

        void AddVoice(Motor baseMotor, double beat, int midi)
        {
            if (_doc == null || baseMotor == null) return;
            string before = _doc.Serialize();
            EditorVoice v = _doc.NewVoice(baseMotor, beat, midi);
            _doc.Normalize();
            RecordEdit(before);
            _timeline.SetDocument(_doc);
            _density.SetDocument(_doc);
            _timeline.SetSelectedVoice(v);
            if (v.Notes.Count > 0) _timeline.SelectNote(v, v.Notes[0]);
            _timeline.ScrollToVoice(v);
            RefreshAll();
            ShowInspectorSelection();
            SetStatus(I18n.L("已添加 ", "Added ") + v.Label + " · " + v.ModelText);
        }

        void DeleteVoice(EditorVoice voice)
        {
            if (_doc == null || voice == null) return;
            string before = _doc.Serialize();
            _doc.RemoveVoice(voice);
            RecordEdit(before);
            _timeline.SetDocument(_doc);
            _density.SetDocument(_doc);
            RefreshAll();
            ShowInspectorSelection();
            SetStatus(I18n.L("已删除声部；Ctrl+Z 可撤销", "Voice deleted · press Ctrl+Z to undo"));
        }

        void ShowInspectorSelection()
        {
            if (_doc == null) return;
            EditorVoice voice = _timeline.SelectedVoice;
            _inspector.ShowSelection(_doc, voice, _timeline.SelectedNotes);
        }

        void RecordEdit(string before)
        {
            if (_doc == null || before == null) return;
            string after = _doc.Serialize();
            if (before == after) return;
            _history.Record(before);
            UpdateTitle();
        }

        void Undo()
        {
            if (_doc == null) return;
            string restored = _history.Undo(_doc.Serialize());
            if (restored == null) { SetStatus(I18n.L("没有可撤销的操作", "Nothing to undo")); return; }
            ApplyRestored(restored, I18n.L("已撤销", "Undone"));
        }

        void Redo()
        {
            if (_doc == null) return;
            string restored = _history.Redo(_doc.Serialize());
            if (restored == null) { SetStatus(I18n.L("没有可重做的操作", "Nothing to redo")); return; }
            ApplyRestored(restored, I18n.L("已重做", "Redone"));
        }

        void ApplyRestored(string text, string status)
        {
            _doc = EditorDocument.Load(text, _catalog);
            _timeline.SetDocument(_doc);
            _density.SetDocument(_doc);
            RefreshAll();
            ShowInspectorSelection();
            SetStatus(status);
        }

        void RefreshAll()
        {
            if (_doc == null) return;
            _doc.BuildScore();
            _timeline.RefreshDiagnostics();
            _density.Invalidate();
            _fillingBpm = true;
            _transport.Bpm = (decimal)_doc.Bpm;
            _fillingBpm = false;
            _transport.SetProject(string.IsNullOrEmpty(_scorePath)
                ? I18n.L("未命名项目", "Untitled project") : Path.GetFileName(_scorePath));
            int warnings = _doc.CountWarnings();
            int maxConcurrent = _doc.MaxConcurrent();
            string risk = I18n.IsEnglish
                ? warnings.ToString(CultureInfo.InvariantCulture) + " timing/range warnings · up to " +
                    maxConcurrent.ToString(CultureInfo.InvariantCulture) + " voices together"
                : warnings.ToString(CultureInfo.InvariantCulture) + " 个时值/音域风险 · 最多 " +
                    maxConcurrent.ToString(CultureInfo.InvariantCulture) + " 台同时发声";
            if (maxConcurrent > 8) risk += I18n.L(" · 低频会糊", " · low end may become muddy");
            _statusRisk.Text = risk;
            _statusRisk.ForeColor = (warnings > 0 || maxConcurrent > 8) ? StudioTheme.Danger : StudioTheme.Muted;
            _timeline.Invalidate();
            UpdateTitle();
        }

        void UpdateTitle()
        {
            bool dirty = _doc != null && _scorePath == null;
            if (_doc != null && _scorePath != null) dirty = _doc.Serialize() != _savedSnapshot;
            string name = string.IsNullOrEmpty(_scorePath)
                ? I18n.L("未命名项目", "Untitled project") : Path.GetFileName(_scorePath);
            Text = (dirty ? "● " : "") + name + " — fan_studio";
            _transport.SetProject((dirty ? "● " : "") + name);
        }

        void ImportMidi()
        {
            if (_busy) return;
            var pick = new OpenFileDialog();
            pick.Filter = I18n.L("MIDI (*.mid;*.midi)|*.mid;*.midi|所有文件 (*.*)|*.*",
                "MIDI (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*");
            pick.Title = I18n.L("选择要改编成风扇乐器的 MIDI", "Choose MIDI to arrange for motors");
            if (pick.ShowDialog(this) != DialogResult.OK) return;

            var setup = new MidiImportDialog(_doc == null || _doc.Voices.Count == 0 ? 4 : _doc.Voices.Count,
                _doc == null ? 120 : _doc.Bpm);
            if (setup.ShowDialog(this) != DialogResult.OK) return;

            SetBusy(true);
            SetStatus(I18n.L("正在让 Python 改编器分析 MIDI…", "Analyzing MIDI with the local Python arranger…"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string root = FindProjectRoot();
                    if (root == null) throw new DirectoryNotFoundException(
                        I18n.L("找不到包含 fan_music 的项目根目录", "Could not find a project root containing fan_music"));
                    string output = Path.Combine(Path.GetTempPath(), "fan_studio_import.fscore");
                    string args = "-m fan_music import-midi " + Quote(pick.FileName) +
                        " --voices " + setup.VoiceCount.ToString(CultureInfo.InvariantCulture) +
                        " --duration " + setup.Duration.ToString("0.##", CultureInfo.InvariantCulture) +
                        " --tempo " + setup.Bpm.ToString("0.##", CultureInfo.InvariantCulture) +
                        " --out " + Quote(output);
                    var psi = new ProcessStartInfo();
                    psi.FileName = "python";
                    psi.Arguments = args;
                    psi.WorkingDirectory = root;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    string stdout = "", stderr = "";
                    using (var p = Process.Start(psi))
                    {
                        stdout = p.StandardOutput.ReadToEnd();
                        stderr = p.StandardError.ReadToEnd();
                        p.WaitForExit();
                        if (p.ExitCode != 0)
                            throw new InvalidOperationException(
                                I18n.L("MIDI 改编失败：\n", "MIDI arrangement failed:\n") +
                                (stderr.Length > 0 ? stderr : stdout));
                    }
                    if (!File.Exists(output)) throw new FileNotFoundException(
                        I18n.L("改编器没有生成谱子", "The arranger did not produce a score"), output);
                    string text = File.ReadAllText(output, Encoding.UTF8);
                    RunOnUi(delegate
                    {
                        string before = _doc.Serialize();
                        _doc = EditorDocument.Load(text, _catalog);
                        _doc.Title = Path.GetFileNameWithoutExtension(pick.FileName);
                        _scorePath = null;
                        _savedSnapshot = "";
                        _history.Record(before);
                        _timeline.SetDocument(_doc);
                        _density.SetDocument(_doc);
                        _timeline.Fit();
                        RefreshAll();
                        ShowInspectorSelection();
                        SetBusy(false);
                        SetStatus(I18n.L("MIDI 已改编为 ", "MIDI arranged into ") + _doc.Voices.Count +
                            I18n.L(" 个声部；保存后写入 .fscore", " voices · save to write .fscore"));
                    });
                }
                catch (Exception ex)
                {
                    RunOnUi(delegate
                    {
                        SetBusy(false);
                        SetStatus(I18n.L("MIDI 导入失败", "MIDI import failed"));
                        MessageBox.Show(this, ex.Message, I18n.L("导入 MIDI", "Import MIDI"),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            });
        }

        string FindProjectRoot()
        {
            DirectoryInfo d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "fan_music"))) return d.FullName;
            return null;
        }

        static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        void RunOnUi(MethodInvoker action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        void RenderAndPlay()
        {
            if (_busy || _doc == null) return;
            if (_playing) StopPlayback();
            SetBusy(true);
            SetStatus(I18n.L("正在渲染声学模型并做 FFT 音准自检…",
                "Rendering acoustics and running FFT pitch checks…"));
            _bw.RunWorkerAsync(new RenderRequest(_doc.Serialize(), _doc.Bpm, null));
        }

        RenderResult DoRender(RenderRequest req)
        {
            EditorDocument doc = EditorDocument.Load(req.ScoreText, _catalog);
            Score sc = doc.BuildScore();
            if (sc.Voices.Count == 0) throw new InvalidOperationException(
                I18n.L("谱子里没有声部", "The score has no voices"));
            int fs = 48000;
            double[][] mix = Synth.Render(sc, fs, 0.85, 0.14);
            if (mix[0].Length == 0) throw new InvalidOperationException(
                I18n.L("渲染结果为空", "The render is empty"));
            double worst;
            int detected, total;
            string report = PitchCheck.Run(sc, mix, fs, out worst, out detected, out total);
            string wav = Path.Combine(Path.GetTempPath(), "fan_studio_preview.wav");
            WavWriter.Write(wav, mix, fs);
            var result = new RenderResult();
            result.WavPath = wav;
            result.Duration = mix[0].Length / (double)fs;
            result.Status = string.Format(CultureInfo.InvariantCulture,
                I18n.L("渲染完成 · {0:F1}s · {1} 声部 · 检出 {2}/{3} · 最差 {4:+0.0;-0.0} 音分",
                    "Rendered · {0:F1}s · {1} voices · detected {2}/{3} · worst {4:+0.0;-0.0} cents"),
                result.Duration, sc.Voices.Count, detected, total, worst);
            result.Report = report;
            return result;
        }

        void FinishRender(RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);
            if (e.Error != null)
            {
                SetStatus(I18n.L("渲染失败", "Render failed"));
                MessageBox.Show(this, e.Error.Message, I18n.L("渲染", "Render"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RenderResult r = (RenderResult)e.Result;
            _lastWav = r.WavPath;
            _previewDuration = r.Duration;
            SetStatus(r.Status);
            PlayFile(r.WavPath, r.Duration);
            if (!string.IsNullOrEmpty(r.Report)) Console.Write(r.Report);
        }

        void AuditionVoice(EditorVoice voice)
        {
            if (_busy || voice == null || _voiceBw.IsBusy) return;
            SetBusy(true);
            SetStatus(I18n.L("正在试听声部 ", "Auditioning voice ") + voice.Label + "…");
            _voiceBw.RunWorkerAsync(voice);
        }

        RenderResult DoVoicePreview(EditorVoice original)
        {
            string text = _doc.Serialize();
            EditorDocument doc = EditorDocument.Load(text, _catalog);
            EditorVoice voice = doc.FindVoice(original.Label);
            if (voice == null) throw new InvalidOperationException(
                I18n.L("声部已不存在", "The voice no longer exists"));
            doc.Voices.Clear();
            doc.Voices.Add(voice);
            Score sc = doc.BuildScore();
            int fs = 48000;
            double[][] mix = Synth.Render(sc, fs, 0.85, 0.14);
            string wav = Path.Combine(Path.GetTempPath(), "fan_studio_voice.wav");
            WavWriter.Write(wav, mix, fs);
            var r = new RenderResult();
            r.WavPath = wav;
            r.Duration = mix[0].Length / (double)fs;
            r.Status = I18n.L("试听声部 ", "Audition: ") + voice.Label + " · " +
                r.Duration.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            return r;
        }

        void FinishVoicePreview(RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);
            if (e.Error != null)
            {
                SetStatus(I18n.L("试听失败", "Audition failed"));
                MessageBox.Show(this, e.Error.Message, I18n.L("试听", "Audition"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RenderResult r = (RenderResult)e.Result;
            SetStatus(r.Status);
            PlayFile(r.WavPath, r.Duration);
        }

        void PlayFile(string path, double duration)
        {
            try
            {
                StopPlayback();
                _player.SoundLocation = path;
                _player.Load();
                _player.Play();
                _playing = true;
                _previewDuration = duration;
                _playWatch.Reset();
                _playWatch.Start();
                _playTimer.Start();
                _transport.SetPlaying(true);
                _timeline.SetPlaying(true);
                _timeline.SetPlayhead(0, true);
            }
            catch (Exception ex)
            {
                SetStatus(I18n.L("播放失败：", "Playback failed: ") + ex.Message);
            }
        }

        void PlayTick()
        {
            if (!_playing) return;
            double seconds = _playWatch.Elapsed.TotalSeconds;
            double beat = seconds / _doc.BeatSeconds;
            _timeline.SetPlayhead(beat, false);
            _transport.SetTime(seconds);
            if (seconds >= _previewDuration + 0.15)
            {
                StopPlayback();
                SetStatus(I18n.L("播放结束", "Playback finished"));
            }
        }

        void StopPlayback()
        {
            try { _player.Stop(); } catch { }
            if (_playTimer != null) _playTimer.Stop();
            _playWatch.Stop();
            _playing = false;
            if (_transport != null) _transport.SetPlaying(false);
            if (_timeline != null) _timeline.SetPlaying(false);
        }

        void AuditionPitch(int midi)
        {
            if (_doc == null) return;
            EditorVoice voice = _timeline.SelectedVoice;
            if (voice == null && _doc.Voices.Count > 0) voice = _doc.Voices[0];
            Motor motor = voice == null ? null : voice.Motor;
            if (motor == null)
            {
                foreach (var m in _catalog)
                    if (m.Kind == "revolve") { motor = m; break; }
            }
            if (motor == null) return;
            if (motor.LoHz <= 0 || motor.HiHz <= 0) return;
            double lo = 69.0 + 12.0 * Math.Log(motor.LoHz / 440.0, 2.0);
            double hi = 69.0 + 12.0 * Math.Log(motor.HiHz / 440.0, 2.0);
            if (midi + 0.45 < lo || midi - 0.45 > hi)
            {
                SetStatus(ScoreParser.MidiToName(midi) + I18n.L(" 超出 ", " is outside ") +
                    (voice == null ? I18n.MotorModel(motor) : voice.Label) +
                    I18n.L(" 的音域", "'s range"));
                return;
            }
            try
            {
                var sc = new Score();
                sc.Bpm = 100;
                var sv = new Voice();
                sv.Motor = motor.Clone();
                sv.Label = voice == null ? motor.Key : voice.Label;
                sv.Raw.Add(new NoteEv { Midi = midi, Beats = 1.2 });
                sc.Voices.Add(sv);
                Planner.Build(sc);
                int fs = 48000;
                double[][] mix = Synth.Render(sc, fs, 0.7, 0.08);
                string wav = Path.Combine(Path.GetTempPath(), "fan_studio_pitch.wav");
                WavWriter.Write(wav, mix, fs);
                SetStatus(I18n.L("试听 ", "Audition ") + ScoreParser.MidiToName(midi) + " · " + sv.Label);
                PlayFile(wav, mix[0].Length / (double)fs);
            }
            catch (Exception ex)
            {
                SetStatus(I18n.L("试听失败：", "Audition failed: ") + ex.Message);
            }
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            _transport.SetBusy(busy);
            UseWaitCursor = busy;
        }

        void SetStatus(string text)
        {
            if (_statusText == null) return;
            _statusText.Text = text;
        }

        bool ConfirmDiscard()
        {
            if (_suppressPrompt) return true;
            if (_doc == null) return true;
            bool dirty = _scorePath == null || _doc.Serialize() != _savedSnapshot;
            if (!dirty) return true;
            DialogResult r = MessageBox.Show(this,
                I18n.L("当前项目还没有保存。要先保存吗？", "This project has not been saved. Save it first?"),
                "fan_studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes)
            {
                SaveScore(false);
                return _scorePath != null && _doc.Serialize() == _savedSnapshot;
            }
            return true;
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                SetStatus(I18n.L("后台任务仍在运行，请稍候或先停止播放",
                    "A background task is still running. Wait or stop playback first."));
                return;
            }
            if (!ConfirmDiscard()) e.Cancel = true;
            else StopPlayback();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0;
            bool shift = (keyData & Keys.Shift) != 0;
            bool editing = IsTextEditing(this);

            if (key == Keys.F1)
            {
                ShowLegalDocuments();
                return true;
            }
            if (ctrl && key == Keys.S) { SaveScore(shift); return true; }
            if (ctrl && key == Keys.O) { OpenScore(); return true; }
            if (ctrl && key == Keys.N) { NewProject(); return true; }
            if (ctrl && key == Keys.I) { ImportMidi(); return true; }
            if (ctrl && key == Keys.F) { _library.FocusSearch(); return true; }
            if (key == Keys.F5) { RenderAndPlay(); return true; }

            // 文本输入框优先接收文本编辑、方向键、空格和选区快捷键。
            if (editing) return base.ProcessCmdKey(ref msg, keyData);

            if (ctrl && key == Keys.Z) { Undo(); return true; }
            if (ctrl && key == Keys.Y) { Redo(); return true; }
            if (ctrl && key == Keys.A) { _timeline.SelectAll(); return true; }
            if (ctrl && key == Keys.D) { _timeline.DuplicateSelection(); return true; }
            if (ctrl && key == Keys.Home)
            {
                _timeline.SetPlayhead(0, true);
                return true;
            }
            if (key == Keys.Home)
            {
                _timeline.SetPlayhead(0, true);
                return true;
            }
            if (key == Keys.End)
            {
                _timeline.SetPlayhead(_doc == null ? 0 : _doc.DurationBeats, true);
                return true;
            }
            if (ctrl && (key == Keys.Left || key == Keys.Right))
            {
                double next = Math.Round(_timeline.PlayheadBeat / 4.0) * 4.0 +
                    (key == Keys.Left ? -4.0 : 4.0);
                _timeline.SetPlayhead(Math.Max(0, next), true);
                return true;
            }
            if (ctrl && (key == Keys.Up || key == Keys.Down))
            {
                _timeline.NudgeSelection(0, key == Keys.Up ? 12 : -12);
                return true;
            }
            if (ctrl && key == Keys.D0)
            {
                _timeline.Fit();
                SetStatus(I18n.L("已适应内容", "Content fitted to view"));
                return true;
            }
            if (ctrl && (key == Keys.Oemplus || key == Keys.Add))
            {
                _timeline.ZoomIn();
                return true;
            }
            if (ctrl && (key == Keys.OemMinus || key == Keys.Subtract))
            {
                _timeline.ZoomOut();
                return true;
            }
            if (ctrl && key >= Keys.D1 && key <= Keys.D9 && _doc != null)
            {
                int index = key - Keys.D1;
                if (index >= 0 && index < _doc.Voices.Count)
                {
                    EditorVoice voice = _doc.Voices[index];
                    _timeline.SetSelectedVoice(voice);
                    _inspector.ShowSelection(_doc, voice, new List<EditorNote>());
                    SetStatus(I18n.L("已切换到声部 ", "Selected voice ") + voice.Label);
                }
                return true;
            }
            if (keyData == Keys.Space)
            {
                if (_playing) StopPlayback(); else RenderAndPlay();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (_playing) StopPlayback();
                else _timeline.ClearSelection();
                return true;
            }
            if (keyData == Keys.Delete) { _timeline.DeleteSelection(); return true; }
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
            {
                if (_timeline.SelectedNotes.Count > 0)
                {
                    if (shift && (key == Keys.Left || key == Keys.Right))
                    {
                        double amount = key == Keys.Left ? -0.25 : 0.25;
                        string before = _doc.Serialize();
                        foreach (var note in _timeline.SelectedNotes)
                            note.Beats = Math.Max(0.25, note.Beats + amount);
                        RecordEdit(before);
                        _timeline.RefreshDiagnostics();
                    }
                    else
                    {
                        _timeline.NudgeSelection(
                            key == Keys.Left ? -0.25 : (key == Keys.Right ? 0.25 : 0),
                            key == Keys.Up ? 1 : (key == Keys.Down ? -1 : 0));
                    }
                }
                else if (key == Keys.Left || key == Keys.Right)
                {
                    _timeline.SetPlayhead(_timeline.PlayheadBeat +
                        (key == Keys.Left ? -0.25 : 0.25), true);
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        bool IsTextEditing(Control root)
        {
            Control focused = FindFocusedControl(root);
            if (focused == null) return false;
            if (focused is TextBoxBase) return true;
            if (focused is NumericUpDown || focused is ComboBox) return true;
            return focused is DarkNumeric;
        }

        static Control FindFocusedControl(Control parent)
        {
            if (parent == null) return null;
            if (parent.Focused) return parent;
            foreach (Control child in parent.Controls)
            {
                Control found = FindFocusedControl(child);
                if (found != null) return found;
            }
            return null;
        }

        public void RunUiTest()
        {
            var log = new StringBuilder();
            var t = new System.Windows.Forms.Timer();
            int step = 0;
            t.Interval = 250;
            t.Tick += delegate
            {
                step++;
                try
                {
                    if (step == 1)
                    {
                        log.AppendLine("1 文档声部 = " + _doc.Voices.Count + "，音符 = " + CountNotes());
                        log.AppendLine("  窗口 = " + Width + "x" + Height + "，时间线 = " + _timeline.Width + "x" + _timeline.Height);
                        foreach (var v in _doc.Voices)
                            log.AppendLine("  " + v.Label + " / " + v.ModelText + " / " + v.Notes.Count + " 音符");
                    }
                    else if (step == 2)
                    {
                        EditorVoice v = FirstSoundingVoice();
                        if (v == null) throw new InvalidOperationException("没有可编辑音符");
                        _timeline.SelectNote(v, v.Notes[0]);
                        log.AppendLine("2 选中音符 = " + v.Label + " " + ScoreParser.MidiToName(v.Notes[0].Midi));
                    }
                    else if (step == 3)
                    {
                        int before = CountNotes();
                        string snap = _doc.Serialize();
                        EditorVoice v = FirstSoundingVoice();
                        if (v == null) throw new InvalidOperationException("没有可编辑声部");
                        v.Notes.Add(new EditorNote
                        {
                            Midi = 60,
                            StartBeat = _doc.DurationBeats + 1,
                            Beats = 1
                        });
                        RecordEdit(snap);
                        log.AppendLine("3 添加音符 = " + before + " -> " + CountNotes());
                    }
                    else if (step == 4)
                    {
                        int after = CountNotes();
                        Undo();
                        log.AppendLine("4 撤销后音符 = " + CountNotes() + "（编辑前 " + after + "）");
                    }
                    else if (step == 5)
                    {
                        string text = _doc.Serialize();
                        EditorDocument copy = EditorDocument.Load(text, _catalog);
                        log.AppendLine("5 fscore 往返 = " + copy.Voices.Count + " 声部 / " + CountNotes(copy) + " 音符");
                        foreach (var v in copy.Voices)
                            log.AppendLine("  " + v.Label + " / " + v.Notes.Count + " 音符");
                    }
                    else if (step == 6)
                    {
                        log.AppendLine("6 风险 = " + _doc.CountWarnings() + "，最大并发 = " + _doc.MaxConcurrent());
                        log.AppendLine("  状态 = " + _statusText.Text);
                    }
                    else if (step == 7)
                    {
                        AuditionPitch(0);
                        log.AppendLine("7 超音域试听 -> " + _statusText.Text);
                    }
                    else if (step == 8)
                    {
                        log.AppendLine("RESULT: OK 新版编辑器交互路径无异常");
                        t.Stop();
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_test.log"),
                            log.ToString(), Encoding.UTF8);
                        _suppressPrompt = true;
                        _savedSnapshot = _doc.Serialize();
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine("FAIL: " + ex);
                    t.Stop();
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_test.log"),
                        log.ToString(), Encoding.UTF8);
                    _suppressPrompt = true;
                    _savedSnapshot = _doc.Serialize();
                    Close();
                }
            };
            t.Start();
        }

        int CountNotes() { return CountNotes(_doc); }

        EditorVoice FirstSoundingVoice()
        {
            foreach (var v in _doc.Voices) if (v.Notes.Count > 0) return v;
            return null;
        }

        int CountNotes(EditorDocument doc)
        {
            int n = 0;
            foreach (var v in doc.Voices) n += v.Notes.Count;
            return n;
        }

        public void SaveSnapshot(string path)
        {
            StopPlayback();
            if (_doc.Voices.Count > 0 && _doc.Voices[0].Notes.Count > 0)
            {
                EditorNote note = _doc.Voices[0].Notes[2 < _doc.Voices[0].Notes.Count ? 2 : 0];
                _timeline.SelectNote(_doc.Voices[0], note);
                _timeline.SetHoverForSnapshot(note.Midi);
            }
            _timeline.SetPlayhead(2.6, true);
            Refresh();
            Application.DoEvents();
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, ImageFormat.Png);
            }
            _suppressPrompt = true;
            _savedSnapshot = _doc.Serialize();
        }
    }

    class RenderRequest
    {
        public readonly string ScoreText;
        public readonly double Bpm;
        public readonly EditorVoice Voice;
        public RenderRequest(string text, double bpm, EditorVoice voice)
        {
            ScoreText = text;
            Bpm = bpm;
            Voice = voice;
        }
    }

    class RenderResult
    {
        public string WavPath;
        public string Status;
        public string Report;
        public double Duration;
    }
}
