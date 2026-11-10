// Studio.cs —— fan_studio.exe 入口与新编辑器启动逻辑

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FanStudio;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppSettings.Load();
        Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            MessageBox.Show(I18n.L("程序遇到一个错误，详情已写入 crash.log：\n\n",
                "The application encountered an error. Details were written to crash.log:\n\n") + e.Exception.Message,
                "fan_studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception);
        };

        if (args.Length > 0 && args[0] == "--selftest")
        {
            RunSelfTest();
            return;
        }
        if (args.Length > 0 && args[0] == "--scorecheck")
        {
            RunScoreCheck();
            return;
        }
        if (args.Length > 0 && args[0] == "--show-legal")
        {
            while (true)
            {
                using (var legal = new LegalDialog(false))
                {
                    DialogResult result = legal.ShowDialog();
                    if (result != DialogResult.Retry) break;
                }
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--legal-snapshot")
        {
            var legal = new LegalDialog(true);
            string path = args.Length > 1 ? args[1] :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legal_snapshot.png");
            legal.Shown += delegate
            {
                Application.DoEvents();
                legal.SaveSnapshot(path);
                legal.Close();
            };
            Application.Run(legal);
            return;
        }

        bool automatedLaunch = args.Length > 0 &&
            (args[0] == "--uitest" || args[0] == "--snapshot");
        if (!automatedLaunch && !HasAcceptedLegal())
        {
            while (true)
            {
                using (var legal = new LegalDialog(true))
                {
                    DialogResult result = legal.ShowDialog();
                    if (result == DialogResult.Retry) continue;
                    if (result != DialogResult.OK) return;
                    break;
                }
            }
            SaveLegalAcceptance();
        }

        string scorePath = null;
        if (args.Length > 0 && (args[0] == "--uitest" || args[0] == "--snapshot"))
        {
            if (args.Length > 2) scorePath = args[2];
            else if (args.Length > 1 && File.Exists(args[1]) &&
                args[1].ToLowerInvariant().EndsWith(".fscore")) scorePath = args[1];
            var form = new MainForm(LoadCatalog(AppDomain.CurrentDomain.BaseDirectory), scorePath);
            form.Shown += delegate
            {
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 450;
                timer.Tick += delegate
                {
                    timer.Stop();
                    if (args[0] == "--snapshot")
                    {
                        string path = args.Length > 1 && !args[1].StartsWith("--") &&
                            args[1].ToLowerInvariant().EndsWith(".png")
                            ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "studio_snapshot.png");
                        try
                        {
                            form.SaveSnapshot(path);
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snapshot.log"),
                                "OK " + path, Encoding.UTF8);
                        }
                        catch (Exception ex)
                        {
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snapshot.log"),
                                "FAIL " + ex, Encoding.UTF8);
                        }
                        form.Close();
                    }
                    else form.RunUiTest();
                };
                timer.Start();
            };
            Application.Run(form);
            return;
        }

        try
        {
            Application.Run(new MainForm(LoadCatalog(AppDomain.CurrentDomain.BaseDirectory),
                args.Length > 0 ? args[0] : null));
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show(I18n.L("启动失败：\n\n", "Startup failed:\n\n") + ex, "fan_studio",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void LogCrash(Exception ex)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine(ex == null ? "(无异常对象)" : ex.ToString());
            sb.AppendLine();
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    static string LegalMarkerPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fan_studio");
        return Path.Combine(dir, "legal-v" + LegalDocuments.Version + ".accepted");
    }

    static bool HasAcceptedLegal()
    {
        try { return File.Exists(LegalMarkerPath()); }
        catch { return false; }
    }

    static void SaveLegalAcceptance()
    {
        try
        {
            string path = LegalMarkerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "fan_studio legal accepted\nversion=" + LegalDocuments.Version +
                "\ndate=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n",
                Encoding.UTF8);
        }
        catch { }
    }

    static void RunSelfTest()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        var log = new StringBuilder();
        try
        {
            List<Motor> cat = LoadCatalog(dir);
            log.AppendLine("电机库: " + cat.Count);
            EditorDocument doc = EditorDocument.CreateDemo(cat);
            Score sc = doc.BuildScore();
            int fs = 48000;
            double[][] mix = Synth.Render(sc, fs, 0.85, 0.14);
            string wav = Path.Combine(dir, "gui_selftest.wav");
            WavWriter.Write(wav, mix, fs);
            double worst;
            int det, tot;
            string report = PitchCheck.Run(sc, mix, fs, out worst, out det, out tot);
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "声部 {0}, 音符 {1}, 时长 {2:F2}s, 检出 {3}/{4}, 最差 {5:F1} 音分",
                sc.Voices.Count, CountNotes(sc), mix[0].Length / (double)fs, det, tot, worst));
            log.Append(report);
            log.AppendLine("文档往返: " + EditorDocument.Load(doc.Serialize(), cat).Voices.Count + " 声部");
        }
        catch (Exception ex) { log.AppendLine("失败: " + ex); }
        File.WriteAllText(Path.Combine(dir, "gui_selftest.log"), log.ToString(), Encoding.UTF8);
    }

    static int CountNotes(Score sc)
    {
        int n = 0;
        foreach (var v in sc.Voices) n += v.Raw.Count;
        return n;
    }

    static void RunScoreCheck()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string scores = Path.GetFullPath(Path.Combine(dir, "..", "scores"));
        var log = new StringBuilder();
        int failed = 0;
        try
        {
            var cat = LoadCatalog(dir);
            string[] files = Directory.GetFiles(scores, "*.fscore");
            Array.Sort(files);
            foreach (string file in files)
            {
                try
                {
                    EditorDocument doc = EditorDocument.Load(File.ReadAllText(file, Encoding.UTF8), cat);
                    Score score = doc.BuildScore();
                    int notes = 0;
                    foreach (var v in doc.Voices) notes += v.Notes.Count;
                    EditorDocument round = EditorDocument.Load(doc.Serialize(), cat);
                    int roundNotes = 0;
                    foreach (var v in round.Voices) roundNotes += v.Notes.Count;
                    bool ok = doc.Voices.Count > 0 && notes > 0 && round.Voices.Count == doc.Voices.Count &&
                        roundNotes == notes;
                    if (!ok) failed++;
                    log.AppendLine((ok ? "OK  " : "FAIL") + "  " + Path.GetFileName(file) +
                        " · 声部 " + doc.Voices.Count + " · 音符 " + notes +
                        " · 往返 " + round.Voices.Count + "/" + roundNotes +
                        " · 风险 " + doc.CountWarnings());
                }
                catch (Exception ex)
                {
                    failed++;
                    log.AppendLine("FAIL  " + Path.GetFileName(file) + " · " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            failed++;
            log.AppendLine("FAIL  扫描 scores 失败 · " + ex);
        }
        log.AppendLine(failed == 0 ? "RESULT: OK 所有 .fscore 均可载入并往返" :
            "RESULT: " + failed + " 个文件失败");
        File.WriteAllText(Path.Combine(dir, "scorecheck.log"), log.ToString(), Encoding.UTF8);
    }

    public static List<Motor> LoadCatalog(string dir)
    {
        string p = Path.Combine(dir, "motors.txt");
        if (!File.Exists(p)) p = Path.Combine(dir, "..", "motors.txt");
        var list = MotorCatalog.Load(p);
        if (list.Count == 0) list.Add(MotorCatalog.Fallback());
        return list;
    }
}
