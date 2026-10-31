using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class ConnectionStatusBadge : Control
    {
        private enum ConnectionState { Waiting, Connected, Attention }
        private ConnectionState state;

        internal ConnectionStatusBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(112, 30);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
            SetStatus("연결 준비", NasTheme.Muted);
        }

        internal void SetStatus(string text, Color color)
        {
            // Store meaning rather than a palette color: an open window can change themes.
            string value = text ?? String.Empty;
            state = value == "연결됨" ? ConnectionState.Connected :
                value == "연결 확인" || value.IndexOf("오류", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("실패", StringComparison.Ordinal) >= 0 ? ConnectionState.Attention :
                ConnectionState.Waiting;
            Text = value;
            AccessibleName = "NAS 연결 상태: " + value;
            AccessibleDescription = state == ConnectionState.Connected ?
                "NAS 화면을 사용할 수 있습니다." : state == ConnectionState.Attention ?
                "NAS 연결 상태를 확인해 주세요." : "NAS 연결을 준비하고 있습니다.";
            Invalidate();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;

            Graphics graphics = e.Graphics;
            float scale = graphics.DpiX / 96F;
            bool highContrast = SystemInformation.HighContrast;
            Color background = Parent == null ? NasTheme.Background : Parent.BackColor;
            Color ink = highContrast ? SystemColors.WindowText : state == ConnectionState.Connected ?
                NasTheme.Accent : state == ConnectionState.Attention ?
                NasTheme.Warning : NasTheme.Muted;
            Color fill = highContrast ? SystemColors.Window : Blend(background, ink, 0.09F);
            Color border = highContrast ? SystemColors.WindowText : Blend(background, ink, 0.25F);
            RectangleF bounds = new RectangleF(scale / 2F, scale / 2F,
                Width - scale, Height - scale);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath outline = NasTheme.RoundRect(bounds, bounds.Height / 2F))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border, scale))
            {
                graphics.FillPath(brush, outline);
                graphics.DrawPath(pen, outline);
            }

            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            int iconSize = Math.Max(10, (int)Math.Round(13F * scale));
            int gap = (int)Math.Round(7F * scale);
            int inset = (int)Math.Round(12F * scale);
            int available = Math.Max(0, ClientSize.Width - inset * 2 - iconSize - gap);
            int textWidth = Math.Min(available,
                TextRenderer.MeasureText(graphics, Text, Font, new Size(Int32.MaxValue, Int32.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
            int contentWidth = iconSize + gap + textWidth;
            int left = Math.Max(inset, (ClientSize.Width - contentWidth) / 2);
            RectangleF icon = new RectangleF(left, (Height - iconSize) / 2F, iconSize, iconSize);
            DrawStatusIcon(graphics, icon, ink, scale);
            Rectangle textBounds = new Rectangle(left + iconSize + gap, 0,
                Math.Max(0, Width - inset - left - iconSize - gap), Height);
            TextRenderer.DrawText(graphics, Text, Font, textBounds,
                highContrast ? SystemColors.WindowText : NasTheme.Text, flags);
        }

        private void DrawStatusIcon(Graphics graphics, RectangleF bounds, Color color, float scale)
        {
            using (Pen pen = new Pen(color, 1.4F * scale))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                RectangleF ring = RectangleF.Inflate(bounds, -scale, -scale);
                graphics.DrawEllipse(pen, ring);
                if (state == ConnectionState.Connected)
                {
                    graphics.DrawLines(pen, new PointF[] {
                        At(bounds, 0.29F, 0.50F), At(bounds, 0.45F, 0.66F),
                        At(bounds, 0.73F, 0.36F) });
                }
                else if (state == ConnectionState.Attention)
                {
                    graphics.DrawLine(pen, At(bounds, 0.5F, 0.28F), At(bounds, 0.5F, 0.53F));
                    using (SolidBrush brush = new SolidBrush(color))
                        graphics.FillEllipse(brush, bounds.X + bounds.Width * 0.44F,
                            bounds.Y + bounds.Height * 0.68F, 1.8F * scale, 1.8F * scale);
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(color))
                        graphics.FillEllipse(brush, bounds.X + bounds.Width * 0.39F,
                            bounds.Y + bounds.Height * 0.39F, bounds.Width * 0.22F, bounds.Height * 0.22F);
                }
            }
        }

        private static PointF At(RectangleF bounds, float x, float y)
        {
            return new PointF(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);
        }

        private static Color Blend(Color background, Color foreground, float amount)
        {
            return Color.FromArgb(
                (int)(background.R + (foreground.R - background.R) * amount),
                (int)(background.G + (foreground.G - background.G) * amount),
                (int)(background.B + (foreground.B - background.B) * amount));
        }
    }
}
