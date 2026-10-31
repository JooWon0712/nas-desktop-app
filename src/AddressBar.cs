using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class AddressBar : UserControl
    {
        private readonly Panel inputFrame;
        internal readonly TextBox Input;
        internal readonly ConnectionTypePicker TypePicker;
        internal readonly ShellButton HistoryButton;
        private readonly ToolTip toolTips = new ToolTip();
        private bool historyEnabled = true;
        internal event EventHandler HistoryRequested;

        internal string AddressText { get { return Input.Text; } }
        internal ConnectionKind Kind { get { return TypePicker.Kind; } }
        internal bool HistoryEnabled
        {
            get { return historyEnabled; }
            set { historyEnabled = value; UpdateHistoryControls(); }
        }

        internal AddressBar()
        {
            SuspendLayout();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Height = 42;
            BackColor = NasTheme.Background;
            Font = new Font("Malgun Gothic", 9F);
            inputFrame = new AddressInputFrame { BackColor = NasTheme.Background, Padding = new Padding(12, 0, 12, 0), TabIndex = 0 };
            inputFrame.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (inputFrame.Width < 2 || inputFrame.Height < 2) return;
                e.Graphics.Clear(NasTheme.Background);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = NasTheme.RoundRect(new RectangleF(0.5F, 0.5F, inputFrame.Width - 1F, inputFrame.Height - 1F), 7F * e.Graphics.DpiX / 96F))
                using (SolidBrush fill = new SolidBrush(NasTheme.Surface))
                using (Pen border = new Pen(NasTheme.Border))
                {
                    e.Graphics.FillPath(fill, shape);
                    e.Graphics.DrawPath(border, shape);
                }
            };
            inputFrame.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) RequestHistory(); };
            TypePicker = new ConnectionTypePicker { TabIndex = 0, Enabled = false, TabStop = false };
            Input = new TextBox {
                BorderStyle = BorderStyle.None, BackColor = NasTheme.Surface, ForeColor = NasTheme.Muted,
                ReadOnly = true, Font = new Font("Segoe UI", 10F), AccessibleName = "NAS 주소 (읽기 전용)", TabIndex = 1
            };
            Input.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) RequestHistory(); };
            inputFrame.Controls.Add(TypePicker);
            inputFrame.Controls.Add(Input);
            inputFrame.Resize += delegate { LayoutInput(); inputFrame.Invalidate(true); };
            Input.FontChanged += delegate { LayoutInput(); };
            HistoryButton = new ShellButton { Text = "주소 기록", Glyph = ShellGlyph.History, AccessibleName = "NAS 주소 기록 관리", TabIndex = 1 };
            HistoryButton.Click += delegate { RequestHistory(); };
            Controls.AddRange(new Control[] { inputFrame, HistoryButton });
            ResumeLayout(true);
            UpdateHistoryControls();
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
            if (HistoryButton == null) return;
            float scale = CurrentAutoScaleDimensions.Width / 96F;
            int historyWidth = (int)Math.Round(104F * scale);
            int gap = (int)Math.Round(8F * scale);
            HistoryButton.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - historyWidth), 0, historyWidth, ClientSize.Height);
            inputFrame.Bounds = new Rectangle(0, 0, HistoryEnabled ? Math.Max(1, HistoryButton.Left - gap) : ClientSize.Width, ClientSize.Height);
            LayoutInput();
        }

        private void UpdateHistoryControls()
        {
            Input.ReadOnly = true;
            TypePicker.Enabled = false;
            HistoryButton.Visible = HistoryButton.Enabled = HistoryEnabled;
            Input.Cursor = HistoryEnabled ? Cursors.Hand : Cursors.IBeam;
            toolTips.SetToolTip(Input, HistoryEnabled ? "읽기 전용 주소입니다. 클릭하거나 Ctrl+L로 주소 기록에서 연결할 NAS를 선택하세요." : "이 NAS 창의 주소는 읽기 전용입니다.");
            toolTips.SetToolTip(HistoryButton, "주소와 이름 입력 · 기록 관리 · NAS 연결");
            PerformLayout();
            inputFrame.Invalidate();
        }

        internal void SetAddress(string value) { SetAddress(value, Kind); }
        internal void SetAddress(string value, ConnectionKind kind)
        {
            Input.Text = value ?? String.Empty;
            Input.ClearUndo();
            TypePicker.Kind = kind;
            UpdateHistoryControls();
        }

        internal void RefreshTheme()
        {
            BackColor = inputFrame.BackColor = NasTheme.Background;
            Input.BackColor = TypePicker.BackColor = HistoryButton.BackColor = NasTheme.Surface;
            Input.ForeColor = NasTheme.Muted;
            TypePicker.ForeColor = HistoryButton.ForeColor = NasTheme.Text;
            UpdateHistoryControls();
            Invalidate(true);
        }

        private void RequestHistory()
        {
            if (!HistoryEnabled) return;
            EventHandler handler = HistoryRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (HistoryEnabled && (keyData == (Keys.Control | Keys.L)
                || Input.ContainsFocus && (keyData == Keys.Enter || keyData == (Keys.Alt | Keys.Down))))
            {
                RequestHistory();
                return true;
            }
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
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }
}
