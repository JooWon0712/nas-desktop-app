using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NasDesk
{
    internal enum ThemeMode { System, Light, Dark }

    internal sealed class ThemePalette
    {
        internal string Key { get; private set; }
        internal string Name { get; private set; }
        internal Color Background { get; private set; }
        internal Color Surface { get; private set; }
        internal Color Border { get; private set; }
        internal Color Text { get; private set; }
        internal Color Muted { get; private set; }
        internal Color Accent { get; private set; }
        internal Color Hover { get; private set; }
        internal Color Pressed { get; private set; }
        internal Color Success { get; private set; }
        internal Color Warning { get; private set; }
        internal Color Danger { get; private set; }
        internal Color AccentText { get; private set; }

        internal ThemePalette(string key, string name, Color background, Color surface, Color border,
            Color text, Color muted, Color accent, Color hover, Color pressed,
            Color success, Color warning, Color danger, Color accentText)
        {
            Key = key;
            Name = name;
            Background = background;
            Surface = surface;
            Border = border;
            Text = text;
            Muted = muted;
            Accent = accent;
            Hover = hover;
            Pressed = pressed;
            Success = success;
            Warning = warning;
            Danger = danger;
            AccentText = accentText;
        }
    }

    internal static class NasTheme
    {
        private static readonly ThemePalette[] darkPalettes = new ThemePalette[]
        {
            new ThemePalette("charcoal-blue", "차콜 블루",
                Rgb(0x121419), Rgb(0x1B1E25), Rgb(0x303540), Rgb(0xF0F3F9),
                Rgb(0xA1AAB9), Rgb(0x83B4FF), Rgb(0x262B35), Rgb(0x2F3642),
                Rgb(0x83B4FF), Rgb(0xF1C374), Rgb(0xF2919A), Rgb(0x131B29)),
            new ThemePalette("violet", "그래파이트 바이올렛",
                Rgb(0x17161D), Rgb(0x211F2A), Rgb(0x3B354B), Rgb(0xF2EFF9),
                Rgb(0xAEA5BF), Rgb(0xB7A1FF), Rgb(0x302B3D), Rgb(0x3B344C),
                Rgb(0xB7A1FF), Rgb(0xE8C28A), Rgb(0xF298AF), Rgb(0x211A30)),
            new ThemePalette("amber", "웜 앰버",
                Rgb(0x1A1714), Rgb(0x24201C), Rgb(0x40372E), Rgb(0xF7F3ED),
                Rgb(0xB1A496), Rgb(0xF2BD78), Rgb(0x312B25), Rgb(0x3D342B),
                Rgb(0xF2BD78), Rgb(0xEBD887), Rgb(0xF49B91), Rgb(0x2B2016)),
            new ThemePalette("forest", "포레스트 그린",
                Rgb(0x141A16), Rgb(0x1D2720), Rgb(0x34483A), Rgb(0xEDF5EF),
                Rgb(0xA3B5A8), Rgb(0x8FD69A), Rgb(0x29392E), Rgb(0x344839),
                Rgb(0x8FD69A), Rgb(0xE5C482), Rgb(0xED9C9C), Rgb(0x17291B)),
            new ThemePalette("sage", "세이지 올리브",
                Rgb(0x191C16), Rgb(0x24291F), Rgb(0x414B35), Rgb(0xF2F5EB),
                Rgb(0xADB7A1), Rgb(0xB7CC8A), Rgb(0x323B29), Rgb(0x414C35),
                Rgb(0xB7CC8A), Rgb(0xE8CC89), Rgb(0xEDAA9A), Rgb(0x222B17)),
            new ThemePalette("mint", "네이비 민트",
                Rgb(0x11161E), Rgb(0x19212C), Rgb(0x2A3544), Rgb(0xE9EEF6),
                Rgb(0x8E9BAA), Rgb(0x59D6B0), Rgb(0x253140), Rgb(0x2D3C4C),
                Rgb(0x59D6B0), Rgb(0xF0C284), Rgb(0xF2919A), Rgb(0x10251E))
        };
        private static readonly ThemePalette[] lightPalettes = new ThemePalette[]
        {
            new ThemePalette("charcoal-blue", "차콜 블루",
                Rgb(0xF2F5FA), Rgb(0xFFFFFF), Rgb(0xCBD5E3), Rgb(0x1D2938),
                Rgb(0x566579), Rgb(0x285EB0), Rgb(0xE5ECF6), Rgb(0xD7E2F2),
                Rgb(0x285EB0), Rgb(0x8A5700), Rgb(0xB13A49), Rgb(0xFFFFFF)),
            new ThemePalette("violet", "그래파이트 바이올렛",
                Rgb(0xF6F3FA), Rgb(0xFFFFFF), Rgb(0xD9CFE5), Rgb(0x2B2235),
                Rgb(0x65566F), Rgb(0x7044A5), Rgb(0xEDE5F5), Rgb(0xE2D5EF),
                Rgb(0x7044A5), Rgb(0x875500), Rgb(0xB13A59), Rgb(0xFFFFFF)),
            new ThemePalette("amber", "웜 앰버",
                Rgb(0xF8F4EF), Rgb(0xFFFFFF), Rgb(0xDFD2C1), Rgb(0x33271D),
                Rgb(0x705D49), Rgb(0x8C4E10), Rgb(0xF0E6DA), Rgb(0xE7D7C2),
                Rgb(0x8C4E10), Rgb(0x795300), Rgb(0xAE3D34), Rgb(0xFFFFFF)),
            new ThemePalette("forest", "포레스트 그린",
                Rgb(0xF1F6F1), Rgb(0xFFFFFF), Rgb(0xC9D9CC), Rgb(0x203025),
                Rgb(0x526653), Rgb(0x286B3A), Rgb(0xE3EEE4), Rgb(0xD4E4D7),
                Rgb(0x286B3A), Rgb(0x805500), Rgb(0xAF3E45), Rgb(0xFFFFFF)),
            new ThemePalette("sage", "세이지 올리브",
                Rgb(0xF5F6EF), Rgb(0xFFFFFF), Rgb(0xD4DBC6), Rgb(0x2B3222),
                Rgb(0x626C50), Rgb(0x526C2F), Rgb(0xE9EDDD), Rgb(0xDDE4CB),
                Rgb(0x526C2F), Rgb(0x805500), Rgb(0xAF4438), Rgb(0xFFFFFF)),
            new ThemePalette("mint", "네이비 민트",
                Rgb(0xF0F7F5), Rgb(0xFFFFFF), Rgb(0xC9DDD6), Rgb(0x1F312C),
                Rgb(0x506A60), Rgb(0x106B52), Rgb(0xE1F0EB), Rgb(0xD1E5DD),
                Rgb(0x106B52), Rgb(0x805500), Rgb(0xAF3E45), Rgb(0xFFFFFF))
        };
        private static ThemePalette current = darkPalettes[0];
        private static ThemeMode mode = ThemeMode.Dark;
        private static bool isDark = true;

        internal static event EventHandler Changed;

        internal static ThemePalette[] Palettes { get { return (ThemePalette[])(isDark ? darkPalettes : lightPalettes).Clone(); } }
        internal static ThemePalette Current { get { return current; } }
        internal static ThemeMode Mode { get { return mode; } }
        internal static bool IsDark { get { return isDark; } }
        internal static Color Background { get { return current.Background; } }
        internal static Color Surface { get { return current.Surface; } }
        internal static Color Border { get { return current.Border; } }
        internal static Color Text { get { return current.Text; } }
        internal static Color Muted { get { return current.Muted; } }
        internal static Color Accent { get { return current.Accent; } }
        internal static Color Hover { get { return current.Hover; } }
        internal static Color Pressed { get { return current.Pressed; } }
        internal static Color Success { get { return current.Success; } }
        internal static Color Warning { get { return current.Warning; } }
        internal static Color Danger { get { return current.Danger; } }
        internal static Color AccentText { get { return current.AccentText; } }

        internal static bool SetPalette(string key)
        {
            return SetAppearance(key, mode);
        }

        internal static bool SetMode(ThemeMode value)
        {
            return SetAppearance(current.Key, value);
        }

        internal static bool SetAppearance(string key, ThemeMode value)
        {
            if (value != ThemeMode.System && value != ThemeMode.Light && value != ThemeMode.Dark) return false;
            bool dark = value == ThemeMode.Dark || value == ThemeMode.System && ReadSystemDarkMode();
            return ApplyAppearance(key, value, dark);
        }

        internal static void RefreshSystemAppearance()
        {
            if (mode == ThemeMode.System) RefreshSystemAppearance(ReadSystemDarkMode());
        }

        internal static void RefreshSystemAppearance(bool systemIsDark)
        {
            if (mode == ThemeMode.System) ApplyAppearance(current.Key, mode, systemIsDark);
        }

        internal static bool ReadSystemDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            return false;
        }

        private static bool ApplyAppearance(string key, ThemeMode value, bool dark)
        {
            foreach (ThemePalette palette in dark ? darkPalettes : lightPalettes)
            {
                if (!String.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (Object.ReferenceEquals(current, palette) && mode == value) return true;
                current = palette;
                mode = value;
                isDark = dark;
                EventHandler handler = Changed;
                if (handler != null) handler(null, EventArgs.Empty);
                return true;
            }
            return false;
        }

        private static Color Rgb(int value)
        {
            return Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
        }

        internal static Color Blend(Color start, Color end, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)Math.Round(start.R + (end.R - start.R) * amount),
                (int)Math.Round(start.G + (end.G - start.G) * amount),
                (int)Math.Round(start.B + (end.B - start.B) * amount));
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icon);

        internal static void ApplyWindowChrome(Form form)
        {
            if (form == null) return;
            form.HandleCreated -= OnWindowHandleCreated;
            form.HandleCreated += OnWindowHandleCreated;
            RefreshWindowChrome(form);
        }

        internal static void RefreshWindowChrome(Form form)
        {
            if (form != null && !form.IsDisposed && form.IsHandleCreated) ApplyChrome(form.Handle);
        }

        private static void OnWindowHandleCreated(object sender, EventArgs e)
        {
            RefreshWindowChrome(sender as Form);
        }

        private static void ApplyChrome(IntPtr handle)
        {
            if (SystemInformation.HighContrast) return;
            try
            {
                int dark = IsDark ? 1 : 0;
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

    internal enum ShellGlyph { None, Home, Refresh, Download, Settings, External, Tray, ArrowRight, History }

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
            Color foreground = primary ? NasTheme.AccentText : NasTheme.Text;
            Color line = primary ? NasTheme.Accent : NasTheme.Border;
            if (!Enabled) { fill = NasTheme.Surface; foreground = NasTheme.Muted; line = NasTheme.Border; }
            else if (primary && pressed) fill = NasTheme.IsDark ?
                NasTheme.Blend(NasTheme.Accent, NasTheme.Background, 0.18F) : NasTheme.Blend(NasTheme.Accent, Color.Black, 0.15F);
            else if (primary && hover) fill = NasTheme.IsDark ?
                NasTheme.Blend(NasTheme.Accent, Color.White, 0.12F) : NasTheme.Blend(NasTheme.Accent, Color.Black, 0.06F);
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
                case ShellGlyph.History:
                    graphics.DrawArc(pen, 2.5F, 2.5F, 11, 11, 210, 300);
                    graphics.DrawLines(pen, new PointF[] { new PointF(1.5F, 2), new PointF(1.5F, 6), new PointF(5.5F, 6) });
                    graphics.DrawLines(pen, new PointF[] { new PointF(8, 4.5F), new PointF(8, 8), new PointF(10.5F, 9.5F) });
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
