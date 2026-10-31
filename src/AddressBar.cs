using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class AddressBar : UserControl
    {
        private string committedUrl = String.Empty;
        private ConnectionKind committedKind = ConnectionKind.DirectHttps;
        private readonly Panel inputFrame;
        internal readonly AddressInput Input;
        internal readonly ConnectionTypePicker TypePicker;
        internal readonly AddressLockButton LockButton;
        internal readonly ShellButton ConnectButton;
        private readonly ToolTip toolTips = new ToolTip();
        private bool editing;
        private bool editingEnabled = true;
        private bool applyingAddress;
        internal event EventHandler ConnectRequested;

        internal string AddressText
        {
            get { return Input.Text; }
            set { Input.Text = value ?? String.Empty; }
        }

        internal bool IsEditing
        {
            get { return editing; }
            set { if (value) BeginEditing(true, true); else RestoreCommittedAddress(); }
        }

        internal bool EditingEnabled
        {
            get { return editingEnabled; }
            set
            {
                editingEnabled = value;
                if (!value) RestoreCommittedAddress();
                else UpdateEditingControls();
            }
        }

        internal ConnectionKind Kind
        {
            get { return TypePicker.Kind; }
            set { TypePicker.Kind = value; }
        }

        internal AddressBar()
        {
            SuspendLayout();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Height = 42;
            // The form supplies the minimum window width. A nested MinimumSize is
            // scaled twice by .NET Framework at high DPI and can exceed its row.
            BackColor = NasTheme.Background;
            Font = new Font("Malgun Gothic", 9F);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            inputFrame = new AddressInputFrame { Dock = DockStyle.Fill, BackColor = NasTheme.Background, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(12, 0, 12, 0) };
            inputFrame.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (inputFrame.Width < 2 || inputFrame.Height < 2) return;
                e.Graphics.Clear(NasTheme.Background);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath border = NasTheme.RoundRect(new RectangleF(0.5F, 0.5F, inputFrame.Width - 1F, inputFrame.Height - 1F), 7 * e.Graphics.DpiX / 96F))
                using (SolidBrush fill = new SolidBrush(NasTheme.Surface))
                using (Pen pen = new Pen(IsEditing ? NasTheme.Accent : NasTheme.Border))
                {
                    e.Graphics.FillPath(fill, border);
                    e.Graphics.DrawPath(pen, border);
                }
            };
            inputFrame.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) BeginEditing(true, true);
            };
            TypePicker = new ConnectionTypePicker { TabIndex = 0 };
            Input = new AddressInput { BorderStyle = BorderStyle.None, BackColor = NasTheme.Surface, ForeColor = NasTheme.Muted, ReadOnly = true, Font = new Font("Segoe UI", 10F), AccessibleName = "NAS 주소", TabIndex = 1 };
            Input.EditRequested += delegate { BeginEditing(false, true); };
            TypePicker.EditRequested += delegate { BeginEditing(false, false); };
            inputFrame.Controls.Add(TypePicker);
            inputFrame.Controls.Add(Input);
            inputFrame.Resize += delegate { LayoutInput(); inputFrame.Invalidate(true); };
            Input.FontChanged += delegate { LayoutInput(); };
            LockButton = new AddressLockButton { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), TabIndex = 1 };
            ConnectButton = new ShellButton { Dock = DockStyle.Fill, Margin = Padding.Empty, Text = "연결", Glyph = ShellGlyph.ArrowRight, Primary = true, Enabled = false, AccessibleName = "연결", TabIndex = 2 };
            LockButton.Click += delegate { IsEditing = !IsEditing; };
            ConnectButton.Click += delegate { RequestConnection(); };
            layout.Controls.Add(inputFrame, 0, 0);
            layout.Controls.Add(LockButton, 1, 0);
            layout.Controls.Add(ConnectButton, 2, 0);
            Controls.Add(layout);
            ResumeLayout(true);
            LayoutInput();
            UpdateEditingControls();
        }

        private void LayoutInput()
        {
            if (Input == null || TypePicker == null) return;
            float scale = CurrentAutoScaleDimensions.Width / 96F;
            int top = (int)Math.Round(3F * scale);
            TypePicker.Bounds = new Rectangle((int)Math.Round(5F * scale), top,
                (int)Math.Round(128F * scale), Math.Max(1, inputFrame.ClientSize.Height - top * 2));
            int left = TypePicker.Right + inputFrame.Padding.Left;
            Input.Size = new Size(Math.Max(1, inputFrame.ClientSize.Width - left - inputFrame.Padding.Right), Input.PreferredHeight);
            Input.Location = new Point(left, Math.Max(0, (inputFrame.ClientSize.Height - Input.Height) / 2));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutInput();
        }

        private void BeginEditing(bool focusInput, bool selectAll)
        {
            if (!editingEnabled || applyingAddress) return;
            if (!editing)
            {
                editing = true;
                UpdateEditingControls();
            }
            if (focusInput) Input.Focus();
            if (selectAll) Input.SelectAll();
        }

        private void UpdateEditingControls()
        {
            Input.ReadOnly = !IsEditing;
            Input.ForeColor = IsEditing ? NasTheme.Text : NasTheme.Muted;
            ConnectButton.Enabled = IsEditing;
            TypePicker.Enabled = editingEnabled;
            LockButton.Enabled = editingEnabled;
            LockButton.Editing = IsEditing;
            toolTips.SetToolTip(LockButton, IsEditing ? "변경을 취소하고 주소를 잠급니다 (Esc)." : "주소 편집 상태입니다. 주소를 클릭하거나 이 버튼을 누르면 편집합니다.");
            toolTips.SetToolTip(Input, IsEditing ? "Enter로 연결 · Esc로 변경 취소" : "주소를 클릭하면 편집할 수 있습니다.");
            inputFrame.Invalidate();
        }

        private void RestoreCommittedAddress()
        {
            applyingAddress = true;
            try
            {
                editing = false;
                Input.Text = committedUrl;
                Input.ClearUndo();
                TypePicker.Kind = committedKind;
                UpdateEditingControls();
            }
            finally { applyingAddress = false; }
        }

        internal void SetAddress(string value)
        {
            SetAddress(value, Kind);
        }

        internal void SetAddress(string value, ConnectionKind kind)
        {
            committedUrl = value ?? String.Empty;
            committedKind = kind;
            RestoreCommittedAddress();
        }

        internal void RefreshTheme()
        {
            BackColor = NasTheme.Background;
            inputFrame.BackColor = NasTheme.Background;
            Input.BackColor = NasTheme.Surface;
            Input.ForeColor = IsEditing ? NasTheme.Text : NasTheme.Muted;
            TypePicker.BackColor = NasTheme.Surface;
            TypePicker.ForeColor = NasTheme.Text;
            TypePicker.Invalidate();
            LockButton.BackColor = NasTheme.Surface;
            LockButton.ForeColor = NasTheme.Text;
            ConnectButton.BackColor = NasTheme.Surface;
            ConnectButton.ForeColor = NasTheme.Text;
            Invalidate(true);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) toolTips.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class AddressInputFrame : Panel
    {
        internal AddressInputFrame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }
}
