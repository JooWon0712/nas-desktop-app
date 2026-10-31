using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class ConnectionTypePicker : Button
    {
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem directItem;
        private readonly ToolStripMenuItem quickConnectItem;
        private ConnectionKind kind;
        private bool hover;
        internal event EventHandler KindChanged;
        internal event EventHandler EditRequested;

        internal ConnectionKind Kind
        {
            get { return kind; }
            set
            {
                if (value != ConnectionKind.DirectHttps && value != ConnectionKind.SynologyQuickConnect)
                    throw new ArgumentException("알 수 없는 접속 방식입니다.", "value");
                if (kind == value) return;
                kind = value;
                UpdateSelection();
                EventHandler handler = KindChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        internal ConnectionTypePicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Size = new Size(128, 34);
            BackColor = NasTheme.Surface;
            ForeColor = NasTheme.Text;
            Font = new Font("Malgun Gothic", 9F);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            AccessibleName = "NAS 접속 방식";
            menu = new ContextMenuStrip { Font = Font };
            directItem = new ToolStripMenuItem("NAS HTTPS 주소");
            quickConnectItem = new ToolStripMenuItem("Synology QuickConnect");
            directItem.Click += delegate { Kind = ConnectionKind.DirectHttps; };
            quickConnectItem.Click += delegate { Kind = ConnectionKind.SynologyQuickConnect; };
            menu.Items.Add(directItem);
            menu.Items.Add(quickConnectItem);
            TrayMenuStyle.Configure(menu);
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            bool direct = kind == ConnectionKind.DirectHttps;
            Text = direct ? "HTTPS 주소" : "QuickConnect";
            AccessibleDescription = direct ? "NAS의 직접 HTTPS 주소로 연결합니다." :
                "Synology QuickConnect ID 또는 주소로 연결합니다.";
            if (directItem != null) directItem.Checked = direct;
            if (quickConnectItem != null) quickConnectItem.Checked = !direct;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            ShowMenu();
        }

        private void ShowMenu()
        {
            if (!Enabled || IsDisposed) return;
            EventHandler handler = EditRequested;
            if (handler != null) handler(this, EventArgs.Empty);
            menu.Font = Font;
            menu.Show(this, new Point(0, Height + 3));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return (keyData & Keys.KeyCode) == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                ShowMenu();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96F;
            Color background = SystemInformation.HighContrast ? SystemColors.Window : NasTheme.Surface;
            Color text = SystemInformation.HighContrast ?
                (Enabled ? SystemColors.WindowText : SystemColors.GrayText) : Enabled ? NasTheme.Text : NasTheme.Muted;
            e.Graphics.Clear(background);
            if (Enabled && hover)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = NasTheme.RoundRect(new RectangleF(0, 0, Width - scale, Height - scale), 5F * scale))
                using (SolidBrush fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Highlight : NasTheme.Hover))
                    e.Graphics.FillPath(fill, shape);
                if (SystemInformation.HighContrast) text = SystemColors.HighlightText;
            }
            int inset = (int)Math.Round(8F * scale);
            int arrowWidth = (int)Math.Round(8F * scale);
            int arrowLeft = Width - (int)Math.Round(21F * scale);
            Rectangle label = new Rectangle(inset, 0, Math.Max(0, arrowLeft - inset - (int)Math.Round(6F * scale)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, label, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(text, 1.3F * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float centerY = Height / 2F;
                e.Graphics.DrawLines(pen, new PointF[] {
                    new PointF(arrowLeft, centerY - 2F * scale),
                    new PointF(arrowLeft + arrowWidth / 2F, centerY + 2F * scale),
                    new PointF(arrowLeft + arrowWidth, centerY - 2F * scale) });
            }
            using (Pen separator = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Border, scale))
                e.Graphics.DrawLine(separator, Width - scale, 5F * scale, Width - scale, Height - 5F * scale);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -inset / 2, -inset / 2), text, background);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && menu != null) menu.Dispose();
            base.Dispose(disposing);
        }
    }
}
