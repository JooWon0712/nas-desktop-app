using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class SecurityDialog : Form
    {
        internal readonly CheckBox ExternalContent;
        internal readonly ShellButton ClearData;
        private readonly Label message;

        internal SecurityDialog(bool allowExternal, Action<bool> savePolicy, Func<Task> clearData)
        {
            Text = "보안 및 개인정보";
            Font = new Font("Malgun Gothic", 9F);
            BackColor = NasTheme.Background;
            ForeColor = NasTheme.Text;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(24);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
            foreach (int height in new[] { 32, 56, 36, 88, 44, 48, 40 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "현재 NAS의 보안 설정", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
            layout.Controls.Add(new Label { Text = "로그인 쿠키와 웹 저장소는 NAS별로 분리합니다.\n이 창의 설정과 삭제는 현재 NAS에만 적용됩니다.", Dock = DockStyle.Fill, ForeColor = NasTheme.Muted }, 0, 1);
            ExternalContent = new CheckBox { Text = "이 NAS의 외부 웹 콘텐츠 허용", Checked = allowExternal, AutoSize = false, Dock = DockStyle.Fill };
            layout.Controls.Add(ExternalContent, 0, 2);
            layout.Controls.Add(new Label { Text = "기본값은 차단입니다. 외부 로그인·패키지가\n작동하지 않을 때만 허용하세요.\nQuickConnect 연결 탐색의 HTTPS 통신은 예외입니다.", Dock = DockStyle.Fill, ForeColor = NasTheme.Muted }, 0, 3);
            ClearData = new ShellButton { Text = "현재 NAS 로그인 데이터 삭제", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            layout.Controls.Add(ClearData, 0, 4);
            message = new Label { Text = "삭제하면 쿠키·캐시·웹 저장소·권한을 지웁니다.", Dock = DockStyle.Fill, ForeColor = NasTheme.Muted };
            layout.Controls.Add(message, 0, 5);
            ShellButton close = new ShellButton { Text = "닫기", Dock = DockStyle.Right, Width = 90, DialogResult = DialogResult.Cancel };
            layout.Controls.Add(close, 0, 6);
            Controls.Add(layout);
            CancelButton = close;
            bool changing = false;
            ExternalContent.CheckedChanged += delegate
            {
                if (changing) return;
                try { savePolicy(ExternalContent.Checked); message.Text = "외부 콘텐츠 설정을 저장했습니다. 새로 고침하면 적용됩니다."; }
                catch (Exception error)
                {
                    changing = true;
                    ExternalContent.Checked = !ExternalContent.Checked;
                    changing = false;
                    message.Text = "설정을 저장하지 못했습니다: " + error.Message;
                }
            };
            ClearData.Click += async delegate
            {
                if (MessageBox.Show(this, "현재 NAS의 로그인 데이터를 삭제할까요?\n다른 NAS의 로그인과 주소 기록은 유지합니다. 삭제 후 NAS 홈에서 다시 연결하세요.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                ClearData.Enabled = ExternalContent.Enabled = false;
                message.Text = "로그인 데이터를 삭제하고 있습니다…";
                try
                {
                    await clearData();
                    if (!IsDisposed) message.Text = "삭제했습니다. NAS에 다시 로그인해 주세요.";
                }
                catch (Exception error) { if (!IsDisposed) message.Text = "삭제하지 못했습니다: " + error.Message; }
                finally { if (!IsDisposed) ClearData.Enabled = ExternalContent.Enabled = true; }
            };
            NasTheme.ApplyWindowChrome(this);
        }
    }
}
