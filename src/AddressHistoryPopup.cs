using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NasDesk
{
    // An address suggestion surface must leave the editable address in focus.
    // Both the form and its drawn child avoid activation; no native ListBox or
    // button can take keyboard focus while the user is typing an address.
    internal sealed class AddressHistoryPopup : Form, IMessageFilter
    {
        private readonly PopupSurface surface;
        private Control anchor;
        private bool filterInstalled;
        private bool themeSubscribed;
        internal AddressHistoryEntry SelectedEntry { get; private set; }
        internal event EventHandler EntryChosen;
        internal event EventHandler ManageRequested;

        internal string EmptyMessage
        {
            get { return surface.EmptyMessage; }
            set { surface.EmptyMessage = value; surface.Invalidate(); }
        }

        internal AddressHistoryPopup(IList<AddressHistoryEntry> entries)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            Text = "NAS 주소 기록";
            surface = new PopupSurface(entries) { Dock = DockStyle.Fill };
            surface.ChooseRequested += delegate { ChooseSelected(); };
            surface.ManageRequested += delegate {
                EventHandler handler = ManageRequested;
                Dismiss();
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(surface);
            NasTheme.Changed += OnThemeChanged;
            themeSubscribed = true;
            RefreshTheme();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                value.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return value;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = (IntPtr)3; return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }

        internal void ShowFor(Control value)
        {
            if (IsDisposed) throw new ObjectDisposedException("AddressHistoryPopup");
            if (value == null) throw new ArgumentNullException("value");
            if (value.IsDisposed || !value.Visible) return;
            Form owner = value.FindForm();
            if (owner == null || owner.IsDisposed) return;
            anchor = value;
            float scale;
            using (Graphics graphics = value.CreateGraphics()) scale = graphics.DpiX / 96F;
            surface.SetScale(scale);
            Rectangle area = Screen.FromControl(value).WorkingArea;
            Rectangle input = value.RectangleToScreen(value.ClientRectangle);
            int edge = Math.Max(2, (int)Math.Round(6F * scale));
            int width = Math.Min(Math.Max(value.Width, (int)Math.Round(420F * scale)), Math.Max(1, area.Width - edge * 2));
            int desired = surface.PreferredPopupHeight;
            int below = Math.Max(0, area.Bottom - input.Bottom - edge * 2);
            int above = Math.Max(0, input.Top - area.Top - edge * 2);
            bool useBelow = desired <= below || below >= above;
            int room = useBelow ? below : above;
            int height = Math.Min(desired, Math.Max(1, room));
            int left = Math.Max(area.Left + edge, Math.Min(input.Left, area.Right - edge - width));
            int top = useBelow ? input.Bottom + edge : input.Top - edge - height;
            top = Math.Max(area.Top + edge, Math.Min(top, area.Bottom - edge - height));
            Bounds = new Rectangle(left, top, width, height);
            if (!Visible) Show(owner);
            if (!filterInstalled)
            {
                Application.AddMessageFilter(this);
                filterInstalled = true;
            }
        }

        internal bool HandleEditorKey(Keys keyData)
        {
            if (IsDisposed || !Visible) return false;
            if (keyData == Keys.Escape) { Dismiss(); return true; }
            if (keyData == Keys.Down || keyData == Keys.Up)
                return surface.MoveSelection(keyData == Keys.Down ? 1 : -1);
            if (keyData == Keys.Enter && surface.SelectedIndex >= 0)
            {
                ChooseSelected();
                return true;
            }
            return false;
        }

        private void ChooseSelected()
        {
            AddressHistoryEntry value = surface.SelectedValue;
            if (value == null || IsDisposed) return;
            SelectedEntry = new AddressHistoryEntry { Name = value.Name, Url = value.Url, Type = value.Type };
            EventHandler handler = EntryChosen;
            if (handler != null) handler(this, EventArgs.Empty);
            Dismiss();
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (IsDisposed || !Visible) return false;
            if (message.Msg == 0x020A) // WM_MOUSEWHEEL uses screen coordinates.
            {
                long location = message.LParam.ToInt64();
                Point point = new Point((short)(location & 0xFFFF), (short)((location >> 16) & 0xFFFF));
                if (Bounds.Contains(point))
                {
                    int delta = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);
                    surface.ScrollBy(-(delta / 120) * 3);
                    return true;
                }
            }
            if (message.Msg == 0x0201 || message.Msg == 0x0204 || message.Msg == 0x0207 || message.Msg == 0x020B
                || message.Msg == 0x00A1 || message.Msg == 0x00A4 || message.Msg == 0x00A7)
            {
                bool inside = IsHandleCreated && (message.HWnd == Handle || IsChild(Handle, message.HWnd));
                bool address = anchor != null && !anchor.IsDisposed && anchor.IsHandleCreated
                    && (message.HWnd == anchor.Handle || IsChild(anchor.Handle, message.HWnd));
                if (!inside && !address) Dismiss();
            }
            // In particular, never eat the click that closes the chooser. A
            // WebView native child must receive its original mouse event.
            return false;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr parent, IntPtr child);

        internal void Dismiss()
        {
            if (IsDisposed) return;
            if (IsHandleCreated) Close();
            else Dispose();
        }

        private void OnThemeChanged(object sender, EventArgs e) { if (!IsDisposed) RefreshTheme(); }

        private void RefreshTheme()
        {
            BackColor = surface.BackColor = SystemInformation.HighContrast ? SystemColors.Window : NasTheme.Surface;
            surface.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Text;
            surface.Invalidate();
        }

        private void RemoveSubscriptions()
        {
            if (filterInstalled) { Application.RemoveMessageFilter(this); filterInstalled = false; }
            if (themeSubscribed) { NasTheme.Changed -= OnThemeChanged; themeSubscribed = false; }
            anchor = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            RemoveSubscriptions();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) RemoveSubscriptions();
            base.Dispose(disposing);
        }

        private sealed class PopupSurface : Control
        {
            private readonly List<AddressHistoryEntry> entries = new List<AddressHistoryEntry>();
            private readonly Font nameFont = new Font("Malgun Gothic", 10F, FontStyle.Bold);
            private readonly Font detailFont = new Font("Malgun Gothic", 8.5F);
            private readonly Font headerFont = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
            private float scale = 1F;
            private int selectedIndex = -1;
            private int firstIndex;
            private int hoverIndex = -1;
            private int pressedIndex = -1;
            private bool footerHover;
            private bool footerPressed;
            private bool dragging;
            private int dragOffset;
            internal string EmptyMessage = "저장된 NAS가 없습니다.\n주소 기록 관리에서 추가해 보세요.";
            internal event EventHandler ChooseRequested;
            internal event EventHandler ManageRequested;

            internal PopupSurface(IList<AddressHistoryEntry> values)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                AccessibleName = "저장된 NAS 주소 선택";
                if (values != null)
                    foreach (AddressHistoryEntry value in values)
                    {
                        if (value == null) continue;
                        entries.Add(new AddressHistoryEntry { Name = value.Name, Url = value.Url, Type = value.Type });
                        if (entries.Count == 30) break;
                    }
            }

            internal int SelectedIndex { get { return selectedIndex; } }
            internal AddressHistoryEntry SelectedValue { get { return selectedIndex < 0 ? null : entries[selectedIndex]; } }
            private int Px(float value) { return Math.Max(1, (int)Math.Round(value * scale)); }
            private int RowHeight { get { return Px(60); } }
            private Rectangle ListBounds { get { return new Rectangle(Px(7), Px(36), Math.Max(1, Width - Px(14)), Math.Max(0, Height - Px(82))); } }
            private Rectangle FooterBounds { get { return new Rectangle(Px(1), Math.Max(Px(1), Height - Px(43)), Math.Max(1, Width - Px(2)), Px(42)); } }
            private int VisibleRows { get { return Math.Max(1, ListBounds.Height / RowHeight); } }
            internal int PreferredPopupHeight { get { return Px(82) + (entries.Count == 0 ? Px(78) : Math.Min(entries.Count, 5) * RowHeight); } }
            private int MaxFirst { get { return Math.Max(0, entries.Count - VisibleRows); } }
            private bool Scrolls { get { return entries.Count > VisibleRows; } }
            private Rectangle TrackBounds { get { Rectangle list = ListBounds; return new Rectangle(list.Right - Px(10), list.Top + Px(3), Px(8), Math.Max(1, list.Height - Px(6))); } }
            private Rectangle ThumbBounds
            {
                get
                {
                    Rectangle track = TrackBounds;
                    int height = Math.Min(track.Height, Math.Max(Px(24), track.Height * VisibleRows / Math.Max(1, entries.Count)));
                    int top = track.Top + (MaxFirst == 0 ? 0 : (track.Height - height) * firstIndex / MaxFirst);
                    return new Rectangle(track.Left + Px(1), top, Math.Max(1, track.Width - Px(2)), height);
                }
            }

            internal void SetScale(float value) { scale = value; Invalidate(); }

            internal bool MoveSelection(int direction)
            {
                if (entries.Count == 0) return false;
                selectedIndex = selectedIndex < 0 ? (direction > 0 ? 0 : entries.Count - 1)
                    : Math.Max(0, Math.Min(entries.Count - 1, selectedIndex + direction));
                if (selectedIndex < firstIndex) firstIndex = selectedIndex;
                if (selectedIndex >= firstIndex + VisibleRows) firstIndex = selectedIndex - VisibleRows + 1;
                Invalidate();
                return true;
            }

            internal void ScrollBy(int rows)
            {
                firstIndex = Math.Max(0, Math.Min(MaxFirst, firstIndex + rows));
                Invalidate();
            }

            private Rectangle RowBounds(int index)
            {
                Rectangle list = ListBounds;
                return new Rectangle(list.Left, list.Top + (index - firstIndex) * RowHeight,
                    Math.Max(1, list.Width - (Scrolls ? Px(14) : 0)), RowHeight);
            }

            private int HitRow(Point point)
            {
                Rectangle list = ListBounds;
                if (!list.Contains(point) || Scrolls && TrackBounds.Contains(point)) return -1;
                int index = firstIndex + (point.Y - list.Top) / RowHeight;
                return index >= 0 && index < entries.Count && index < firstIndex + VisibleRows ? index : -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging)
                {
                    Rectangle track = TrackBounds, thumb = ThumbBounds;
                    int travel = Math.Max(1, track.Height - thumb.Height);
                    firstIndex = Math.Max(0, Math.Min(MaxFirst, (int)Math.Round((e.Y - dragOffset - track.Top) * (double)MaxFirst / travel)));
                }
                hoverIndex = HitRow(e.Location);
                footerHover = FooterBounds.Contains(e.Location);
                Cursor = hoverIndex >= 0 || footerHover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hoverIndex = -1; footerHover = false; Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (Scrolls && TrackBounds.Contains(e.Location))
                {
                    if (ThumbBounds.Contains(e.Location)) { dragging = true; dragOffset = e.Y - ThumbBounds.Top; Capture = true; }
                    else ScrollBy(e.Y < ThumbBounds.Top ? -VisibleRows : VisibleRows);
                    return;
                }
                pressedIndex = HitRow(e.Location);
                footerPressed = FooterBounds.Contains(e.Location);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                if (dragging) { dragging = false; Capture = false; return; }
                int index = HitRow(e.Location);
                if (pressedIndex >= 0 && pressedIndex == index)
                {
                    selectedIndex = index;
                    EventHandler chosen = ChooseRequested;
                    if (chosen != null) chosen(this, EventArgs.Empty);
                }
                else if (footerPressed && FooterBounds.Contains(e.Location))
                {
                    EventHandler manage = ManageRequested;
                    if (manage != null) manage(this, EventArgs.Empty);
                }
                pressedIndex = -1; footerPressed = false;
            }

            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                base.OnMouseCaptureChanged(e);
                if (!Capture) dragging = false;
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollBy(-(e.Delta / 120) * 3);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0021) { message.Result = (IntPtr)3; return; }
                base.WndProc(ref message);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                bool contrast = SystemInformation.HighContrast;
                Color background = contrast ? SystemColors.Window : NasTheme.Surface;
                Color text = contrast ? SystemColors.WindowText : NasTheme.Text;
                Color muted = contrast ? SystemColors.WindowText : NasTheme.Muted;
                Color border = contrast ? SystemColors.WindowText : NasTheme.Border;
                e.Graphics.Clear(background);
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(e.Graphics, "저장된 NAS", headerFont,
                    new Rectangle(Px(17), Px(6), Math.Max(1, Width - Px(34)), Px(26)), muted, flags);
                Rectangle clip = ListBounds;
                GraphicsState saved = e.Graphics.Save();
                e.Graphics.SetClip(clip);
                if (entries.Count == 0)
                    TextRenderer.DrawText(e.Graphics, EmptyMessage ?? String.Empty, detailFont, clip, muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                for (int index = firstIndex; index < Math.Min(entries.Count, firstIndex + VisibleRows); index++)
                {
                    Rectangle row = RowBounds(index);
                    bool selected = index == selectedIndex;
                    if (selected || index == hoverIndex)
                    {
                        Color fill = contrast ? SystemColors.Highlight : selected ? NasTheme.Blend(background, NasTheme.Accent, 0.13F) : NasTheme.Hover;
                        using (GraphicsPath shape = NasTheme.RoundRect(new RectangleF(row.Left, row.Top + Px(2), row.Width, row.Height - Px(4)), Px(7)))
                        using (SolidBrush brush = new SolidBrush(fill)) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillPath(brush, shape); }
                    }
                    Color ink = contrast && (selected || index == hoverIndex) ? SystemColors.HighlightText : text;
                    Color detail = contrast ? ink : muted;
                    int left = row.Left + Px(11), available = Math.Max(1, row.Width - Px(22));
                    TextRenderer.DrawText(e.Graphics, entries[index].DisplayName, nameFont,
                        new Rectangle(left, row.Top + Px(6), available, Px(23)), ink, flags);
                    string type = String.Equals(entries[index].Type, "quickconnect", StringComparison.OrdinalIgnoreCase) ? "QuickConnect" : "HTTPS";
                    TextRenderer.DrawText(e.Graphics, type + " · " + entries[index].Url, detailFont,
                        new Rectangle(left, row.Top + Px(31), available, Px(18)), detail, flags);
                }
                if (Scrolls)
                {
                    using (SolidBrush track = new SolidBrush(contrast ? SystemColors.Window : NasTheme.Background)) e.Graphics.FillRectangle(track, TrackBounds);
                    using (GraphicsPath shape = NasTheme.RoundRect(ThumbBounds, Px(3)))
                    using (SolidBrush thumb = new SolidBrush(contrast ? SystemColors.WindowText : NasTheme.Muted)) e.Graphics.FillPath(thumb, shape);
                }
                e.Graphics.Restore(saved);
                Rectangle footer = FooterBounds;
                if (footerHover)
                    using (SolidBrush brush = new SolidBrush(contrast ? SystemColors.Highlight : NasTheme.Hover)) e.Graphics.FillRectangle(brush, footer);
                using (Pen pen = new Pen(border, Px(1)))
                {
                    e.Graphics.DrawLine(pen, Px(12), footer.Top, Width - Px(12), footer.Top);
                    e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                }
                Color footerText = contrast && footerHover ? SystemColors.HighlightText : contrast ? text : NasTheme.Accent;
                TextRenderer.DrawText(e.Graphics, "주소 기록 관리", headerFont,
                    new Rectangle(Px(18), footer.Top, Math.Max(1, Width - Px(36)), footer.Height), footerText, flags);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { nameFont.Dispose(); detailFont.Dispose(); headerFont.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
