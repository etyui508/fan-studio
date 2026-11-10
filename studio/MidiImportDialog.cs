// MidiImportDialog.cs —— MIDI 自动改编参数

using System;
using System.Drawing;
using System.Windows.Forms;

namespace FanStudio
{
    public class MidiImportDialog : Form
    {
        DarkNumeric _voices;
        DarkNumeric _duration;
        DarkNumeric _bpm;

        public int VoiceCount { get { return (int)_voices.Value; } }
        public double Duration { get { return (double)_duration.Value; } }
        public double Bpm { get { return (double)_bpm.Value; } }

        public MidiImportDialog(int voices, double bpm)
        {
            Text = I18n.L("导入 MIDI", "Import MIDI");
            ClientSize = new Size(390, 245);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = StudioTheme.Panel;
            ForeColor = StudioTheme.Text;
            Font = StudioTheme.Ui;

            var title = new Label();
            title.Text = I18n.L("自动改编为电机乐器", "Arrange MIDI for motor instruments");
            title.Font = StudioTheme.Title;
            title.ForeColor = StudioTheme.Text;
            title.AutoSize = true;
            title.Location = new Point(18, 16);
            Controls.Add(title);

            var hint = new Label();
            hint.Text = I18n.L(
                "分层抽取旋律与低音，按每台电机的惯性重采样。\n结果会替换当前项目，但仍可 Ctrl+Z 撤销。",
                "Extract melody and bass layers, then resample around each motor's inertia.\nThe result replaces the current project and can be restored with Ctrl+Z.");
            hint.ForeColor = StudioTheme.Muted;
            hint.Font = StudioTheme.UiSmall;
            hint.AutoSize = false;
            hint.Bounds = new Rectangle(18, 45, 352, 42);
            Controls.Add(hint);

            _voices = AddNumber(I18n.L("声部 / 电机数", "Voices / motors"), 18, 96, voices, 1, 12, 0, 1);
            _duration = AddNumber(I18n.L("最长时长（秒）", "Max duration (s)"), 18, 132, 45, 1, 300, 1, 5);
            _bpm = AddNumber(I18n.L("工程 BPM", "Project BPM"), 18, 168, bpm, 20, 400, 2, 1);

            var cancel = new StudioButton();
            cancel.Text = I18n.L("取消", "Cancel");
            cancel.Bounds = new Rectangle(196, 205, 78, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            var ok = new StudioButton();
            ok.Text = I18n.L("开始改编", "Start arranging");
            ok.Accent = true;
            ok.Bounds = new Rectangle(282, 205, 92, 30);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        DarkNumeric AddNumber(string label, int x, int y, double value,
            decimal min, decimal max, int decimals, decimal step)
        {
            var l = new Label();
            l.Text = label;
            l.ForeColor = StudioTheme.Muted;
            l.Bounds = new Rectangle(x, y, 170, 24);
            l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l);

            var n = new DarkNumeric();
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Increment = step;
            n.Value = Math.Max(min, Math.Min(max, (decimal)value));
            n.Bounds = new Rectangle(x + 180, y, 170, 24);
            Controls.Add(n);
            return n;
        }
    }
}
