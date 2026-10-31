using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class ContextMenuStyle : ToolStripProfessionalRenderer
    {
        private readonly HashSet<ToolStripDropDownMenu> menus = new HashSet<ToolStripDropDownMenu>();
        private readonly HashSet<ToolStripMenuItem> items = new HashSet<ToolStripMenuItem>();

        internal ContextMenuStyle() : base(new MenuColors()) { RoundedEdges = false; }

        internal static void Configure(ContextMenuStrip menu)
        {
            if (menu == null) return;
            ContextMenuStyle renderer = menu.Renderer as ContextMenuStyle ?? new ContextMenuStyle();
            renderer.AttachMenu(menu);
        }

        private void AttachMenu(ToolStripDropDownMenu menu)
        {
            if (!menus.Add(menu)) return;
            menu.Renderer = this;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Opening += delegate { UpdateMenu(menu); };
            menu.Opened += delegate { UpdateRegion(menu); };
            menu.SizeChanged += delegate { if (menu.Visible) UpdateRegion(menu); };
            menu.ItemAdded += delegate(object sender, ToolStripItemEventArgs e) { PrepareItem(menu, e.Item); };
            menu.Disposed += delegate { menus.Remove(menu); };
            UpdateMenu(menu);
        }

        private void UpdateMenu(ToolStripDropDownMenu menu)
        {
            float scale = ScaleFor(menu);
            menu.BackColor = BackgroundColor;
            menu.ForeColor = TextColor;
            menu.Padding = new Padding(Scale(6, scale));
            foreach (ToolStripItem item in menu.Items) PrepareItem(menu, item);
            // WinForms measures drop-down width without item padding. Reserve
            // each full row explicitly so right-side checks remain inside it.
            int width = 0;
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripMenuItem && item.Available)
                    width = Math.Max(width, item.GetPreferredSize(Size.Empty).Width);
            menu.MinimumSize = new Size(width + Scale(2, scale), 0);
            menu.Invalidate();
        }

        private void PrepareItem(ToolStripDropDownMenu menu, ToolStripItem item)
        {
            float scale = ScaleFor(menu);
            item.Margin = Padding.Empty;
            item.ForeColor = TextColor;
            ToolStripSeparator separator = item as ToolStripSeparator;
            if (separator != null)
            {
                separator.Margin = new Padding(Scale(6, scale), Scale(3, scale),
                    Scale(6, scale), Scale(3, scale));
                return;
            }
            item.Padding = new Padding(Scale(10, scale), Scale(8, scale),
                Scale(32, scale), Scale(8, scale));
            ToolStripMenuItem menuItem = item as ToolStripMenuItem;
            if (menuItem == null) return;
            if (items.Add(menuItem))
            {
                menuItem.DropDownOpening += delegate {
                    ToolStripDropDownMenu child = menuItem.DropDown as ToolStripDropDownMenu;
                    if (child != null) { AttachMenu(child); UpdateMenu(child); }
                };
                menuItem.Disposed += delegate { items.Remove(menuItem); };
            }
            if (menuItem.HasDropDownItems)
            {
                ToolStripDropDownMenu child = menuItem.DropDown as ToolStripDropDownMenu;
                if (child != null) AttachMenu(child);
            }
        }

        private static void UpdateRegion(ToolStripDropDownMenu menu)
        {
            Region previous = menu.Region;
            if (SystemInformation.HighContrast || menu.Width < 2 || menu.Height < 2)
                menu.Region = null;
            else
            {
                using (GraphicsPath path = NasTheme.RoundRect(
                    new RectangleF(0, 0, menu.Width, menu.Height), 10F * ScaleFor(menu)))
                    menu.Region = new Region(path);
            }
            if (previous != null) previous.Dispose();
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(BackgroundColor);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackgroundColor))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip.Width < 2 || e.ToolStrip.Height < 2) return;
            float scale = e.Graphics.DpiX / 96F;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = NasTheme.RoundRect(new RectangleF(scale / 2F, scale / 2F,
                e.ToolStrip.Width - scale, e.ToolStrip.Height - scale),
                SystemInformation.HighContrast ? 1F : 10F * scale))
            using (Pen pen = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Border, scale))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96F;
            RectangleF bounds = new RectangleF(scale, scale, e.Item.Width - scale * 2,
                e.Item.Height - scale * 2);
            if (e.Item.Enabled && (e.Item.Selected || e.Item.Pressed))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = NasTheme.RoundRect(bounds, 6F * scale))
                using (SolidBrush brush = new SolidBrush(SystemInformation.HighContrast ?
                    SystemColors.Highlight : e.Item.Pressed ? NasTheme.Pressed : NasTheme.Hover))
                    e.Graphics.FillPath(brush, path);
            }

            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            ToolStripDropDownMenu owner = e.ToolStrip as ToolStripDropDownMenu;
            if (item != null && item.Checked && owner != null && !owner.ShowCheckMargin)
            {
                float rightInset = (item.HasDropDownItems ? 29F : 17F) * scale;
                RectangleF check = new RectangleF(item.Width - rightInset - 11F * scale,
                    (item.Height - 11F * scale) / 2F, 11F * scale, 11F * scale);
                DrawCheck(e.Graphics, check, ItemColor(item, true), scale);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96F;
            float y = e.Item.Height / 2F;
            using (Pen pen = new Pen(SystemInformation.HighContrast ? SystemColors.GrayText : NasTheme.Border, scale))
                e.Graphics.DrawLine(pen, 6F * scale, y, e.Item.Width - 6F * scale, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = ItemColor(e.Item, false);
            float scale = e.Graphics.DpiX / 96F;
            int left = Scale(12, scale), right = Scale(32, scale);
            e.TextRectangle = new Rectangle(left, 0,
                Math.Max(0, e.Item.Width - left - right), e.Item.Height);
            e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = ItemColor(e.Item, false);
            float scale = e.Graphics.DpiX / 96F;
            int size = Scale(8, scale);
            e.ArrowRectangle = new Rectangle(e.Item.Width - Scale(21, scale),
                (e.Item.Height - size) / 2, size, size);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            ToolStripDropDownMenu owner = e.ToolStrip as ToolStripDropDownMenu;
            if (owner != null && !owner.ShowCheckMargin) return;
            DrawCheck(e.Graphics, e.ImageRectangle, ItemColor(e.Item, true), e.Graphics.DpiX / 96F);
        }

        private static void DrawCheck(Graphics graphics, RectangleF bounds, Color color, float scale)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 1.7F * scale))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLines(pen, new PointF[] {
                    new PointF(bounds.X + bounds.Width * 0.15F, bounds.Y + bounds.Height * 0.49F),
                    new PointF(bounds.X + bounds.Width * 0.40F, bounds.Y + bounds.Height * 0.74F),
                    new PointF(bounds.X + bounds.Width * 0.86F, bounds.Y + bounds.Height * 0.23F) });
            }
        }

        private static Color ItemColor(ToolStripItem item, bool accent)
        {
            if (!item.Enabled) return SystemInformation.HighContrast ? SystemColors.GrayText : NasTheme.Muted;
            if (SystemInformation.HighContrast)
                return item.Selected ? SystemColors.HighlightText : SystemColors.MenuText;
            return accent ? NasTheme.Accent : NasTheme.Text;
        }

        private static Color BackgroundColor
        {
            get { return SystemInformation.HighContrast ? SystemColors.Menu : NasTheme.Surface; }
        }

        private static Color TextColor
        {
            get { return SystemInformation.HighContrast ? SystemColors.MenuText : NasTheme.Text; }
        }

        private static float ScaleFor(ToolStrip menu)
        {
            using (Graphics graphics = menu.CreateGraphics()) return graphics.DpiX / 96F;
        }

        private static int Scale(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            internal MenuColors() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return BackgroundColor; } }
            public override Color ImageMarginGradientBegin { get { return BackgroundColor; } }
            public override Color ImageMarginGradientMiddle { get { return BackgroundColor; } }
            public override Color ImageMarginGradientEnd { get { return BackgroundColor; } }
            public override Color MenuBorder { get { return NasTheme.Border; } }
            public override Color MenuItemBorder { get { return NasTheme.Hover; } }
            public override Color MenuItemSelected { get { return NasTheme.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return NasTheme.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return NasTheme.Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return NasTheme.Pressed; } }
            public override Color MenuItemPressedGradientMiddle { get { return NasTheme.Pressed; } }
            public override Color MenuItemPressedGradientEnd { get { return NasTheme.Pressed; } }
            public override Color CheckBackground { get { return BackgroundColor; } }
            public override Color CheckSelectedBackground { get { return NasTheme.Hover; } }
            public override Color CheckPressedBackground { get { return NasTheme.Pressed; } }
        }
    }
}
