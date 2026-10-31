using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class AddressBar : UserControl
    {
        private string committedUrl = String.Empty;
        private readonly Panel inputFrame;
        internal readonly TextBox Input;
        internal readonly CheckBox EditSwitch;
        internal readonly ShellButton ConnectButton;
        internal event EventHandler ConnectRequested;

        internal string AddressText
        {
            get { return Input.Text; }
            set { Input.Text = value ?? String.Empty; }
        }

        internal bool IsEditing
        {
            get { return EditSwitch.Checked; }
            set { EditSwitch.Checked = value; }
        }

        internal AddressBar()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Height = 42;
            MinimumSize = new Size(300, 38);
            BackColor = NasTheme.Background;
            Font = new Font("Malgun Gothic", 9F);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            inputFrame = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Surface, Margin = new Padding(0, 1, 8, 1), Padding = new Padding(12, 0, 12, 0) };
            inputFrame.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (inputFrame.Width < 2 || inputFrame.Height < 2) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath border = NasTheme.RoundRect(new RectangleF(0.5F, 0.5F, inputFrame.Width - 1F, inputFrame.Height - 1F), 7))
                using (Pen pen = new Pen(IsEditing ? NasTheme.Accent : NasTheme.Border)) e.Graphics.DrawPath(pen, border);
            };
            Input = new TextBox { BorderStyle = BorderStyle.None, BackColor = NasTheme.Surface, ForeColor = NasTheme.Muted, ReadOnly = true, Font = new Font("Segoe UI", 10F), AccessibleName = "NAS 주소", TabIndex = 0 };
            inputFrame.Controls.Add(Input);
            inputFrame.Resize += delegate { LayoutInput(); };
            Input.FontChanged += delegate { LayoutInput(); };
            EditSwitch = new AddressEditSwitch { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 8, 1), Text = "주소 수정", AccessibleName = "주소 수정", TabIndex = 1 };
            ConnectButton = new ShellButton { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1), Text = "연결", Glyph = ShellGlyph.ArrowRight, Primary = true, Enabled = false, AccessibleName = "연결", TabIndex = 2 };
            EditSwitch.CheckedChanged += delegate { ApplyEditing(); };
            ConnectButton.Click += delegate { RequestConnection(); };
            layout.Controls.Add(inputFrame, 0, 0);
            layout.Controls.Add(EditSwitch, 1, 0);
            layout.Controls.Add(ConnectButton, 2, 0);
            Controls.Add(layout);
            ResumeLayout(true);
            LayoutInput();
        }

        private void LayoutInput()
        {
            if (Input == null) return;
            Input.SetBounds(inputFrame.Padding.Left, Math.Max(0, (inputFrame.ClientSize.Height - Input.PreferredHeight) / 2), Math.Max(1, inputFrame.ClientSize.Width - inputFrame.Padding.Horizontal), Input.PreferredHeight);
        }

        private void ApplyEditing()
        {
            Input.ReadOnly = !IsEditing;
            Input.ForeColor = IsEditing ? NasTheme.Text : NasTheme.Muted;
            ConnectButton.Enabled = IsEditing;
            if (IsEditing) { Input.Focus(); Input.SelectAll(); }
            else Input.Text = committedUrl;
            inputFrame.Invalidate();
        }

        internal void SetAddress(string value)
        {
            committedUrl = value ?? String.Empty;
            EditSwitch.Checked = false;
            Input.Text = committedUrl;
            ApplyEditing();
        }

        private void RequestConnection()
        {
            if (!IsEditing) return;
            EventHandler handler = ConnectRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape && IsEditing) { IsEditing = false; return true; }
            if (keyData == Keys.Enter && IsEditing && Input.ContainsFocus) { RequestConnection(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
    }

    internal sealed class AddressEditSwitch : CheckBox
    {
        internal AddressEditSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
            BackColor = NasTheme.Background;
            ForeColor = NasTheme.Text;
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            float scale = e.Graphics.DpiX / 96F;
            int toggleWidth = (int)(42 * scale), toggleHeight = (int)(22 * scale);
            Rectangle toggle = new Rectangle(Math.Max(0, Width - toggleWidth - (int)(2 * scale)), (Height - toggleHeight) / 2, toggleWidth, toggleHeight);
            Color active = SystemInformation.HighContrast ? SystemColors.Highlight : NasTheme.Accent;
            Color inactive = SystemInformation.HighContrast ? SystemColors.ControlDark : NasTheme.Border;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = NasTheme.RoundRect(toggle, toggleHeight / 2F))
            using (SolidBrush track = new SolidBrush(Checked ? active : inactive)) e.Graphics.FillPath(track, shape);
            int inset = Math.Max(2, (int)(3 * scale)), knob = toggleHeight - inset * 2;
            using (SolidBrush dot = new SolidBrush(Checked ? NasTheme.Background : NasTheme.Text))
                e.Graphics.FillEllipse(dot, Checked ? toggle.Right - inset - knob : toggle.Left + inset, toggle.Top + inset, knob, knob);
            Rectangle label = new Rectangle(0, 0, Math.Max(0, toggle.Left - (int)(7 * scale)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, label, Enabled ? ForeColor : NasTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), ForeColor, BackColor);
        }
    }
}
