// FanCore.cs —— 电机乐器引擎(C# 版, 与 fan_music/*.py 同一套物理模型)
// 只用 C# 5 语法, 因为本机只有 .NET Framework 4.x 自带的 csc.exe。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FanStudio
{
    // ───────────────────────────── 电机型号 ─────────────────────────────
    public class Motor
    {
        public string Key, Model, Family, Semantics, Kind, Role, Note = "";
        public int Blades;
        public double MinRpm, MaxRpm, StartDuty, Glide, Harmonic, Timbre, NoiseCenter, NoiseGain;

        // 每台电机在编制里的摆位。C# 的 double 字段不给初值就是 0, 所以这里必须显式初始化,
        // 否则 Gain=0 会把整个混音乘成静音。
        public bool Enabled = true;
        public double Pan = 0.0;
        public double Gain = 1.0;
        public double DelayMs = 0.0;
        public bool LayoutFromFile = false;

        public double BeatFreq(double rpm) { return rpm * Blades / 60.0; }
        public double RpmFor(double f) { return f * 60.0 / Blades; }

        public double LoHz { get { double k = Harmonic < 1 ? 1 : Harmonic; return MinRpm * Blades / 60.0 * k; } }
        public double HiHz { get { double k = Harmonic < 1 ? 1 : Harmonic; return MaxRpm * Blades / 60.0 * k; } }
        public bool Playable { get { return Kind == "revolve"; } }

        public double DutyFor(double rpm)
        {
            double span = Math.Max(MaxRpm - MinRpm, 1e-9);
            double frac = Math.Min(1.0, Math.Max(0.0, (rpm - MinRpm) / span));
            return StartDuty + frac * (1.0 - StartDuty);
        }

        public Motor Clone()
        {
            var m = (Motor)MemberwiseClone();
            m.Enabled = true;
            return m;
        }
    }

    public static class MotorCatalog
    {
        public static List<Motor> Load(string path)
        {
            var list = new List<Motor>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)          // 跳过表头
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] f = line.Split('|');
                if (f.Length < 15) continue;
                var m = new Motor();
                m.Key = f[0]; m.Model = f[1]; m.Family = f[2]; m.Semantics = f[3];
                m.Blades = Int(f[4]);
                m.MinRpm = Dbl(f[5]); m.MaxRpm = Dbl(f[6]);
                m.StartDuty = Dbl(f[7]); m.Glide = Dbl(f[8]);
                m.Harmonic = Dbl(f[9]); m.Timbre = Dbl(f[10]);
                m.NoiseCenter = Dbl(f[11]); m.NoiseGain = Dbl(f[12]);
                m.Kind = f[13]; m.Role = f[14];
                if (f.Length > 15) m.Note = f[15];
                list.Add(m);
            }
            return list;
        }

        public static Motor Fallback()
        {
            var m = new Motor();
            m.Key = "fan120"; m.Model = "120mm 静音扇"; m.Family = "轴流风机";
            m.Semantics = "叶片通过"; m.Kind = "revolve"; m.Role = "低音";
            m.Blades = 7; m.MinRpm = 500; m.MaxRpm = 1500; m.StartDuty = 0.18;
            m.Glide = 0.18; m.Harmonic = 1; m.Timbre = 1.35;
            m.NoiseCenter = 260; m.NoiseGain = 0.9;
            m.Note = "内置兜底型号";
            return m;
        }

        static double Dbl(string s)
        {
            double v;
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }
        static int Int(string s) { return (int)Math.Round(Dbl(s)); }
    }

    // ───────────────────────────── 乐谱 ─────────────────────────────
    public class NoteEv
    {
        public int Midi;            // -1 = 休止
        public double Beats;
        // 排程后填进来
        public double T0, T1, CmdT0;
        public double Freq, FBp, Rpm, Duty;
        public int Harm;
        public string Warn = "";
    }

    public class Voice
    {
        public Motor Motor;
        public string Label;
        public List<NoteEv> Raw = new List<NoteEv>();
        public List<NoteEv> Ev = new List<NoteEv>();
    }

    public class Score
    {
        public double Bpm = 120;
        public List<Voice> Voices = new List<Voice>();
        public List<string> Warnings = new List<string>();       // 排程阶段(音域越界等)
        public List<string> ParseWarnings = new List<string>();  // 解析阶段(找不到声部/音符写错)

        public double BeatSeconds { get { return 60.0 / Bpm; } }

        public double Duration
        {
            get
            {
                double d = 0;
                foreach (var v in Voices)
                    if (v.Ev.Count > 0) d = Math.Max(d, v.Ev[v.Ev.Count - 1].T1);
                return d + 0.9;
            }
        }
    }

    public static class ScoreParser
    {
        static readonly Dictionary<string, int> Semi = new Dictionary<string, int>();

        static ScoreParser()
        {
            Semi["C"] = 0; Semi["D"] = 2; Semi["E"] = 4; Semi["F"] = 5;
            Semi["G"] = 7; Semi["A"] = 9; Semi["B"] = 11;
        }

        public static int NoteToMidi(string name)
        {
            string s = name.Trim();
            if (s.Length == 0) throw new FormatException("空音符");
            string low = s.ToLowerInvariant();
            if (low == "r" || low == "rest" || low == "-" || low == ".") return -1;

            int i = 0;
            string letter = s.Substring(0, 1).ToUpperInvariant();
            if (!Semi.ContainsKey(letter)) throw new FormatException("无法解析音符: " + name);
            i = 1;
            int acc = 0;
            while (i < s.Length && (s[i] == '#' || s[i] == 'b'))
            {
                acc += s[i] == '#' ? 1 : -1;
                i++;
            }
            if (i >= s.Length) throw new FormatException("音符缺少八度: " + name);
            int octave = int.Parse(s.Substring(i), CultureInfo.InvariantCulture);
            return 12 * (octave + 1) + Semi[letter] + acc;
        }

        public static string MidiToName(int midi)
        {
            string[] n = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            if (midi < 0) return "r";
            return n[midi % 12] + (midi / 12 - 1).ToString(CultureInfo.InvariantCulture);
        }

        public static double MidiToFreq(int midi, double detune)
        {
            return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0) * Math.Pow(2.0, detune / 1200.0);
        }

        /// <summary>解析 .fscore。模板只提供 bpm 与各声部音符, 参数一律取自传入的电机对象。</summary>
        public static Score Parse(string text, List<Motor> catalog, bool applyFileParams)
        {
            var sc = new Score();
            Voice cur = null;
            Voice implicitVoice = null;
            Motor implicitMotor = MotorCatalog.Fallback();
            var byLabel = new Dictionary<string, Voice>();

            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1).TrimStart();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int c1 = line.IndexOf(';');
                int c2 = line.IndexOf("//", StringComparison.Ordinal);
                int cut = -1;
                if (c1 >= 0) cut = c1;
                if (c2 >= 0 && (cut < 0 || c2 < cut)) cut = c2;
                if (cut >= 0) line = line.Substring(0, cut).Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string label = line.Substring(1, line.Length - 2).Trim();
                    // [voice fan40] 里的 voice 是关键字, 不是声部名
                    if (label.ToLowerInvariant().StartsWith("voice "))
                        label = label.Substring(6).Trim();
                    var m = FindMotor(catalog, label);
                    if (m != null) m = m.Clone();
                    else
                    {
                        m = MotorCatalog.Fallback();
                        m.Key = label;
                        m.Model = label;
                        m.Family = "自定义声部";
                        m.Semantics = "参数化声部";
                        m.Role = "自定义";
                        m.Note = "由 .fscore 中的显式物理参数定义";
                    }
                    cur = new Voice();
                    cur.Motor = m;
                    cur.Label = label;
                    byLabel[label] = cur;
                    sc.Voices.Add(cur);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0 && IsKeyStart(line))
                {
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "bpm") sc.Bpm = ParseD(val, sc.Bpm);
                    else if (applyFileParams && cur != null) ApplyParam(cur.Motor, key, val);
                    else if (applyFileParams && cur == null) ApplyParam(implicitMotor, key, val);
                    continue;
                }

                if (cur == null && implicitVoice == null)
                {
                    implicitVoice = new Voice();
                    implicitVoice.Motor = implicitMotor;
                    implicitVoice.Label = "main";
                    sc.Voices.Add(implicitVoice);
                }
                Voice target = cur == null ? implicitVoice : cur;
                foreach (string tok in line.Replace("|", " ").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string nm = tok; string du = "1";
                    int colon = tok.IndexOf(':');
                    if (colon > 0) { nm = tok.Substring(0, colon); du = tok.Substring(colon + 1); }
                    try
                    {
                        var ev = new NoteEv();
                        ev.Midi = NoteToMidi(nm);
                        ev.Beats = Math.Max(0.05, ParseD(du, 1));
                        target.Raw.Add(ev);
                    }
                    catch (Exception ex) { sc.ParseWarnings.Add("音符 " + tok + " 解析失败: " + ex.Message); }
                }
            }
            return sc;
        }

        static bool IsKeyStart(string line)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) return false;
            string k = line.Substring(0, eq).Trim();
            if (k.Length == 0) return false;
            foreach (char c in k)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        static void ApplyParam(Motor m, string key, string val)
        {
            switch (key)
            {
                case "blades": m.Blades = (int)Math.Round(ParseD(val, m.Blades)); break;
                case "harmonic": m.Harmonic = ParseD(val, m.Harmonic); break;
                case "glide": m.Glide = ParseD(val, m.Glide); break;
                case "min_rpm": m.MinRpm = ParseD(val, m.MinRpm); break;
                case "max_rpm": m.MaxRpm = ParseD(val, m.MaxRpm); break;
                case "start_duty": m.StartDuty = ParseD(val, m.StartDuty); break;
                case "pan": m.Pan = ParseD(val, m.Pan); m.LayoutFromFile = true; break;
                case "delay_ms": m.DelayMs = ParseD(val, m.DelayMs); m.LayoutFromFile = true; break;
                case "gain": m.Gain = ParseD(val, m.Gain); m.LayoutFromFile = true; break;
                case "noise_center": m.NoiseCenter = ParseD(val, m.NoiseCenter); break;
                case "timbre": m.Timbre = ParseD(val, m.Timbre); break;
                case "noise_gain": m.NoiseGain = ParseD(val, m.NoiseGain); break;
            }
        }

        static double ParseD(string s, double fallback)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        public static Motor FindMotor(List<Motor> catalog, string label)
        {
            string baseKey = label;
            int dash = label.LastIndexOf('-');
            if (dash > 0)
            {
                string tail = label.Substring(dash + 1);
                int n;
                if (int.TryParse(tail, out n)) baseKey = label.Substring(0, dash);
            }
            foreach (var m in catalog) if (m.Key == baseKey) return m;
            foreach (var m in catalog) if (m.Model == label) return m;
            return null;
        }
    }

    // ───────────────────────────── 排程(饱和控制 + 抢拍) ─────────────────────────────
    public static class Planner
    {
        const double LeadIn = 0.25;

        public static double TransitionTime(double prev, double target, Motor m)
        {
            if (Math.Abs(target - prev) < 1e-6) return 0;
            double tau = Math.Max(m.Glide, 1e-4);
            double asym = target > prev ? m.MaxRpm : m.MinRpm;
            double denom = asym - prev;
            if (Math.Abs(denom) < 1e-9) return 0;
            double ratio = (asym - target) / denom;
            if (ratio <= 0 || ratio >= 1) return 0;
            return -tau * Math.Log(ratio);
        }

        public static void Build(Score sc)
        {
            sc.Warnings.Clear();

            // 谱子里没写摆位时, 按声部顺序自动摊开(和 Python 的 ensemble() 一致)
            bool anyFromFile = false;
            foreach (var v in sc.Voices) if (v.Motor.LayoutFromFile) anyFromFile = true;
            if (!anyFromFile && sc.Voices.Count > 0)
            {
                int n = sc.Voices.Count;
                for (int i = 0; i < n; i++)
                {
                    var m = sc.Voices[i].Motor;
                    m.Pan = n == 1 ? 0.0 : -0.6 + 1.2 * i / (n - 1);
                    m.DelayMs = 3.0 + 22.0 * (i / (double)Math.Max(1, n - 1));
                    m.Gain = Math.Max(0.45, 1.0 - 0.03 * i);
                }
            }

            foreach (var v in sc.Voices)
            {
                v.Ev.Clear();
                Motor m = v.Motor;
                double beat = sc.BeatSeconds;
                double t = LeadIn;
                double fmin = m.BeatFreq(m.MinRpm);
                double fmax = m.BeatFreq(m.MaxRpm);

                foreach (var raw in v.Raw)
                {
                    var e = new NoteEv();
                    e.Midi = raw.Midi;
                    e.Beats = raw.Beats;
                    e.T0 = t; e.T1 = t + raw.Beats * beat;
                    if (raw.Midi < 0)
                    {
                        e.Freq = 0; e.Harm = 0; e.FBp = fmin;
                        e.Rpm = m.MinRpm; e.Duty = m.StartDuty;
                    }
                    else
                    {
                        double f = ScoreParser.MidiToFreq(raw.Midi, 0);
                        int k;
                        if (m.Harmonic >= 1) k = (int)Math.Round(m.Harmonic);
                        else k = Math.Max(1, (int)Math.Ceiling(f / fmax - 1e-9));
                        double fb = f / k;
                        if (fb > fmax) { fb = fmax; e.Warn = "超出上限"; }
                        else if (fb < fmin) { fb = fmin; e.Warn = "低于下限"; }
                        if (k > 8) { e.Warn = "超出上限"; k = 8; }
                        e.Freq = f; e.Harm = k; e.FBp = fb;
                        e.Rpm = Math.Min(m.MaxRpm, Math.Max(m.MinRpm, m.RpmFor(fb)));
                        e.Duty = m.DutyFor(e.Rpm);
                        if (e.Warn != "") sc.Warnings.Add("[" + v.Label + "] " + ScoreParser.MidiToName(raw.Midi) + ": " + e.Warn);
                    }
                    v.Ev.Add(e);
                    t += raw.Beats * beat;
                }

                // 抢拍: 让换速刚好在起拍时结束
                double prevRpm = m.MinRpm;
                for (int i = 0; i < v.Ev.Count; i++)
                {
                    var e = v.Ev[i];
                    double lead = TransitionTime(prevRpm, e.Rpm, m);
                    double floor = i > 0 ? v.Ev[i - 1].T0 + 0.05 : 0.0;
                    e.CmdT0 = Math.Max(floor, Math.Min(e.T0, e.T0 - lead));
                    prevRpm = e.Rpm;
                }
            }
        }
    }

    // ───────────────────────────── 合成 ─────────────────────────────
    public static class Synth
    {
        const int Harmonics = 7;

        public static double[][] Render(Score sc, int fs, double noise, double reverb)
        {
            int n = (int)Math.Round(sc.Duration * fs);
            if (n <= 0) return new double[2][] { new double[0], new double[0] };
            var mix = new double[2][];
            mix[0] = new double[n]; mix[1] = new double[n];

            int seed = 12345;
            foreach (var v in sc.Voices)
            {
                double[] mono = RenderVoice(sc, v, fs, n, noise, ref seed);
                double theta = (v.Motor.Pan + 1.0) * Math.PI / 4.0;
                int delay = (int)Math.Round(Math.Max(0, v.Motor.DelayMs) * 1e-3 * fs);
                double gl = Math.Cos(theta) * v.Motor.Gain;
                double gr = Math.Sin(theta) * v.Motor.Gain;
                for (int i = 0; i < n; i++)
                {
                    int j = i - delay;
                    double x = j >= 0 ? mono[j] : 0.0;
                    mix[0][i] += gl * x;
                    mix[1][i] += gr * x;
                }
            }
            if (reverb > 0) ApplyReverb(mix, fs, reverb);
            Normalize(mix, 0.89);
            return mix;
        }

        static double[] RenderVoice(Score sc, Voice v, int fs, int n, double noise, ref int seed)
        {
            var m = v.Motor;
            var outBuf = new double[n];
            double[] rpm = SpeedCurve(sc, v, fs, n);
            double[] phase = new double[n];
            double acc = 0;
            double twoPiOverFs = 2.0 * Math.PI / fs;
            for (int i = 0; i < n; i++)
            {
                acc += m.BeatFreq(rpm[i]) * twoPiOverFs;
                phase[i] = acc;
            }

            double[] w = new double[Harmonics + 1];
            for (int k = 1; k <= Harmonics; k++) w[k] = 0.30 / Math.Pow(k, m.Timbre);

            foreach (var e in v.Ev)
            {
                if (e.Freq <= 0) continue;
                int i0 = (int)Math.Round(e.T0 * fs);
                int i1 = (int)Math.Round(e.T1 * fs);
                if (i0 < 0) i0 = 0;
                if (i1 > n) i1 = n;
                if (i1 <= i0) continue;
                double[] wk = (double[])w.Clone();
                if (e.Harm >= 1 && e.Harm <= Harmonics) wk[e.Harm] += 1.0;
                for (int i = i0; i < i1; i++)
                {
                    double vel = Math.Pow(Math.Min(1.0, rpm[i] / m.MaxRpm), 1.25);
                    double s = 0;
                    for (int k = 1; k <= Harmonics; k++)
                        if (wk[k] > 0) s += wk[k] * Math.Sin(k * phase[i]);
                    outBuf[i] += vel * s;
                }
            }

            // 气流噪声: 谐振带通 + 一阶低通
            var bp = new Biquad.BandPass(m.NoiseCenter, 1.2, fs);
            double lp = 0, lpCoef = Math.Exp(-2.0 * Math.PI * 1800.0 / fs);
            double peak = 0;
            var rng = new Random(seed++);
            var noiseBuf = new double[n];
            for (int i = 0; i < n; i++)
            {
                double white = rng.NextDouble() * 2 - 1;
                double b = bp.Process(white);
                lp = lpCoef * lp + (1 - lpCoef) * b;
                noiseBuf[i] = 0.6 * b + 0.4 * lp;
                if (Math.Abs(noiseBuf[i]) > peak) peak = Math.Abs(noiseBuf[i]);
            }
            if (peak > 0)
            {
                double scale = noise * 0.35 * m.NoiseGain / peak;
                for (int i = 0; i < n; i++)
                {
                    double vel = Math.Pow(Math.Min(1.0, rpm[i] / m.MaxRpm), 1.7);
                    outBuf[i] += scale * vel * noiseBuf[i];
                }
            }
            return outBuf;
        }

        static double[] SpeedCurve(Score sc, Voice v, int fs, int n)
        {
            var m = v.Motor;
            var rpm = new double[n];
            for (int i = 0; i < n; i++) rpm[i] = m.MinRpm;
            if (v.Ev.Count == 0) return rpm;

            double tau = Math.Max(m.Glide, 1e-4);
            double cur = m.MinRpm;
            for (int e = 0; e < v.Ev.Count; e++)
            {
                var ev = v.Ev[e];
                int i0 = (int)Math.Round(ev.CmdT0 * fs);
                int i1 = (e + 1 < v.Ev.Count) ? (int)Math.Round(v.Ev[e + 1].CmdT0 * fs) : n;
                if (i0 < 0) i0 = 0;
                if (i1 > n) i1 = n;
                if (i1 <= i0) continue;

                double target = ev.Rpm;
                if (Math.Abs(target - cur) < 1e-6)
                {
                    for (int i = i0; i < i1; i++) rpm[i] = target;
                    continue;
                }
                double asym = target > cur ? m.MaxRpm : m.MinRpm;
                double denom = asym - cur;
                double ratio = Math.Abs(denom) > 1e-9 ? (asym - target) / denom : 1.0;
                int hit = i1;
                if (ratio > 0 && ratio < 1) hit = i0 + (int)Math.Ceiling(-tau * Math.Log(ratio) * fs);
                if (hit < i0) hit = i0;
                if (hit > i1) hit = i1;
                for (int i = i0; i < hit; i++)
                {
                    double tsec = (i - i0) / (double)fs;
                    rpm[i] = asym + (cur - asym) * Math.Exp(-tsec / tau);
                }
                for (int i = hit; i < i1; i++) rpm[i] = target;
                cur = rpm[i1 - 1];
            }
            return rpm;
        }

        static void ApplyReverb(double[][] mix, int fs, double wet)
        {
            int[] delays = { (int)(0.0297 * fs), (int)(0.0371 * fs), (int)(0.0411 * fs), (int)(0.0437 * fs) };
            double[] fb = { 0.77, 0.73, 0.71, 0.69 };
            var wetBuf = new double[2][];
            for (int ch = 0; ch < 2; ch++)
            {
                wetBuf[ch] = new double[mix[ch].Length];
                for (int c = 0; c < 4; c++)
                {
                    int d = delays[c];
                    double[] buf = new double[mix[ch].Length + d];
                    for (int i = 0; i < mix[ch].Length; i++)
                    {
                        double y = mix[ch][i] + (i >= d ? fb[c] * buf[i - d] : 0);
                        buf[i] = y;
                        wetBuf[ch][i] += y * 0.25;
                    }
                }
            }
            double wr = 0, sr = 0;
            long cnt = 0;
            for (int ch = 0; ch < 2; ch++)
                for (int i = 0; i < mix[ch].Length; i++)
                {
                    wr += wetBuf[ch][i] * wetBuf[ch][i];
                    sr += mix[ch][i] * mix[ch][i];
                    cnt++;
                }
            double wetRms = Math.Sqrt(wr / Math.Max(1, cnt));
            double sigRms = Math.Sqrt(sr / Math.Max(1, cnt));
            double k = wetRms > 1e-9 ? sigRms / wetRms : 0;
            for (int ch = 0; ch < 2; ch++)
                for (int i = 0; i < mix[ch].Length; i++)
                    mix[ch][i] = (1 - wet) * mix[ch][i] + wet * wetBuf[ch][i] * k;
        }

        static void Normalize(double[][] mix, double target)
        {
            double peak = 0;
            for (int ch = 0; ch < 2; ch++)
                for (int i = 0; i < mix[ch].Length; i++)
                    if (Math.Abs(mix[ch][i]) > peak) peak = Math.Abs(mix[ch][i]);
            if (peak < 1e-12) return;
            double g = target / peak;
            for (int ch = 0; ch < 2; ch++)
                for (int i = 0; i < mix[ch].Length; i++)
                    mix[ch][i] *= g;
        }
    }

    public static class Biquad
    {
        public class BandPass
        {
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public BandPass(double f0, double q, int fs)
            {
                f0 = Math.Min(f0, fs * 0.45);
                double w0 = 2 * Math.PI * f0 / fs;
                double alpha = Math.Sin(w0) / (2 * q);
                double cosw = Math.Cos(w0);
                double a0 = 1 + alpha;
                b0 = alpha / a0; b1 = 0; b2 = -alpha / a0;
                a1 = -2 * cosw / a0; a2 = (1 - alpha) / a0;
            }
            public double Process(double x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }
    }

    // ───────────────────────────── WAV 输出 ─────────────────────────────
    public static class WavWriter
    {
        public static void Write(string path, double[][] chans, int fs)
        {
            int n = chans[0].Length;
            int ch = chans.Length;
            using (var fsOut = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fsOut))
            {
                int dataBytes = n * ch * 2;
                bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)ch);
                bw.Write(fs);
                bw.Write(fs * ch * 2);
                bw.Write((short)(ch * 2));
                bw.Write((short)16);
                bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                bw.Write(dataBytes);
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < ch; c++)
                    {
                        double v = Math.Max(-1.0, Math.Min(1.0, chans[c][i]));
                        bw.Write((short)Math.Round(v * 32767.0));
                    }
            }
        }
    }

    // ───────────────────────────── 音准自检(FFT) ─────────────────────────────
    public static class PitchCheck
    {
        public static string Run(Score sc, double[][] mix, int fs, out double worst, out int detected, out int total)
        {
            var sb = new StringBuilder();
            worst = 0; detected = 0; total = 0;
            double[] mono = new double[mix[0].Length];
            for (int i = 0; i < mono.Length; i++) mono[i] = 0.5 * (mix[0][i] + mix[1][i]);

            foreach (var v in sc.Voices)
            {
                int good = 0, cnt = 0;
                for (int e = 0; e < v.Ev.Count; e++)
                {
                    var ev = v.Ev[e];
                    if (ev.Freq <= 0) continue;
                    double dur = ev.T1 - ev.T0;
                    if (dur < 0.05) continue;
                    double steadyEnd = (e + 1 < v.Ev.Count) ? Math.Min(ev.T1, v.Ev[e + 1].CmdT0) : ev.T1;
                    int i0 = (int)((ev.T0 + 0.15 * dur) * fs);
                    int i1 = (int)(Math.Min(ev.T1 - 0.05 * dur, steadyEnd) * fs);
                    if (i1 - i0 < 512) continue;
                    if (i1 > mono.Length) i1 = mono.Length;
                    cnt++; total++;

                    int nfft = 1;
                    while (nfft < (i1 - i0) * 4) nfft <<= 1;
                    if (nfft > 1 << 18) nfft = 1 << 18;
                    var re = new double[nfft]; var im = new double[nfft];
                    int len = Math.Min(i1 - i0, nfft);
                    for (int i = 0; i < len; i++)
                    {
                        double hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, len - 1));
                        re[i] = mono[i0 + i] * hann;
                    }
                    Fft(re, im);

                    int lo = (int)Math.Floor(ev.Freq * 0.96 * nfft / fs);
                    int hi = (int)Math.Ceiling(ev.Freq * 1.04 * nfft / fs);
                    lo = Math.Max(1, lo); hi = Math.Min(nfft / 2 - 1, hi);
                    if (hi <= lo) continue;
                    double best = 0, sum = 0; int bi = lo;
                    for (int b = lo; b <= hi; b++)
                    {
                        double mag = Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                        sum += mag;
                        if (mag > best) { best = mag; bi = b; }
                    }
                    double median = sum / (hi - lo + 1);
                    double prominence = median > 1e-12 ? best / median : 0;
                    double peakHz = bi * (double)fs / nfft;
                    double cents = peakHz > 0 ? 1200.0 * Math.Log(peakHz / ev.Freq, 2) : 0;
                    if (prominence >= 2.5)
                    {
                        good++; detected++;
                        if (Math.Abs(cents) > Math.Abs(worst)) worst = cents;
                    }
                }
                if (cnt > 0)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-10} {1}叶 {2,6}-{3,-6}rpm  {4,4}/{5,-4} 检出 ({6,3}%)",
                        v.Label, v.Motor.Blades, v.Motor.MinRpm, v.Motor.MaxRpm,
                        good, cnt, (int)Math.Round(100.0 * good / cnt)));
            }
            return sb.ToString();
        }

        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double xr = re[b] * cr - im[b] * ci;
                        double xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }
    }
}
