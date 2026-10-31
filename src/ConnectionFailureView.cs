using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class ConnectionFailureView : UserControl
    {
        private readonly FailureCard card;
        private readonly OfflineMark mark;
        private readonly ConnectionStatusBadge badge;
        private readonly Label titleLabel;
        private readonly Label descriptionLabel;
        private readonly Panel detailFrame;
        private readonly Label detailLabel;
        private readonly ToolTip toolTips = new ToolTip();
        private bool layingOut;
        private string errorTitle = String.Empty;
        private string errorDescription = String.Empty;
        private string errorDetail = String.Empty;
        internal readonly ShellButton RetryButton;
        internal readonly ShellButton EditAddressButton;
        internal event EventHandler RetryRequested;
        internal event EventHandler EditAddressRequested;

        internal string ErrorTitle { get { return errorTitle; } }
        internal string ErrorDescription { get { return errorDescription; } }
        internal string ErrorDetail { get { return errorDetail; } }

        internal ConnectionFailureView()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            BackColor = NasTheme.Background;
            Font = new Font("Malgun Gothic", 9F);
            Size = new Size(640, 480);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            card = new FailureCard();
            mark = new OfflineMark();
            badge = new ConnectionStatusBadge { Font = new Font("Malgun Gothic", 9F) };
            badge.SetStatus("연결 확인", NasTheme.Warning);
            titleLabel = new Label { AutoSize = false, Font = new Font("Malgun Gothic", 20F, FontStyle.Bold), TabStop = false };
            descriptionLabel = new Label { AutoSize = false, Font = new Font("Malgun Gothic", 10F), TabStop = false };
            detailFrame = new Panel();
            detailLabel = new Label {
                AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F), Dock = DockStyle.Fill, TabStop = false
            };
            detailFrame.Controls.Add(detailLabel);
            RetryButton = new ShellButton {
                Text = "다시 연결", Glyph = ShellGlyph.Refresh, Primary = true,
                AccessibleName = "NAS에 다시 연결", TabIndex = 0
            };
            EditAddressButton = new ShellButton {
                Text = "주소 수정", Glyph = ShellGlyph.Settings,
                AccessibleName = "NAS 접속 주소 수정", TabIndex = 1
            };
            RetryButton.Click += delegate {
                EventHandler handler = RetryRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            EditAddressButton.Click += delegate {
                EventHandler handler = EditAddressRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            card.Controls.AddRange(new Control[] {
                mark, badge, titleLabel, descriptionLabel, detailFrame, RetryButton, EditAddressButton
            });
            Controls.Add(card);
            SetError("NAS에 연결할 수 없습니다", "잠시 후 다시 연결하거나 NAS 주소를 확인해 주세요.", null);
            RefreshTheme();
            ResumeLayout(true);
        }

        internal void SetError(string title, string description, string detail)
        {
            errorTitle = title ?? String.Empty;
            errorDescription = description ?? String.Empty;
            errorDetail = detail ?? String.Empty;
            titleLabel.Text = errorTitle;
            descriptionLabel.Text = errorDescription;
            detailLabel.Text = errorDetail.Replace('\r', ' ').Replace('\n', ' ');
            detailFrame.Visible = !String.IsNullOrEmpty(errorDetail);
            toolTips.SetToolTip(detailLabel, errorDetail);
            AccessibleName = errorTitle;
            AccessibleDescription = errorDescription;
            SetBusy(false);
            PerformLayout();
        }

        internal void SetBusy(bool busy)
        {
            RetryButton.Enabled = !busy;
            RetryButton.Text = busy ? "연결 중" : "다시 연결";
        }

        internal void RefreshTheme()
        {
            bool highContrast = SystemInformation.HighContrast;
            BackColor = highContrast ? SystemColors.Window : NasTheme.Background;
            Color surface = highContrast ? SystemColors.Window : NasTheme.Surface;
            Color text = highContrast ? SystemColors.WindowText : NasTheme.Text;
            Color muted = highContrast ? SystemColors.WindowText : NasTheme.Muted;
            card.BackColor = surface;
            mark.BackColor = surface;
            titleLabel.BackColor = descriptionLabel.BackColor = surface;
            titleLabel.ForeColor = text;
            descriptionLabel.ForeColor = muted;
            detailFrame.BackColor = highContrast ? SystemColors.Control : NasTheme.Blend(NasTheme.Surface, NasTheme.Background, 0.5F);
            detailLabel.BackColor = detailFrame.BackColor;
            detailLabel.ForeColor = muted;
            RetryButton.BackColor = EditAddressButton.BackColor = surface;
            RetryButton.ForeColor = EditAddressButton.ForeColor = text;
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (layingOut || card == null || titleLabel == null) return;
            layingOut = true;
            try
            {
                float scale = CurrentAutoScaleDimensions.Width / 96F;
                int outside = Px(24, scale);
                int inset = Px(30, scale);
                int width = Math.Max(1, Math.Min(Px(580, scale), ClientSize.Width - outside * 2));
                int inside = Math.Max(1, width - inset * 2);
                int y = inset;
                int icon = Px(64, scale);
                mark.Bounds = new Rectangle(inset, y, icon, icon);
                badge.Bounds = new Rectangle(inset + icon + Px(18, scale), y + Px(18, scale),
                    Math.Min(Px(112, scale), Math.Max(1, inside - icon - Px(18, scale))), Px(28, scale));
                y += icon + Px(22, scale);
                titleLabel.Bounds = new Rectangle(inset, y, inside, MeasureHeight(titleLabel, inside, scale));
                y += titleLabel.Height + Px(12, scale);
                descriptionLabel.Bounds = new Rectangle(inset, y, inside, MeasureHeight(descriptionLabel, inside, scale));
                y += descriptionLabel.Height;
                if (!String.IsNullOrEmpty(errorDetail))
                {
                    y += Px(18, scale);
                    detailFrame.Padding = new Padding(Px(11, scale), 0, Px(11, scale), 0);
                    detailFrame.Bounds = new Rectangle(inset, y, inside, Px(34, scale));
                    y += detailFrame.Height;
                }
                y += Px(24, scale);
                int buttonHeight = Px(42, scale);
                int gap = Px(12, scale);
                if (inside >= Px(300, scale))
                {
                    RetryButton.Bounds = new Rectangle(inset, y, Px(152, scale), buttonHeight);
                    EditAddressButton.Bounds = new Rectangle(RetryButton.Right + gap, y, Px(136, scale), buttonHeight);
                    y += buttonHeight;
                }
                else
                {
                    RetryButton.Bounds = new Rectangle(inset, y, inside, buttonHeight);
                    EditAddressButton.Bounds = new Rectangle(inset, RetryButton.Bottom + gap, inside, buttonHeight);
                    y = EditAddressButton.Bottom;
                }
                int height = y + inset;
                Size minimum = new Size(0, height + outside * 2);
                if (AutoScrollMinSize != minimum) AutoScrollMinSize = minimum;
                Point scroll = AutoScrollPosition;
                card.Bounds = new Rectangle(Math.Max(outside, (ClientSize.Width - width) / 2) + scroll.X,
                    Math.Max(outside, (ClientSize.Height - height) / 2) + scroll.Y, width, height);
            }
            finally { layingOut = false; }
        }

        private static int MeasureHeight(Label label, int width, float scale)
        {
            return Math.Max(label.Font.Height, TextRenderer.MeasureText(label.Text, label.Font,
                new Size(width, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl |
                TextFormatFlags.NoPadding).Height) + Px(4, scale);
        }

        private static int Px(int value, float scale) { return Math.Max(1, (int)Math.Round(value * scale)); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) toolTips.Dispose();
            base.Dispose(disposing);
        }

        private sealed class FailureCard : Panel
        {
            internal FailureCard()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent == null ? NasTheme.Background : Parent.BackColor);
                if (Width < 2 || Height < 2) return;
                float scale = e.Graphics.DpiX / 96F;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = NasTheme.RoundRect(new RectangleF(scale / 2F, scale / 2F,
                    Width - scale, Height - scale), 14F * scale))
                using (SolidBrush fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Window : NasTheme.Surface))
                using (Pen border = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Border, scale))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
            }
        }

        private sealed class OfflineMark : Control
        {
            internal OfflineMark()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                AccessibleRole = AccessibleRole.Graphic;
                AccessibleName = "NAS 연결 확인";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics graphics = e.Graphics;
                graphics.Clear(Parent == null ? NasTheme.Surface : Parent.BackColor);
                if (Width < 2 || Height < 2) return;
                GraphicsState saved = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(Width / 64F, Height / 64F);
                Color ink = SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Warning;
                Color fill = SystemInformation.HighContrast ? SystemColors.Window : NasTheme.Blend(NasTheme.Surface, ink, 0.10F);
                using (SolidBrush circle = new SolidBrush(fill)) graphics.FillEllipse(circle, 0, 0, 64, 64);
                using (Pen rack = new Pen(ink, 2F))
                {
                    rack.StartCap = rack.EndCap = LineCap.Round;
                    for (int index = 0; index < 2; index++)
                    {
                        float top = 18 + index * 16;
                        using (GraphicsPath bay = NasTheme.RoundRect(new RectangleF(15, top, 34, 12), 3))
                            graphics.DrawPath(rack, bay);
                        graphics.DrawEllipse(rack, 21, top + 4, 3, 3);
                        graphics.DrawLine(rack, 32, top + 6, 42, top + 6);
                    }
                }
                using (Pen gap = new Pen(fill, 6F))
                using (Pen slash = new Pen(ink, 2.3F))
                {
                    gap.StartCap = gap.EndCap = slash.StartCap = slash.EndCap = LineCap.Round;
                    graphics.DrawLine(gap, 17, 49, 47, 14);
                    graphics.DrawLine(slash, 17, 49, 47, 14);
                }
                graphics.Restore(saved);
            }
        }
    }
}
