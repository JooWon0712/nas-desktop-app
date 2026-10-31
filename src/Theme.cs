using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NasDesk
{
    internal static class NasTheme
    {
        internal static readonly Color Background = Color.FromArgb(17, 22, 30);
        internal static readonly Color Surface = Color.FromArgb(25, 33, 44);
        internal static readonly Color Border = Color.FromArgb(42, 53, 68);
        internal static readonly Color Text = Color.FromArgb(233, 238, 246);
        internal static readonly Color Muted = Color.FromArgb(142, 155, 170);
        internal static readonly Color Accent = Color.FromArgb(89, 214, 176);
        internal static readonly Color Hover = Color.FromArgb(37, 49, 64);
        internal static readonly Color Pressed = Color.FromArgb(45, 60, 76);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icon);

        internal static void ApplyWindowChrome(Form form)
        {
            if (form == null) return;
            form.HandleCreated += delegate { if (!form.IsDisposed) ApplyChrome(form.Handle); };
            if (form.IsHandleCreated) ApplyChrome(form.Handle);
        }

        private static void ApplyChrome(IntPtr handle)
        {
            if (SystemInformation.HighContrast) return;
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
                int caption = ColorRef(Background);
                int text = ColorRef(Text);
                int border = ColorRef(Border);
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
                DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static int ColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        internal static GraphicsPath RoundRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static Icon CreateIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    DrawMark(graphics, new RectangleF(0, 0, 32, 32));
                }
                IntPtr native = bitmap.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(native)) return (Icon)borrowed.Clone();
                }
                finally { DestroyIcon(native); }
            }
        }

        internal static void DrawMark(Graphics graphics, RectangleF bounds)
        {
            GraphicsState saved = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 32F, bounds.Height / 32F);
            using (GraphicsPath background = RoundRect(new RectangleF(0.5F, 0.5F, 31, 31), 8))
            using (SolidBrush fill = new SolidBrush(Surface))
            using (Pen outline = new Pen(Border, 1))
            {
                graphics.FillPath(fill, background);
                graphics.DrawPath(outline, background);
            }
            using (Pen rack = new Pen(Accent, 1.6F))
            using (SolidBrush light = new SolidBrush(Accent))
            {
                for (int index = 0; index < 2; index++)
                {
                    float top = 8.0F + index * 8.8F;
                    using (GraphicsPath bay = RoundRect(new RectangleF(7.2F, top, 17.6F, 6.0F), 1.8F))
                        graphics.DrawPath(rack, bay);
                    graphics.FillEllipse(light, 10.1F, top + 2.1F, 1.8F, 1.8F);
                    graphics.DrawLine(rack, 15.4F, top + 3, 21.5F, top + 3);
                }
            }
            graphics.Restore(saved);
        }
    }

    internal enum ShellGlyph { None, Home, Refresh, Download, Settings, External, Tray, ArrowRight }

    internal sealed class ShellLogo : Control
    {
        internal ShellLogo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(38, 38);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "NAS";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float side = Math.Min(ClientSize.Width, ClientSize.Height);
            if (side > 0) NasTheme.DrawMark(e.Graphics, new RectangleF((ClientSize.Width - side) / 2F, (ClientSize.Height - side) / 2F, side, side));
        }
    }

    internal sealed class ShellButton : Button
    {
        private bool hover;
        private bool pressed;
        private bool primary;
        private bool compact;
        private ShellGlyph glyph;

        internal bool Primary { get { return primary; } set { primary = value; Invalidate(); } }
        internal bool Compact { get { return compact; } set { compact = value; Invalidate(); } }
        internal ShellGlyph Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }

        internal ShellButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = NasTheme.Surface;
            ForeColor = NasTheme.Text;
            Size = new Size(112, 36);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = false; Invalidate(); } base.OnKeyUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? NasTheme.Background : Parent.BackColor);
            float scale = e.Graphics.DpiX / 96F;
            RectangleF bounds = new RectangleF(0.5F, 0.5F, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            Color fill = primary ? NasTheme.Accent : pressed ? NasTheme.Pressed : hover ? NasTheme.Hover : NasTheme.Surface;
            Color foreground = primary ? NasTheme.Background : NasTheme.Text;
            Color line = primary ? NasTheme.Accent : NasTheme.Border;
            if (!Enabled) { fill = NasTheme.Surface; foreground = NasTheme.Muted; line = NasTheme.Border; }
            else if (primary && pressed) fill = Color.FromArgb(66, 178, 145);
            else if (primary && hover) fill = Color.FromArgb(119, 231, 197);
            if (SystemInformation.HighContrast)
            {
                fill = primary ? SystemColors.Highlight : SystemColors.Control;
                foreground = !Enabled ? SystemColors.GrayText : primary ? SystemColors.HighlightText : SystemColors.ControlText;
                line = SystemColors.ControlText;
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = NasTheme.RoundRect(bounds, 7 * scale))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen outline = new Pen(line, 1))
            {
                e.Graphics.FillPath(brush, shape);
                e.Graphics.DrawPath(outline, shape);
            }

            bool showText = !compact && !String.IsNullOrEmpty(Text);
            bool showIcon = glyph != ShellGlyph.None;
            int iconSize = (int)Math.Round(16 * scale);
            int gap = showText && showIcon ? (int)Math.Round(8 * scale) : 0;
            Size textSize = showText ? TextRenderer.MeasureText(Text, Font, new Size(Int32.MaxValue, Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding) : Size.Empty;
            int contentWidth = (showIcon ? iconSize : 0) + gap + textSize.Width;
            int left = (ClientSize.Width - contentWidth) / 2;
            if (showIcon)
            {
                using (Pen iconPen = new Pen(foreground, 1.6F))
                {
                    iconPen.StartCap = LineCap.Round;
                    iconPen.EndCap = LineCap.Round;
                    iconPen.LineJoin = LineJoin.Round;
                    DrawGlyph(e.Graphics, iconPen, new RectangleF(left, (ClientSize.Height - iconSize) / 2F, iconSize, iconSize));
                }
                left += iconSize + gap;
            }
            if (showText)
            {
                TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Math.Max(0, ClientSize.Width - left - (int)(10 * scale)), ClientSize.Height), foreground, flags);
            }
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(ClientRectangle, -(int)(4 * scale), -(int)(4 * scale));
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, foreground, fill);
            }
        }

        private void DrawGlyph(Graphics graphics, Pen pen, RectangleF bounds)
        {
            GraphicsState saved = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 16F, bounds.Height / 16F);
            switch (glyph)
            {
                case ShellGlyph.Home:
                    graphics.DrawLines(pen, new PointF[] { new PointF(2, 7), new PointF(8, 2), new PointF(14, 7) });
                    graphics.DrawLines(pen, new PointF[] { new PointF(4, 6), new PointF(4, 14), new PointF(12, 14), new PointF(12, 6) });
                    graphics.DrawLines(pen, new PointF[] { new PointF(7, 14), new PointF(7, 10), new PointF(9, 10), new PointF(9, 14) });
                    break;
                case ShellGlyph.Refresh:
                    graphics.DrawArc(pen, 2.7F, 2.7F, 10.6F, 10.6F, 45, 285);
                    graphics.DrawLines(pen, new PointF[] { new PointF(10, 3), new PointF(13.5F, 3), new PointF(13.5F, 6.5F) });
                    break;
                case ShellGlyph.Download:
                    graphics.DrawLine(pen, 8, 2, 8, 10);
                    graphics.DrawLines(pen, new PointF[] { new PointF(4.5F, 7), new PointF(8, 10.5F), new PointF(11.5F, 7) });
                    graphics.DrawLines(pen, new PointF[] { new PointF(2.5F, 10), new PointF(2.5F, 13.5F), new PointF(13.5F, 13.5F), new PointF(13.5F, 10) });
                    break;
                case ShellGlyph.Settings:
                    graphics.DrawEllipse(pen, 3.5F, 3.5F, 9, 9);
                    graphics.DrawEllipse(pen, 6, 6, 4, 4);
                    for (int index = 0; index < 8; index++)
                    {
                        double angle = index * Math.PI / 4;
                        graphics.DrawLine(pen, 8 + (float)Math.Cos(angle) * 4.5F, 8 + (float)Math.Sin(angle) * 4.5F, 8 + (float)Math.Cos(angle) * 6.3F, 8 + (float)Math.Sin(angle) * 6.3F);
                    }
                    break;
                case ShellGlyph.External:
                    graphics.DrawLines(pen, new PointF[] { new PointF(7, 3), new PointF(3, 3), new PointF(3, 13), new PointF(13, 13), new PointF(13, 9) });
                    graphics.DrawLine(pen, 7, 9, 13, 3);
                    graphics.DrawLines(pen, new PointF[] { new PointF(9, 3), new PointF(13, 3), new PointF(13, 7) });
                    break;
                case ShellGlyph.Tray:
                    graphics.DrawLine(pen, 8, 2, 8, 8);
                    graphics.DrawLines(pen, new PointF[] { new PointF(5, 5), new PointF(8, 8), new PointF(11, 5) });
                    graphics.DrawLines(pen, new PointF[] { new PointF(3, 8), new PointF(2, 13), new PointF(14, 13), new PointF(13, 8) });
                    graphics.DrawLine(pen, 5.5F, 11, 10.5F, 11);
                    break;
                case ShellGlyph.ArrowRight:
                    graphics.DrawLine(pen, 2.5F, 8, 13.5F, 8);
                    graphics.DrawLines(pen, new PointF[] { new PointF(9, 3.5F), new PointF(13.5F, 8), new PointF(9, 12.5F) });
                    break;
            }
            graphics.Restore(saved);
        }
    }
}
