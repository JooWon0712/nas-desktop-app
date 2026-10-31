using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class AddressLockButton : Button
    {
        private bool editing;
        private bool hover;
        private bool pressed;

        internal bool Editing
        {
            get { return editing; }
            set
            {
                if (editing == value) return;
                editing = value;
                UpdateState();
            }
        }

        internal AddressLockButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Size = new Size(126, 40);
            Font = new Font("Malgun Gothic", 9F);
            BackColor = NasTheme.Surface;
            ForeColor = NasTheme.Text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            UpdateState();
        }

        private void UpdateState()
        {
            Text = editing ? "취소" : "주소 변경";
            AccessibleName = editing ? "NAS 주소 편집 취소" : "NAS 주소 변경";
            AccessibleDescription = editing
                ? "NAS 주소를 편집하고 있습니다. 클릭하면 변경을 취소하고 주소를 잠급니다."
                : "주소 입력줄이 숨겨져 있습니다. 클릭하면 주소를 편집합니다.";
            Invalidate();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) pressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            Graphics graphics = e.Graphics;
            float scale = graphics.DpiX / 96F;
            bool highContrast = SystemInformation.HighContrast;
            graphics.Clear(highContrast ? SystemColors.Control : Parent == null ? NasTheme.Background : Parent.BackColor);

            Color fill = editing ? NasTheme.Blend(NasTheme.Surface, NasTheme.Accent, 0.10F) : NasTheme.Surface;
            Color line = editing ? NasTheme.Blend(NasTheme.Border, NasTheme.Accent, 0.65F) : NasTheme.Border;
            Color ink = editing ? NasTheme.Accent : NasTheme.Muted;
            Color text = editing ? NasTheme.Text : NasTheme.Muted;
            if (Enabled && (hover || pressed))
                fill = editing ? NasTheme.Blend(NasTheme.Surface, NasTheme.Accent, pressed ? 0.22F : 0.16F)
                    : pressed ? NasTheme.Pressed : NasTheme.Hover;
            if (!Enabled) { fill = NasTheme.Surface; line = NasTheme.Border; ink = text = NasTheme.Muted; }
            if (highContrast)
            {
                bool emphasized = Enabled && (editing || hover || pressed);
                fill = emphasized ? SystemColors.Highlight : SystemColors.Control;
                ink = text = !Enabled ? SystemColors.GrayText : emphasized ? SystemColors.HighlightText : SystemColors.ControlText;
                line = !Enabled ? SystemColors.GrayText : SystemColors.ControlText;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = NasTheme.RoundRect(new RectangleF(scale / 2F, scale / 2F,
                Width - scale, Height - scale), 7F * scale))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen border = new Pen(line, scale))
            {
                graphics.FillPath(brush, shape);
                graphics.DrawPath(border, shape);
            }

            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int inset = (int)Math.Round(10F * scale);
            int iconSize = (int)Math.Round(16F * scale);
            int gap = (int)Math.Round(8F * scale);
            int available = Math.Max(0, Width - inset * 2 - iconSize - gap);
            int textWidth = Math.Min(available, TextRenderer.MeasureText(graphics, Text, Font,
                new Size(Int32.MaxValue, Int32.MaxValue), flags).Width);
            int left = Math.Max(inset, (Width - iconSize - gap - textWidth) / 2);
            DrawLock(graphics, new RectangleF(left, (Height - iconSize) / 2F, iconSize, iconSize), ink);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(left + iconSize + gap, 0,
                Math.Max(0, Width - inset - left - iconSize - gap), Height), text,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle,
                    -(int)Math.Round(4F * scale), -(int)Math.Round(4F * scale)), text, fill);
        }

        private void DrawLock(Graphics graphics, RectangleF bounds, Color color)
        {
            GraphicsState saved = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 16F, bounds.Height / 16F);
            using (Pen pen = new Pen(color, 1.5F))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                using (GraphicsPath body = NasTheme.RoundRect(new RectangleF(2.75F, 7.0F, 10.5F, 7.8F), 1.7F))
                    graphics.DrawPath(pen, body);
                if (editing)
                {
                    graphics.DrawArc(pen, 7.0F, 0.8F, 7.4F, 9.0F, 180, 180);
                    graphics.DrawLine(pen, 7.0F, 5.3F, 7.0F, 7.0F);
                    graphics.DrawLine(pen, 14.4F, 5.3F, 14.4F, 6.3F);
                }
                else
                {
                    graphics.DrawArc(pen, 4.8F, 0.8F, 6.4F, 9.0F, 180, 180);
                    graphics.DrawLine(pen, 4.8F, 5.3F, 4.8F, 7.0F);
                    graphics.DrawLine(pen, 11.2F, 5.3F, 11.2F, 7.0F);
                }
                using (SolidBrush key = new SolidBrush(color))
                    graphics.FillEllipse(key, 7.0F, 9.5F, 2.0F, 2.0F);
                graphics.DrawLine(pen, 8.0F, 11.0F, 8.0F, 12.5F);
            }
            graphics.Restore(saved);
        }
    }
}
