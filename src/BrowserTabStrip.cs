using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class BrowserTabStrip : FlowLayoutPanel
    {
        private readonly List<TabItem> items = new List<TabItem>();
        private readonly ToolTip tips = new ToolTip();
        private bool layingOut;
        private float layoutScale = 1F;

        internal event Action<object> TabSelected;
        internal event Action<object> TabCloseRequested;
        internal object SelectedKey { get; private set; }
        internal int TabCount { get { return items.Count; } }

        internal BrowserTabStrip()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            AutoScroll = true;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Height = 40;
            TabStop = false;
            AccessibleName = "NAS 앱 탭";
            AccessibleRole = AccessibleRole.PageTabList;
            RefreshTheme();
        }

        internal void AddTab(object key, string title, bool closable)
        {
            if (key == null) throw new ArgumentNullException("key");
            TabItem existing = Find(key);
            if (existing != null) { SetTitle(key, title); return; }
            TabItem item = new TabItem(this, key, closable);
            item.Text = NormalizeTitle(title);
            items.Add(item);
            Controls.Add(item);
            tips.SetToolTip(item, item.Text);
            PerformLayout();
        }

        internal void SelectTab(object key)
        {
            TabItem selected = Find(key);
            if (selected == null) return;
            SelectedKey = selected.Key;
            foreach (TabItem item in items) item.SetSelected(Object.Equals(item.Key, key));
            ScrollControlIntoView(selected);
        }

        internal void RemoveTab(object key)
        {
            TabItem item = Find(key);
            if (item == null) return;
            items.Remove(item);
            if (Object.Equals(SelectedKey, key)) SelectedKey = null;
            tips.SetToolTip(item, null);
            Controls.Remove(item);
            item.Dispose();
            PerformLayout();
        }

        internal void SetTitle(object key, string title)
        {
            TabItem item = Find(key);
            if (item == null) return;
            item.Text = NormalizeTitle(title);
            tips.SetToolTip(item, item.Text);
            item.Invalidate();
        }

        internal void RefreshTheme()
        {
            BackColor = SystemInformation.HighContrast ? SystemColors.Window : NasTheme.Background;
            ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Text;
            foreach (TabItem item in items) { item.BackColor = BackColor; item.ForeColor = ForeColor; item.Invalidate(); }
            Invalidate();
        }

        private TabItem Find(object key) { return items.Find(item => Object.Equals(item.Key, key)); }

        private static string NormalizeTitle(string title)
        {
            string value = title ?? String.Empty;
            char[] characters = value.ToCharArray();
            for (int index = 0; index < characters.Length; index++)
                if (Char.IsControl(characters[index])) characters[index] = ' ';
            value = new String(characters).Trim();
            if (value.Length == 0) return "NAS 앱";
            return value.Length > 512 ? value.Substring(0, 511) + "…" : value;
        }

        private void RequestSelection(TabItem item)
        {
            Action<object> handler = TabSelected;
            if (handler != null) handler(item.Key);
        }

        private void RequestClose(TabItem item)
        {
            if (!item.Closable) return;
            Action<object> handler = TabCloseRequested;
            if (handler != null) handler(item.Key);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!layingOut && items != null)
            {
                layingOut = true;
                try
                {
                    float scale = DeviceDpi / 96F;
                    // Legacy system-DPI awareness can leave DeviceDpi at 96
                    // while the window's actual drawing surface uses 144 DPI.
                    if (IsHandleCreated)
                        using (Graphics graphics = CreateGraphics()) scale = graphics.DpiX / 96F;
                    else if (Parent != null && Parent.IsHandleCreated)
                        using (Graphics graphics = Parent.CreateGraphics()) scale = graphics.DpiX / 96F;
                    layoutScale = scale;
                    int rowHeight = Px(34, scale), top = Px(2, scale), gap = Px(6, scale);
                    int requiredWidth = Padding.Horizontal;
                    foreach (TabItem item in items)
                    {
                        item.Size = new Size(Px(item.Closable ? 204 : 136, scale), rowHeight);
                        item.Margin = new Padding(0, top, gap, top);
                        requiredWidth += item.Width + item.Margin.Horizontal;
                    }
                    int height = rowHeight + top * 2 + Padding.Vertical;
                    if (requiredWidth > ClientSize.Width) height += SystemInformation.HorizontalScrollBarHeight;
                    if (Height != height) Height = height;
                }
                finally { layingOut = false; }
            }
            base.OnLayout(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PerformLayout();
        }

        private static int Px(int value, float scale) { return Math.Max(1, (int)Math.Round(value * scale)); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }

        private sealed class TabItem : Control
        {
            private readonly BrowserTabStrip strip;
            internal readonly object Key;
            internal readonly bool Closable;
            private bool selected;
            private bool hovered;
            private bool closeHovered;
            private bool closePressed;

            internal TabItem(BrowserTabStrip strip, object key, bool closable)
            {
                this.strip = strip;
                Key = key;
                Closable = closable;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PageTab;
                AccessibleDescription = closable ? "Enter로 선택하고 Delete로 닫을 수 있는 NAS 앱 탭입니다." : "NAS 기본 화면 탭입니다.";
                BackColor = strip.BackColor;
                ForeColor = strip.ForeColor;
            }

            internal void SetSelected(bool value)
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }

            private Rectangle CloseBounds
            {
                get
                {
                    float scale = strip.layoutScale;
                    int size = Px(26, scale);
                    return new Rectangle(Width - size - Px(4, scale), (Height - size) / 2, size, size);
                }
            }

            protected override void OnTextChanged(EventArgs e)
            {
                base.OnTextChanged(e);
                AccessibleName = Text;
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = closeHovered = closePressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool overClose = Closable && CloseBounds.Contains(e.Location);
                if (closeHovered != overClose) { closeHovered = overClose; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Focus();
                closePressed = Closable && CloseBounds.Contains(e.Location);
                if (!closePressed) strip.RequestSelection(this);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                bool close = e.Button == MouseButtons.Left && closePressed && CloseBounds.Contains(e.Location);
                closePressed = false;
                Invalidate();
                if (close) strip.RequestClose(this);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    strip.RequestSelection(this);
                }
                else if (e.KeyCode == Keys.Delete && Closable)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    strip.RequestClose(this);
                }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 2 || Height < 2) return;
                Graphics graphics = e.Graphics;
                float scale = graphics.DpiX / 96F;
                bool highContrast = SystemInformation.HighContrast;
                Color fill = highContrast ? SystemColors.Window : selected ? NasTheme.Surface : hovered ? NasTheme.Hover : NasTheme.Background;
                Color border = highContrast ? SystemColors.WindowText : selected ? NasTheme.Accent : NasTheme.Border;
                Color ink = highContrast ? SystemColors.WindowText : selected || hovered ? NasTheme.Text : NasTheme.Muted;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath outline = NasTheme.RoundRect(new RectangleF(scale / 2, scale / 2, Width - scale, Height - scale), 7 * scale))
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen pen = new Pen(border, scale))
                {
                    graphics.FillPath(brush, outline);
                    graphics.DrawPath(pen, outline);
                }
                int inset = Px(12, scale);
                int textRight = Closable ? CloseBounds.Left - Px(4, scale) : Width - inset;
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(inset, 0, Math.Max(0, textRight - inset), Height), ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Closable)
                {
                    Rectangle close = CloseBounds;
                    if (closeHovered)
                        using (SolidBrush brush = new SolidBrush(highContrast ? SystemColors.Highlight : NasTheme.Pressed))
                            graphics.FillEllipse(brush, close);
                    Color closeInk = highContrast ? (closeHovered ? SystemColors.HighlightText : SystemColors.WindowText) : closeHovered ? NasTheme.Danger : NasTheme.Muted;
                    int centerX = close.Left + close.Width / 2, centerY = close.Top + close.Height / 2;
                    int arm = Px(4, scale);
                    using (Pen pen = new Pen(closeInk, 1.4F * scale))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        graphics.DrawLine(pen, centerX - arm, centerY - arm, centerX + arm, centerY + arm);
                        graphics.DrawLine(pen, centerX - arm, centerY + arm, centerX + arm, centerY - arm);
                    }
                }
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -Px(4, scale), -Px(4, scale)), ink, fill);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new TabAccessibleObject(this); }

            private sealed class TabAccessibleObject : ControlAccessibleObject
            {
                private readonly TabItem item;
                internal TabAccessibleObject(TabItem item) : base(item) { this.item = item; }
                public override AccessibleStates State { get { return base.State | (item.selected ? AccessibleStates.Selected : AccessibleStates.None); } }
                public override string DefaultAction { get { return "선택"; } }
                public override void DoDefaultAction() { item.strip.RequestSelection(item); }
                public override int GetChildCount() { return item.Closable ? 1 : 0; }
                public override AccessibleObject GetChild(int index) { return index == 0 && item.Closable ? new CloseAccessibleObject(item, this) : null; }
            }

            private sealed class CloseAccessibleObject : AccessibleObject
            {
                private readonly TabItem item;
                private readonly AccessibleObject parent;
                internal CloseAccessibleObject(TabItem item, AccessibleObject parent) { this.item = item; this.parent = parent; }
                public override string Name { get { return item.Text + " 탭 닫기"; } set { } }
                public override string DefaultAction { get { return "닫기"; } }
                public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
                public override AccessibleObject Parent { get { return parent; } }
                public override Rectangle Bounds { get { return item.RectangleToScreen(item.CloseBounds); } }
                public override AccessibleStates State { get { return item.Enabled ? AccessibleStates.None : AccessibleStates.Unavailable; } }
                public override void DoDefaultAction() { item.strip.RequestClose(item); }
            }
        }
    }
}
