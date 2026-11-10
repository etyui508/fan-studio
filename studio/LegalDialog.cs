// LegalDialog.cs —— 首次启动同意页与可重复打开的内置帮助

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FanStudio
{
    public class LegalDialog : Form
    {
        readonly bool _firstRun;
        readonly RichTextBox _text;
        readonly StudioButton _tabGuide;
        readonly StudioButton _tabTerms;
        readonly StudioButton _tabPrivacy;
        readonly StudioButton _accept;
        readonly StudioButton _ok;
        bool _accepted;

        public LegalDialog(bool firstRun)
        {
            _firstRun = firstRun;
            Text = firstRun ? "fan_studio — 首次使用确认" : "fan_studio — 使用说明与法律条款";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ClientSize = new Size(760, 650);
            MinimumSize = new Size(620, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = StudioTheme.Canvas;
            ForeColor = StudioTheme.Text;
            Font = StudioTheme.Ui;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 82;
            header.BackColor = StudioTheme.Panel;
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, firstRun ? "欢迎使用 fan_studio" : "使用说明与法律条款",
                    new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                    new Rectangle(24, 12, header.Width - 48, 30), StudioTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics,
                    firstRun ? "开始编辑前，请阅读并同意用户协议与隐私政策。" :
                    "查看操作说明、用户协议与隐私政策。",
                    StudioTheme.UiSmall, new Rectangle(24, 45, header.Width - 48, 24),
                    StudioTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using (var pen = new Pen(StudioTheme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            Controls.Add(header);

            var tabs = new Panel();
            tabs.Dock = DockStyle.Top;
            tabs.Height = 48;
            tabs.BackColor = StudioTheme.Panel;
            tabs.Padding = new Padding(20, 8, 20, 7);
            Controls.Add(tabs);

            _tabGuide = TabButton("使用说明", 112);
            _tabTerms = TabButton("用户协议", 98);
            _tabPrivacy = TabButton("隐私政策", 98);
            _tabGuide.Left = 20;
            _tabTerms.Left = 138;
            _tabPrivacy.Left = 242;
            _tabGuide.Click += delegate { ShowDoc(0); };
            _tabTerms.Click += delegate { ShowDoc(1); };
            _tabPrivacy.Click += delegate { ShowDoc(2); };
            tabs.Controls.Add(_tabGuide);
            tabs.Controls.Add(_tabTerms);
            tabs.Controls.Add(_tabPrivacy);

            var footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = firstRun ? 102 : 66;
            footer.BackColor = StudioTheme.Panel;
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(StudioTheme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            Controls.Add(footer);

            _text = new RichTextBox();
            _text.Dock = DockStyle.Fill;
            _text.ReadOnly = true;
            _text.BorderStyle = BorderStyle.None;
            _text.BackColor = StudioTheme.Canvas;
            _text.ForeColor = StudioTheme.Text;
            _text.Font = new Font("Microsoft YaHei UI", 10F);
            _text.WordWrap = true;
            _text.DetectUrls = false;
            _text.ScrollBars = RichTextBoxScrollBars.None;
            _text.Margin = new Padding(0);
            _text.SelectionFont = _text.Font;
            Controls.Add(_text);
            _text.BringToFront();

            if (firstRun)
            {
                _accept = new StudioButton();
                _accept.Compact = true;
                _accept.Text = "☐  我已阅读并同意《用户协议》和《隐私政策》";
                _accept.Bounds = new Rectangle(22, 15, 430, 32);
                _accept.Click += delegate
                {
                    _accepted = !_accepted;
                    _accept.Text = (_accepted ? "☑  " : "☐  ") +
                        "我已阅读并同意《用户协议》和《隐私政策》";
                    _accept.Accent = _accepted;
                    _ok.Enabled = _accepted;
                };
                footer.Controls.Add(_accept);

                var quit = new StudioButton();
                quit.Text = "退出";
                quit.Danger = true;
                quit.Bounds = new Rectangle(520, 54, 96, 32);
                quit.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                quit.DialogResult = DialogResult.Cancel;
                footer.Controls.Add(quit);

                _ok = new StudioButton();
                _ok.Text = "同意并继续";
                _ok.Accent = true;
                _ok.Enabled = false;
                _ok.Bounds = new Rectangle(624, 54, 114, 32);
                _ok.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                _ok.DialogResult = DialogResult.OK;
                footer.Controls.Add(_ok);
                AcceptButton = _ok;
                CancelButton = quit;

                var hint = new Label();
                hint.Text = "滚轮或 PageDown 阅读全文 · 同意记录仅保存在本机";
                hint.ForeColor = StudioTheme.Dim;
                hint.Font = StudioTheme.UiSmall;
                hint.Bounds = new Rectangle(468, 18, 270, 24);
                hint.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                hint.TextAlign = ContentAlignment.MiddleRight;
                footer.Controls.Add(hint);
            }
            else
            {
                _ok = new StudioButton();
                _ok.Text = "关闭";
                _ok.Bounds = new Rectangle(638, 17, 100, 32);
                _ok.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                _ok.DialogResult = DialogResult.OK;
                footer.Controls.Add(_ok);
                AcceptButton = _ok;
                CancelButton = _ok;
                var hint = new Label();
                hint.Text = "滚轮或 PageDown 阅读全文";
                hint.ForeColor = StudioTheme.Dim;
                hint.Font = StudioTheme.UiSmall;
                hint.Bounds = new Rectangle(22, 18, 300, 24);
                hint.TextAlign = ContentAlignment.MiddleLeft;
                footer.Controls.Add(hint);
            }

            _text.BringToFront();
            ShowDoc(firstRun ? 1 : 0);
        }

        StudioButton TabButton(string text, int width)
        {
            var b = new StudioButton();
            b.Text = text;
            b.Compact = true;
            b.Width = width;
            b.Height = 30;
            b.Top = 8;
            b.Margin = new Padding(0);
            return b;
        }

        void ShowDoc(int which)
        {
            _text.Text = which == 0 ? LegalDocuments.Guide :
                (which == 1 ? LegalDocuments.Terms : LegalDocuments.Privacy);
            _text.SelectionStart = 0;
            _text.SelectionLength = 0;
            SetActive(_tabGuide, which == 0);
            SetActive(_tabTerms, which == 1);
            SetActive(_tabPrivacy, which == 2);
        }

        void SetActive(StudioButton button, bool active)
        {
            button.Accent = active;
            button.Invalidate();
        }

        public void SaveSnapshot(string path)
        {
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                LegalWindow.Darken(Handle, ref dark);
            }
            catch { }
        }
    }

    static class LegalWindow
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void Darken(IntPtr handle, ref int value)
        {
            DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
            DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
        }
    }
}
