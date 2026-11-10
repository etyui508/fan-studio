// EditorModel.cs —— 图形编辑器使用的文档层
// 保持 .fscore 为唯一落盘格式，但内存中把声部和绝对拍位显式建模，
// 这样钢琴卷帘可以移动、拉伸音符，而不受文本行顺序限制。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FanStudio
{
    public class EditorNote
    {
        public int Midi;
        public double StartBeat;
        public double Beats;

        // 排程结果，只用于界面诊断，不写回文件。
        public double LastRpm;
        public double LastDuty;
        public double LastFb;
        public int LastHarm;
        public string LastWarn = "";

        public double EndBeat { get { return StartBeat + Beats; } }

        public EditorNote Copy()
        {
            return (EditorNote)MemberwiseClone();
        }

        public bool IsRest { get { return Midi < 0; } }
    }

    public class EditorVoice
    {
        public string Label;
        public Motor Motor;
        public bool LayoutExplicit;
        public List<EditorNote> Notes = new List<EditorNote>();

        public double LoMidi
        {
            get
            {
                if (Motor == null || Motor.LoHz <= 0) return 0;
                return 69.0 + 12.0 * Math.Log(Motor.LoHz / 440.0, 2.0);
            }
        }

        public double HiMidi
        {
            get
            {
                if (Motor == null || Motor.HiHz <= 0) return 127;
                return 69.0 + 12.0 * Math.Log(Motor.HiHz / 440.0, 2.0);
            }
        }

        public double MinimumBeats(double bpm)
        {
            if (Motor == null || bpm <= 0) return 0;
            return 3.5 * Motor.Glide / (60.0 / bpm);
        }

        public string ModelText
        {
            get { return Motor == null ? "未指定" : Motor.Model; }
        }

        public EditorNote NoteAt(double beat)
        {
            foreach (var n in Notes)
                if (beat >= n.StartBeat - 1e-6 && beat < n.EndBeat - 1e-6) return n;
            return null;
        }
    }

    public class EditorDocument
    {
        public double Bpm = 120;
        public string Title = "未命名";
        public List<EditorVoice> Voices = new List<EditorVoice>();

        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public double DurationBeats
        {
            get
            {
                double end = 0;
                foreach (var v in Voices)
                    foreach (var n in v.Notes)
                        end = Math.Max(end, n.EndBeat);
                return end;
            }
        }

        public double BeatSeconds { get { return 60.0 / Math.Max(1.0, Bpm); } }

        public static EditorDocument CreateDemo(List<Motor> catalog)
        {
            var doc = new EditorDocument();
            doc.Title = "fan_studio 新项目";
            string[] keys = { "fan40", "fan80", "fan120", "nema17" };
            int[] offsets = { 7, 3, 0, 5 };
            double[] starts = { 0, 0, 0, 0 };
            double[] lengths = { 0.75, 1.0, 2.0, 0.5 };
            int[] motif = { 0, 4, 7, 12, 7, 4, 0, 4 };

            for (int k = 0; k < keys.Length; k++)
            {
                Motor baseMotor = FindBase(catalog, keys[k]);
                if (baseMotor == null) continue;
                var v = doc.NewVoice(baseMotor, starts[k], -1);
                int lo = (int)Math.Ceiling(v.LoMidi);
                int hi = (int)Math.Floor(v.HiMidi);
                int mid = Math.Max(lo, Math.Min(hi, (lo + hi) / 2 + offsets[k]));
                for (int i = 0; i < motif.Length; i++)
                {
                    int pitch = mid + motif[i];
                    if (pitch > hi) pitch = hi;
                    if (pitch < lo) pitch = lo;
                    v.Notes.Add(new EditorNote
                    {
                        Midi = pitch,
                        StartBeat = i * 1.0,
                        Beats = Math.Max(lengths[k], v.MinimumBeats(doc.Bpm))
                    });
                }
            }
            doc.Normalize();
            return doc;
        }

        static Motor FindBase(List<Motor> catalog, string key)
        {
            foreach (var m in catalog) if (m.Key == key) return m;
            return null;
        }

        public EditorVoice NewVoice(Motor baseMotor, double startBeat, int midi)
        {
            var motor = baseMotor == null ? MotorCatalog.Fallback() : baseMotor.Clone();
            var v = new EditorVoice();
            v.Motor = motor;
            v.Label = UniqueLabel(motor.Key);
            Voices.Add(v);
            if (midi >= 0)
            {
                v.Notes.Add(new EditorNote
                {
                    Midi = midi,
                    StartBeat = Math.Max(0, startBeat),
                    Beats = Math.Max(0.25, Math.Round(v.MinimumBeats(Bpm) * 4.0) / 4.0)
                });
            }
            return v;
        }

        public string UniqueLabel(string key)
        {
            if (key.Length == 0) key = "voice";
            int n = 1;
            string candidate = key;
            while (FindVoice(candidate) != null)
            {
                n++;
                candidate = key + "-" + n.ToString(CI);
            }
            return candidate;
        }

        public EditorVoice FindVoice(string label)
        {
            foreach (var v in Voices)
                if (string.Equals(v.Label, label, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        public void RemoveVoice(EditorVoice v)
        {
            Voices.Remove(v);
            Normalize();
        }

        public static EditorDocument Load(string text, List<Motor> catalog)
        {
            var doc = new EditorDocument();
            EditorVoice cur = null;
            EditorVoice implicitVoice = null;
            Motor defaultMotor = MotorCatalog.Fallback();
            double beat = 0;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1).TrimStart();
                line = StripComment(line).Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string label = line.Substring(1, line.Length - 2).Trim();
                    if (label.ToLowerInvariant().StartsWith("voice "))
                        label = label.Substring(6).Trim();
                    Motor baseMotor = ScoreParser.FindMotor(catalog, label);
                    cur = new EditorVoice();
                    cur.Label = label;
                    cur.Motor = baseMotor == null ? defaultMotor.Clone() : baseMotor.Clone();
                    if (baseMotor == null)
                    {
                        cur.Motor.Key = label;
                        cur.Motor.Model = label;
                        cur.Motor.Family = "自定义声部";
                        cur.Motor.Semantics = "参数化声部";
                        cur.Motor.Role = "自定义";
                        cur.Motor.Note = "由 .fscore 中的显式物理参数定义";
                    }
                    doc.Voices.Add(cur);
                    beat = 0;
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "bpm")
                    {
                        double b;
                        if (double.TryParse(val, NumberStyles.Float, CI, out b) && b > 0) doc.Bpm = b;
                    }
                    else if (cur != null)
                    {
                        ApplyParam(cur, key, val);
                    }
                    else
                    {
                        var temp = new EditorVoice();
                        temp.Motor = defaultMotor;
                        ApplyParam(temp, key, val);
                        defaultMotor = temp.Motor;
                    }
                    continue;
                }

                if (cur == null && implicitVoice == null)
                {
                    if (!LooksLikeNotes(line)) continue;
                    implicitVoice = new EditorVoice();
                    implicitVoice.Label = "main";
                    implicitVoice.Motor = defaultMotor.Clone();
                    doc.Voices.Add(implicitVoice);
                    cur = implicitVoice;
                }
                EditorVoice target = cur == null ? implicitVoice : cur;
                string[] tokens = line.Replace("|", " ").Split(
                    new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string pitchText = token;
                    string durText = "1";
                    int colon = token.IndexOf(':');
                    if (colon > 0)
                    {
                        pitchText = token.Substring(0, colon);
                        durText = token.Substring(colon + 1);
                    }
                    double beats;
                    if (!double.TryParse(durText, NumberStyles.Float, CI, out beats)) beats = 1;
                    beats = Math.Max(0.05, beats);
                    int midi;
                    try { midi = ScoreParser.NoteToMidi(pitchText); }
                    catch { midi = -1; }
                    if (midi >= 0)
                    {
                        target.Notes.Add(new EditorNote
                        {
                            Midi = midi,
                            StartBeat = beat,
                            Beats = beats
                        });
                    }
                    beat += beats;
                }
            }
            InferCustomMotors(doc, catalog);
            doc.Normalize();
            return doc;
        }

        static bool LooksLikeNotes(string line)
        {
            string[] tokens = line.Replace("|", " ").Split(
                new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string name = token;
                int colon = token.IndexOf(':');
                if (colon > 0) name = token.Substring(0, colon);
                try
                {
                    ScoreParser.NoteToMidi(name);
                    return true;
                }
                catch { }
            }
            return false;
        }

        static void InferCustomMotors(EditorDocument doc, List<Motor> catalog)
        {
            foreach (var v in doc.Voices)
            {
                if (v.Motor.Family != "自定义声部") continue;
                Motor best = null;
                double bestDiff = double.MaxValue;
                foreach (var m in catalog)
                {
                    if (m.Kind != "revolve") continue;
                    double diff = Math.Abs(m.Blades - v.Motor.Blades) * 1000.0 +
                        Math.Abs(m.Harmonic - v.Motor.Harmonic) * 1000.0 +
                        Math.Abs(m.MinRpm - v.Motor.MinRpm) / 1000.0 +
                        Math.Abs(m.MaxRpm - v.Motor.MaxRpm) / 1000.0 +
                        Math.Abs(m.StartDuty - v.Motor.StartDuty) * 100.0 +
                        Math.Abs(m.Glide - v.Motor.Glide) * 100.0;
                    if (diff < bestDiff) { bestDiff = diff; best = m; }
                }
                if (best != null && bestDiff < 1e-6)
                {
                    double pan = v.Motor.Pan, gain = v.Motor.Gain, delay = v.Motor.DelayMs;
                    bool layout = v.LayoutExplicit;
                    v.Motor = best.Clone();
                    v.Motor.Pan = pan;
                    v.Motor.Gain = gain;
                    v.Motor.DelayMs = delay;
                    v.LayoutExplicit = layout;
                }
            }
        }

        static string StripComment(string line)
        {
            int c1 = line.IndexOf(';');
            int c2 = line.IndexOf("//", StringComparison.Ordinal);
            int cut = -1;
            if (c1 >= 0) cut = c1;
            if (c2 >= 0 && (cut < 0 || c2 < cut)) cut = c2;
            return cut >= 0 ? line.Substring(0, cut) : line;
        }

        static void ApplyParam(EditorVoice v, string key, string val)
        {
            double d;
            if (key == "harmonic" && val.Equals("auto", StringComparison.OrdinalIgnoreCase)) d = 1;
            else if (!double.TryParse(val, NumberStyles.Float, CI, out d)) return;
            Motor m = v.Motor;
            switch (key)
            {
                case "blades": m.Blades = Math.Max(1, (int)Math.Round(d)); break;
                case "harmonic": m.Harmonic = Math.Max(1, d); break;
                case "glide": m.Glide = Math.Max(0.001, d); break;
                case "min_rpm": m.MinRpm = Math.Max(0, d); break;
                case "max_rpm": m.MaxRpm = Math.Max(1, d); break;
                case "start_duty": m.StartDuty = Math.Max(0.01, Math.Min(1, d)); break;
                case "pan": m.Pan = Math.Max(-1, Math.Min(1, d)); v.LayoutExplicit = true; break;
                case "delay_ms": m.DelayMs = Math.Max(0, d); v.LayoutExplicit = true; break;
                case "gain": m.Gain = Math.Max(0, d); v.LayoutExplicit = true; break;
                case "noise_center": m.NoiseCenter = Math.Max(20, d); break;
                case "timbre": m.Timbre = Math.Max(0.2, d); break;
                case "noise_gain": m.NoiseGain = Math.Max(0, d); break;
            }
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# fan_studio 2 editor project");
            sb.AppendLine("bpm = " + F(Bpm));
            sb.AppendLine();
            foreach (var v in Voices)
            {
                Motor m = v.Motor;
                sb.AppendLine("[voice " + v.Label + "]");
                sb.AppendLine("blades = " + m.Blades.ToString(CI));
                sb.AppendLine("harmonic = " + F(m.Harmonic));
                sb.AppendLine("glide = " + F(m.Glide));
                sb.AppendLine("min_rpm = " + F(m.MinRpm));
                sb.AppendLine("max_rpm = " + F(m.MaxRpm));
                sb.AppendLine("start_duty = " + F(m.StartDuty));
                if (v.LayoutExplicit)
                {
                    sb.AppendLine("pan = " + F(m.Pan));
                    sb.AppendLine("delay_ms = " + F(m.DelayMs));
                    sb.AppendLine("gain = " + F(m.Gain));
                }
                sb.AppendLine("noise_center = " + F(m.NoiseCenter));
                sb.AppendLine("timbre = " + F(m.Timbre));
                sb.AppendLine("noise_gain = " + F(m.NoiseGain));

                var notes = new List<EditorNote>(v.Notes);
                notes.Sort(delegate(EditorNote a, EditorNote b) { return a.StartBeat.CompareTo(b.StartBeat); });
                double beat = 0;
                var tokens = new List<string>();
                foreach (var n in notes)
                {
                    if (n.StartBeat > beat + 1e-6)
                    {
                        tokens.Add("r:" + F(Math.Round((n.StartBeat - beat) * 1000.0) / 1000.0));
                        beat = n.StartBeat;
                    }
                    double duration = Math.Max(0.05, Math.Round(n.Beats * 1000.0) / 1000.0);
                    tokens.Add(ScoreParser.MidiToName(n.Midi) + ":" + F(duration));
                    beat = Math.Max(beat, n.StartBeat) + duration;
                }
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(tokens[i]);
                    if ((i + 1) % 8 == 0) sb.AppendLine();
                }
                if (tokens.Count > 0 && tokens.Count % 8 != 0) sb.AppendLine();
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string F(double v)
        {
            return v.ToString("0.######", CI);
        }

        public Score BuildScore()
        {
            var sc = new Score();
            sc.Bpm = Bpm;
            var maps = new List<KeyValuePair<Voice, List<EditorNote>>>();

            foreach (var v in Voices)
            {
                var sv = new Voice();
                sv.Label = v.Label;
                sv.Motor = v.Motor.Clone();
                var noteMap = new List<EditorNote>();
                var notes = new List<EditorNote>(v.Notes);
                notes.Sort(delegate(EditorNote a, EditorNote b) { return a.StartBeat.CompareTo(b.StartBeat); });
                double beat = 0;
                foreach (var n in notes)
                {
                    if (n.StartBeat > beat + 1e-6)
                    {
                        sv.Raw.Add(new NoteEv { Midi = -1, Beats = n.StartBeat - beat });
                        noteMap.Add(null);
                        beat = n.StartBeat;
                    }
                    double start = Math.Max(beat, n.StartBeat);
                    double duration = Math.Max(0.05, n.Beats);
                    sv.Raw.Add(new NoteEv { Midi = n.Midi, Beats = duration });
                    noteMap.Add(n);
                    beat = start + duration;
                }
                sc.Voices.Add(sv);
                maps.Add(new KeyValuePair<Voice, List<EditorNote>>(sv, noteMap));
            }

            Planner.Build(sc);
            foreach (var pair in maps)
            {
                Voice sv = pair.Key;
                List<EditorNote> noteMap = pair.Value;
                int count = Math.Min(sv.Ev.Count, noteMap.Count);
                for (int i = 0; i < count; i++)
                {
                    EditorNote note = noteMap[i];
                    if (note == null) continue;
                    NoteEv ev = sv.Ev[i];
                    note.LastRpm = ev.Rpm;
                    note.LastDuty = ev.Duty;
                    note.LastFb = ev.FBp;
                    note.LastHarm = ev.Harm;
                    note.LastWarn = ev.Warn;
                }
            }
            return sc;
        }

        public int CountWarnings()
        {
            int count = 0;
            foreach (var v in Voices)
                foreach (var n in v.Notes)
                {
                    if (n.LastWarn.Length > 0) count++;
                    else if (n.Beats + 1e-6 < v.MinimumBeats(Bpm)) count++;
                }
            return count;
        }

        public int MaxConcurrent()
        {
            var marks = new List<KeyValuePair<double, int>>();
            foreach (var v in Voices)
                foreach (var n in v.Notes)
                {
                    marks.Add(new KeyValuePair<double, int>(n.StartBeat, 1));
                    marks.Add(new KeyValuePair<double, int>(n.EndBeat, -1));
                }
            marks.Sort(delegate(KeyValuePair<double, int> a, KeyValuePair<double, int> b)
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : a.Value.CompareTo(b.Value);
            });
            int current = 0, max = 0;
            foreach (var mark in marks)
            {
                current += mark.Value;
                if (current > max) max = current;
            }
            return max;
        }

        public void Normalize()
        {
            foreach (var v in Voices)
            {
                v.Notes.Sort(delegate(EditorNote a, EditorNote b)
                {
                    int c = a.StartBeat.CompareTo(b.StartBeat);
                    return c != 0 ? c : a.Midi.CompareTo(b.Midi);
                });
                double end = 0;
                foreach (var n in v.Notes)
                {
                    n.StartBeat = Math.Max(0, Math.Max(end, n.StartBeat));
                    n.Beats = Math.Max(0.05, n.Beats);
                    n.Midi = Math.Max(0, Math.Min(127, n.Midi));
                    end = n.EndBeat;
                }
            }
        }
    }

    public class SnapshotHistory
    {
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        public void Record(string before)
        {
            if (before == null) return;
            _undo.Add(before);
            if (_undo.Count > 80) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public string Undo(string current)
        {
            if (_undo.Count == 0) return null;
            string text = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(current);
            return text;
        }

        public string Redo(string current)
        {
            if (_redo.Count == 0) return null;
            string text = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(current);
            return text;
        }
    }
}
