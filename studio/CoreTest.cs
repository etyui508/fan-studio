// CoreTest.cs —— 控制台测试台: 不开窗口, 直接把引擎跑一遍并用 FFT 自检。
// 编译: csc /target:exe /out:fan_core_test.exe FanCore.cs CoreTest.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FanStudio;

static class CoreTest
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        CultureInfo ci = CultureInfo.InvariantCulture;

        var cat = MotorCatalog.Load(Path.Combine(dir, "motors.txt"));
        Console.WriteLine("电机库: " + cat.Count + " 个型号");
        if (cat.Count == 0)
        {
            cat.Add(MotorCatalog.Fallback());
            Console.WriteLine("  (motors.txt 缺失, 用内置兜底型号)");
        }

        string scorePath = args.Length > 0 ? args[0] : Path.Combine(dir, "selftest.fscore");
        if (!File.Exists(scorePath)) { Console.WriteLine("找不到谱子: " + scorePath); return 2; }

        var sc = ScoreParser.Parse(File.ReadAllText(scorePath, Encoding.UTF8), cat, true);
        int notes = 0;
        foreach (var v in sc.Voices) notes += v.Ev.Count;
        Console.WriteLine(string.Format(ci, "谱子: {0} 声部, bpm={1:g}", sc.Voices.Count, sc.Bpm));

        Planner.Build(sc);
        notes = 0;
        foreach (var v in sc.Voices) notes += v.Ev.Count;
        Console.WriteLine("排程后音符数: " + notes);

        int fs = 48000;
        var sw = Stopwatch.StartNew();
        double[][] mix = Synth.Render(sc, fs, 0.85, 0.14);
        sw.Stop();
        double secs = mix[0].Length / (double)fs;
        Console.WriteLine(string.Format(ci, "渲染完成: {0:F2}s 音频, 耗时 {1:F2}s, {2} 声道",
            secs, sw.Elapsed.TotalSeconds, mix.Length));

        string wav = Path.Combine(dir, "selftest.wav");
        WavWriter.Write(wav, mix, fs);
        var fi = new FileInfo(wav);
        Console.WriteLine(string.Format(ci, "已写出 {0} ({1:F1} KB)", Path.GetFileName(wav), fi.Length / 1024.0));

        double worst; int det, tot;
        string report = PitchCheck.Run(sc, mix, fs, out worst, out det, out tot);
        Console.WriteLine("音准自检:");
        Console.Write(report);
        Console.WriteLine(string.Format(ci, "  检出 {0}/{1} 个音, 最差 {2:+0.0;-0.0} 音分", det, tot, worst));

        // —— 诊断: 直接看第一个音的实际频谱 ——
        if (args.Length < 2 && sc.Voices.Count > 0 && sc.Voices[0].Ev.Count > 0)
        {
            var v0 = sc.Voices[0];
            var e0 = v0.Ev[0];
            int a0 = (int)(e0.T0 * fs);
            int a1 = Math.Min((int)(e0.T1 * fs), mix[0].Length);
            double rms = 0;
            for (int i = a0; i < a1; i++) rms += mix[0][i] * mix[0][i];
            rms = Math.Sqrt(rms / Math.Max(1, a1 - a0));

            int n = 1; while (n < 32768) n <<= 1;
            var re = new double[n]; var im = new double[n];
            int len = Math.Min(a1 - a0, n);
            for (int i = 0; i < len; i++)
                re[i] = mix[0][a0 + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, len - 1)));
            PitchCheck.Fft(re, im);

            int loB = Math.Max(2, (int)(80.0 * n / fs));
            int hiB = Math.Min(n / 2 - 1, (int)(4500.0 * n / fs));
            double best = 0; int bi = loB;
            for (int b = loB; b <= hiB; b++)
            {
                double mag = Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                if (mag > best) { best = mag; bi = b; }
            }
            int eb = (int)Math.Round(e0.Freq * n / fs);
            double expMag = Math.Sqrt(re[eb] * re[eb] + im[eb] * im[eb]);
            Console.WriteLine(string.Format(ci,
                "诊断 {0}: 期望 {1:F1}Hz 幅度={2:F1} | 实测主峰 {3:F1}Hz 幅度={4:F1} | 片段RMS={5:F4}",
                ScoreParser.MidiToName(e0.Midi), e0.Freq, expMag, bi * (double)fs / n, best, rms));
        }

        if (sc.Warnings.Count > 0)
        {
            Console.WriteLine("警告 " + sc.Warnings.Count + " 条:");
            for (int i = 0; i < Math.Min(6, sc.Warnings.Count); i++)
                Console.WriteLine("  " + sc.Warnings[i]);
        }
        return 0;
    }
}
