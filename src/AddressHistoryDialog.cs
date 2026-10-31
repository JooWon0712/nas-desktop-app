using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class AddressHistoryDialog : Form
    {
        private readonly AddressHistoryStore store;
        private readonly Label heading;
        private readonly Label subtitle;
        private readonly Label listHeading;
        private readonly Label countLabel;
        private readonly HistoryFrame listFrame;
        private readonly HistoryList historyList;
        private readonly Label emptyLabel;
        private readonly Label selectedNameLabel;
        private readonly HistoryFrame selectedNameFrame;
        private readonly TextBox selectedNameInput;
        private readonly ShellButton renameButton;
        private readonly Label addHeading;
        private readonly Label addHint;
        private readonly HistoryFrame addNameFrame;
        private readonly TextBox addNameInput;
        private readonly HistoryFrame inputFrame;
        private readonly ConnectionTypePicker typePicker;
        private readonly TextBox addressInput;
        private readonly Label statusLabel;
        private readonly ShellButton addButton;
        private readonly ShellButton removeButton;
        private readonly ShellButton useButton;
        private readonly ShellButton closeButton;
        private readonly ToolTip inputTips;
        private bool statusIsError;
        private bool layingOut;
        internal ConnectionSettings SelectedConnection { get; private set; }
        internal Func<ConnectionSettings, bool> ConnectRequested { get; set; }

        internal AddressHistoryDialog(AddressHistoryStore store, ConnectionSettings current)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Malgun Gothic", 9F);
            Text = "주소 기록";
            ClientSize = new Size(760, 650);
            MinimumSize = new Size(640, 560);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            MaximizeBox = MinimizeBox = false;
            heading = new Label { AutoSize = false, Text = "주소 기록", Font = new Font("Malgun Gothic", 18F, FontStyle.Bold) };
            subtitle = new Label { AutoSize = false, Text = "목록에서 NAS를 선택한 뒤 ‘선택 NAS 연결’을 누르세요." };
            listHeading = new Label { AutoSize = false, Text = "저장된 주소", Font = new Font("Malgun Gothic", 9F, FontStyle.Bold) };
            countLabel = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleRight };
            listFrame = new HistoryFrame { TabIndex = 0 };
            historyList = new HistoryList { Dock = DockStyle.Fill, TabIndex = 0, AccessibleName = "저장된 NAS 주소" };
            emptyLabel = new Label { Dock = DockStyle.Fill, Text = "저장된 주소가 없습니다.\n아래에서 주소를 추가할 수 있습니다.", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Malgun Gothic", 10F) };
            listFrame.Controls.Add(historyList);
            listFrame.Controls.Add(emptyLabel);
            selectedNameLabel = new Label { AutoSize = false, Text = "기존 NAS 이름", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Malgun Gothic", 9F, FontStyle.Bold) };
            selectedNameFrame = new HistoryFrame { TabIndex = 1 };
            selectedNameInput = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Malgun Gothic", 9F), MaxLength = 60, TabIndex = 0, AccessibleName = "기록에서 선택한 기존 NAS 이름", AccessibleDescription = "이름을 비우고 저장하면 호스트명으로 표시합니다." };
            selectedNameFrame.Controls.Add(selectedNameInput);
            renameButton = new ShellButton { Text = "이름 저장", TabIndex = 2, AccessibleName = "선택한 NAS 이름 저장" };
            addHeading = new Label { AutoSize = false, Text = "새 주소 추가", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Malgun Gothic", 9F, FontStyle.Bold) };
            addHint = new Label { AutoSize = false, Text = "새 NAS 이름 (선택)", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Malgun Gothic", 8.5F) };
            addNameFrame = new HistoryFrame { TabIndex = 3 };
            addNameInput = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Malgun Gothic", 9F), MaxLength = 60, TabIndex = 0, AccessibleName = "새 NAS 이름 (선택)", AccessibleDescription = "예: 집 NAS / 회사 NAS" };
            addNameFrame.Controls.Add(addNameInput);
            inputFrame = new HistoryFrame { TabIndex = 4 };
            typePicker = new ConnectionTypePicker { TabIndex = 0 };
            addressInput = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10F), TabIndex = 1, AccessibleName = "추가할 NAS 주소" };
            inputFrame.Controls.Add(typePicker);
            inputFrame.Controls.Add(addressInput);
            statusLabel = new Label { AutoSize = false, Text = "입력한 전체 주소를 이 PC에만 저장합니다.", Font = new Font("Malgun Gothic", 8.5F) };
            addButton = new ShellButton { Text = "추가", Glyph = ShellGlyph.None, TabIndex = 6, AccessibleName = "NAS 주소 기록에 추가" };
            removeButton = new ShellButton { Text = "삭제", TabIndex = 7, AccessibleName = "선택한 주소 기록 삭제" };
            closeButton = new ShellButton { Text = "닫기", DialogResult = DialogResult.Cancel, TabIndex = 8 };
            useButton = new ShellButton { Text = "선택 NAS 연결", Glyph = ShellGlyph.ArrowRight, Primary = true, TabIndex = 9, AccessibleName = "선택한 NAS에 연결" };
            inputTips = new ToolTip();
            inputTips.SetToolTip(addNameInput, "예: 집 NAS / 회사 NAS. 비워 두면 호스트명으로 표시합니다.");
            inputTips.SetToolTip(selectedNameInput, "이름을 비우고 저장하면 호스트명으로 표시합니다.");
            Controls.AddRange(new Control[] {
                heading, subtitle, listHeading, countLabel, listFrame,
                selectedNameLabel, selectedNameFrame, renameButton, addHeading, addHint, addNameFrame,
                inputFrame, addButton, statusLabel, removeButton, closeButton, useButton
            });
            addButton.Click += delegate { AddAddress(); };
            renameButton.Click += delegate { RenameSelected(); };
            removeButton.Click += delegate { RemoveSelected(); };
            useButton.Click += delegate { UseSelected(); };
            historyList.SelectedIndexChanged += delegate { UpdateActions(); };
            historyList.MouseDoubleClick += delegate(object sender, MouseEventArgs e) {
                int index = historyList.IndexFromPoint(e.Location);
                if (index >= 0) { historyList.SelectedIndex = index; UseSelected(); }
            };
            typePicker.KindChanged += delegate { ClearStatus(); };
            addressInput.TextChanged += delegate { if (statusIsError) ClearStatus(); };
            addNameInput.TextChanged += delegate { if (statusIsError) ClearStatus(); };
            selectedNameInput.TextChanged += delegate { if (statusIsError) ClearStatus(); };
            AcceptButton = useButton;
            CancelButton = closeButton;
            SeedInput(current);
            try { ReplaceRows(store.ReadEntries(), current, 0); }
            catch (Exception) { SetStatus("주소 기록을 읽지 못했습니다. 파일 접근 권한을 확인해 주세요.", true); UpdateActions(); }
            NasTheme.ApplyWindowChrome(this);
            NasTheme.Changed += OnThemeChanged;
            RefreshTheme();
            ResumeLayout(true);
        }

        private void SeedInput(ConnectionSettings current)
        {
            if (current == null) return;
            try
            {
                ConnectionSettings normalized;
                if (!ConnectionSettings.TryCreate(current.Url, current.Kind, out normalized)) return;
                typePicker.Kind = normalized.Kind;
                addressInput.Text = normalized.Url;
            }
            catch (ArgumentException) { }
            catch (System.IO.InvalidDataException) { }
        }

        private static ConnectionSettings Copy(ConnectionSettings value)
        {
            ConnectionSettings copy;
            if (value == null || !ConnectionSettings.TryCreate(value.Url, value.Kind, out copy))
                throw new ArgumentException("NAS 접속 주소를 확인해 주세요.");
            return copy;
        }

        private void ReplaceRows(IList<AddressHistoryEntry> values, ConnectionSettings selected, int fallback)
        {
            List<HistoryRow> rows = new List<HistoryRow>();
            if (values != null)
                foreach (AddressHistoryEntry value in values) rows.Add(new HistoryRow(value.Name, Copy(value.Connection)));
            int selection = -1;
            if (selected != null)
                for (int index = 0; index < rows.Count; index++)
                    if (Same(rows[index].Connection, selected)) { selection = index; break; }
            if (selection < 0 && rows.Count > 0) selection = Math.Max(0, Math.Min(fallback, rows.Count - 1));
            historyList.BeginUpdate();
            try
            {
                historyList.Items.Clear();
                foreach (HistoryRow row in rows) historyList.Items.Add(row);
                historyList.SelectedIndex = selection;
            }
            finally { historyList.EndUpdate(); }
            UpdateActions();
        }

        private static bool Same(ConnectionSettings left, ConnectionSettings right)
        {
            return left.Kind == right.Kind && String.Equals(left.Url, right.Url, StringComparison.Ordinal);
        }

        private void AddAddress()
        {
            string name;
            if (!TryReadName(addNameInput, out name)) return;
            ConnectionSettings candidate;
            if (!ConnectionSettings.TryCreate(addressInput.Text.Trim(), typePicker.Kind, out candidate))
            {
                SetStatus(typePicker.Kind == ConnectionKind.SynologyQuickConnect
                    ? "Synology QuickConnect ID 또는 quickconnect.to/ID 주소를 입력하세요."
                    : "NAS의 HTTPS 주소를 입력하세요. 주소에 아이디나 비밀번호를 포함하지 마세요.", true);
                addressInput.Focus();
                return;
            }
            try
            {
                if (String.IsNullOrEmpty(name)) store.Add(candidate);
                else store.Add(candidate, name);
                IList<AddressHistoryEntry> entries = store.ReadEntries();
                ReplaceRows(entries, candidate, 0);
                addressInput.Text = candidate.Url;
                addNameInput.Text = name;
                SetStatus("주소를 저장하고 선택했습니다. ‘선택 NAS 연결’을 누르면 접속합니다.", false);
            }
            catch (Exception) { SetStatus("주소를 저장하지 못했습니다. 파일 권한과 저장 공간을 확인해 주세요.", true); }
        }

        private void RenameSelected()
        {
            HistoryRow selected = historyList.SelectedItem as HistoryRow;
            if (selected == null) return;
            string name;
            if (!TryReadName(selectedNameInput, out name)) return;
            int index = historyList.SelectedIndex;
            try
            {
                if (!store.Rename(Copy(selected.Connection), name))
                {
                    SetStatus("선택한 주소 기록이 없습니다. 창을 다시 열어 확인해 주세요.", true);
                    return;
                }
                ReplaceRows(store.ReadEntries(), selected.Connection, index);
                SetStatus(String.IsNullOrEmpty(name) ? "이름을 지웠습니다. 목록에는 호스트명이 표시됩니다." : "NAS 이름을 저장했습니다. 현재 연결은 유지됩니다.", false);
            }
            catch (Exception) { SetStatus("이름을 저장하지 못했습니다. 파일 접근 권한을 확인해 주세요.", true); }
        }

        private bool TryReadName(TextBox input, out string name)
        {
            name = input.Text.Trim();
            bool invalid = name.Length > 60;
            foreach (char character in input.Text)
                if (Char.IsControl(character)) { invalid = true; break; }
            if (!invalid) return true;
            SetStatus("NAS 이름은 제어 문자 없이 60자 이내로 입력해 주세요.", true);
            input.Focus();
            return false;
        }

        private void RemoveSelected()
        {
            HistoryRow selected = historyList.SelectedItem as HistoryRow;
            if (selected == null) return;
            int index = historyList.SelectedIndex;
            try
            {
                store.Remove(Copy(selected.Connection));
                IList<AddressHistoryEntry> entries = store.ReadEntries();
                ReplaceRows(entries, null, index);
                SetStatus("주소 기록을 삭제했습니다. 현재 NAS 연결은 유지됩니다.", false);
            }
            catch (Exception) { SetStatus("주소 기록을 삭제하지 못했습니다. 파일 접근 권한을 확인해 주세요.", true); }
        }

        private void UseSelected()
        {
            HistoryRow selected = historyList.SelectedItem as HistoryRow;
            if (selected == null) return;
            ConnectionSettings candidate;
            try { candidate = Copy(selected.Connection); }
            catch (ArgumentException) { SetStatus("선택한 주소를 확인해 주세요.", true); return; }
            catch (System.IO.InvalidDataException) { SetStatus("선택한 주소의 접속 방식을 확인해 주세요.", true); return; }
            Func<ConnectionSettings, bool> handler = ConnectRequested;
            if (handler != null)
            {
                SelectedConnection = null;
                try
                {
                    if (!handler(Copy(candidate)))
                    {
                        if (!statusIsError) SetStatus("NAS 연결 설정을 저장하지 못했습니다. 다시 시도해 주세요.", true);
                        return;
                    }
                }
                catch (Exception) { SetStatus("NAS 연결 요청을 처리하지 못했습니다. 다시 시도해 주세요.", true); return; }
            }
            SelectedConnection = Copy(candidate);
            DialogResult = DialogResult.OK;
        }

        internal void ShowConnectionError(string message) { SetStatus(message, true); }

        private void UpdateActions()
        {
            bool hasItems = historyList.Items.Count > 0;
            historyList.Visible = hasItems;
            emptyLabel.Visible = !hasItems;
            countLabel.Text = historyList.Items.Count + " / 30";
            HistoryRow selected = historyList.SelectedItem as HistoryRow;
            bool hasSelection = selected != null;
            removeButton.Enabled = useButton.Enabled = renameButton.Enabled = selectedNameInput.Enabled = hasSelection;
            selectedNameInput.Text = hasSelection ? selected.Name : String.Empty;
        }

        private void SetStatus(string message, bool error)
        {
            statusIsError = error;
            statusLabel.Text = message;
            statusLabel.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : error ? NasTheme.Danger : NasTheme.Muted;
        }

        private void ClearStatus() { SetStatus("입력한 전체 주소를 이 PC에만 저장합니다.", false); }

        private void OnThemeChanged(object sender, EventArgs e) { if (!IsDisposed && !Disposing) RefreshTheme(); }

        private void RefreshTheme()
        {
            bool highContrast = SystemInformation.HighContrast;
            Color background = highContrast ? SystemColors.Window : NasTheme.Background;
            Color surface = highContrast ? SystemColors.Window : NasTheme.Surface;
            Color text = highContrast ? SystemColors.WindowText : NasTheme.Text;
            Color muted = highContrast ? SystemColors.WindowText : NasTheme.Muted;
            BackColor = background;
            heading.BackColor = subtitle.BackColor = listHeading.BackColor = countLabel.BackColor = selectedNameLabel.BackColor = addHeading.BackColor = addHint.BackColor = statusLabel.BackColor = background;
            heading.ForeColor = listHeading.ForeColor = selectedNameLabel.ForeColor = addHeading.ForeColor = text;
            subtitle.ForeColor = countLabel.ForeColor = addHint.ForeColor = muted;
            statusLabel.ForeColor = highContrast ? SystemColors.WindowText : statusIsError ? NasTheme.Danger : NasTheme.Muted;
            listFrame.BackColor = selectedNameFrame.BackColor = addNameFrame.BackColor = inputFrame.BackColor = historyList.BackColor = emptyLabel.BackColor = surface;
            historyList.ForeColor = text;
            emptyLabel.ForeColor = muted;
            typePicker.BackColor = addressInput.BackColor = selectedNameInput.BackColor = addNameInput.BackColor = surface;
            typePicker.ForeColor = addressInput.ForeColor = selectedNameInput.ForeColor = addNameInput.ForeColor = text;
            foreach (ShellButton button in new[] { addButton, renameButton, removeButton, closeButton, useButton }) { button.BackColor = surface; button.ForeColor = text; }
            NasTheme.RefreshWindowChrome(this);
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (layingOut || heading == null || historyList == null) return;
            layingOut = true;
            try
            {
                float scale = CurrentAutoScaleDimensions.Width / 96F;
                int pad = Px(24, scale), gap = Px(10, scale);
                int width = Math.Max(1, ClientSize.Width - pad * 2);
                heading.Bounds = new Rectangle(pad, pad - Px(3, scale), width, Px(37, scale));
                subtitle.Bounds = new Rectangle(pad + Px(2, scale), pad + Px(40, scale), width - Px(2, scale), Px(23, scale));
                listHeading.Bounds = new Rectangle(pad, pad + Px(81, scale), width - Px(90, scale), Px(21, scale));
                countLabel.Bounds = new Rectangle(ClientSize.Width - pad - Px(90, scale), listHeading.Top, Px(90, scale), listHeading.Height);
                int footer = ClientSize.Height - pad - Px(40, scale);
                statusLabel.Bounds = new Rectangle(pad, footer - Px(48, scale), width, Px(36, scale));
                int inputTop = statusLabel.Top - Px(12, scale) - Px(40, scale);
                addHeading.Bounds = new Rectangle(pad, inputTop - Px(38, scale), Px(130, scale), Px(30, scale));
                addHint.Bounds = new Rectangle(pad + Px(140, scale), addHeading.Top, Px(126, scale), addHeading.Height);
                addNameFrame.Bounds = new Rectangle(pad + Px(270, scale), addHeading.Top, Math.Max(1, width - Px(270, scale)), addHeading.Height);
                int renameTop = addHeading.Top - Px(54, scale);
                selectedNameLabel.Bounds = new Rectangle(pad, renameTop, Px(110, scale), Px(40, scale));
                renameButton.Bounds = new Rectangle(ClientSize.Width - pad - Px(110, scale), renameTop, Px(110, scale), Px(40, scale));
                selectedNameFrame.Bounds = new Rectangle(pad + Px(120, scale), renameTop,
                    Math.Max(1, renameButton.Left - gap - pad - Px(120, scale)), Px(40, scale));
                LayoutNameInput(selectedNameInput, selectedNameFrame, scale);
                LayoutNameInput(addNameInput, addNameFrame, scale);
                int listTop = pad + Px(109, scale);
                listFrame.Bounds = new Rectangle(pad, listTop, width, Math.Max(Px(90, scale), renameTop - Px(12, scale) - listTop));
                listFrame.Padding = new Padding(Px(4, scale));
                historyList.ItemHeight = Px(62, scale);
                addButton.Bounds = new Rectangle(ClientSize.Width - pad - Px(92, scale), inputTop, Px(92, scale), Px(40, scale));
                inputFrame.Bounds = new Rectangle(pad, inputTop, Math.Max(1, addButton.Left - gap - pad), Px(40, scale));
                typePicker.Bounds = new Rectangle(Px(5, scale), Px(3, scale), Px(128, scale), inputFrame.Height - Px(6, scale));
                int inputLeft = typePicker.Right + Px(12, scale);
                addressInput.Bounds = new Rectangle(inputLeft, Math.Max(0, (inputFrame.Height - addressInput.PreferredHeight) / 2),
                    Math.Max(1, inputFrame.Width - inputLeft - Px(12, scale)), addressInput.PreferredHeight);
                removeButton.Bounds = new Rectangle(pad, footer, Px(92, scale), Px(40, scale));
                useButton.Bounds = new Rectangle(ClientSize.Width - pad - Px(136, scale), footer, Px(136, scale), Px(40, scale));
                closeButton.Bounds = new Rectangle(useButton.Left - gap - Px(88, scale), footer, Px(88, scale), Px(40, scale));
                int selected = historyList.SelectedIndex;
                if (selected >= 0)
                {
                    int visibleRows = Math.Max(1, historyList.ClientSize.Height / historyList.ItemHeight);
                    if (selected < historyList.TopIndex) historyList.TopIndex = selected;
                    else if (selected >= historyList.TopIndex + visibleRows) historyList.TopIndex = selected - visibleRows + 1;
                }
            }
            finally { layingOut = false; }
        }

        private static void LayoutNameInput(TextBox input, Control frame, float scale)
        {
            int inset = Px(10, scale);
            input.Bounds = new Rectangle(inset, Math.Max(0, (frame.Height - input.PreferredHeight) / 2),
                Math.Max(1, frame.Width - inset * 2), input.PreferredHeight);
        }

        private static int Px(int value, float scale) { return Math.Max(1, (int)Math.Round(value * scale)); }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Enter && (addressInput.ContainsFocus || addNameInput.ContainsFocus)) { AddAddress(); return true; }
            if (keyData == Keys.Enter && selectedNameInput.ContainsFocus) { RenameSelected(); return true; }
            if (keyData == Keys.Enter && historyList.ContainsFocus) { UseSelected(); return true; }
            if (keyData == Keys.Delete && historyList.ContainsFocus) { RemoveSelected(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) SelectedConnection = null;
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            base.WndProc(ref message);
            if (kind == 0x001A || kind == 0x031A || kind == 0x0015) { NasTheme.RefreshSystemAppearance(); NasTheme.RefreshWindowChrome(this); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { NasTheme.Changed -= OnThemeChanged; if (inputTips != null) inputTips.Dispose(); }
            base.Dispose(disposing);
        }

        private sealed class HistoryRow
        {
            internal readonly string Name;
            internal readonly ConnectionSettings Connection;
            internal HistoryRow(string name, ConnectionSettings connection) { Name = name ?? String.Empty; Connection = connection; }
            internal string DisplayName { get { return String.IsNullOrEmpty(Name) ? new Uri(Connection.Url).Host : Name; } }
            internal string TypeLabel { get { return Connection.Kind == ConnectionKind.SynologyQuickConnect ? "Synology QuickConnect" : "HTTPS 주소"; } }
            public override string ToString() { return DisplayName + " · " + TypeLabel + " · " + Connection.Url; }
        }

        private sealed class HistoryList : ListBox
        {
            private readonly Font nameFont = new Font("Malgun Gothic", 10F, FontStyle.Bold);
            private readonly Font metadataFont = new Font("Segoe UI", 9F);
            internal HistoryList()
            {
                DrawMode = DrawMode.OwnerDrawFixed;
                BorderStyle = BorderStyle.None;
                IntegralHeight = false;
                SelectionMode = SelectionMode.One;
                Font = new Font("Segoe UI", 10F);
            }
            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= Items.Count) return;
                HistoryRow row = (HistoryRow)Items[e.Index];
                float scale = e.Graphics.DpiX / 96F;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                bool highContrast = SystemInformation.HighContrast;
                Color surface = highContrast ? SystemColors.Window : NasTheme.Surface;
                Color ink = highContrast ? selected ? SystemColors.HighlightText : SystemColors.WindowText : NasTheme.Text;
                Color muted = highContrast ? ink : selected ? NasTheme.Accent : NasTheme.Muted;
                using (SolidBrush brush = new SolidBrush(surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                RectangleF bounds = new RectangleF(Px(3, scale), e.Bounds.Top + Px(3, scale), Math.Max(1, ClientSize.Width - Px(6, scale)), e.Bounds.Height - Px(6, scale));
                if (selected)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = NasTheme.RoundRect(bounds, 7F * scale))
                    using (SolidBrush fill = new SolidBrush(highContrast ? SystemColors.Highlight : NasTheme.Blend(NasTheme.Surface, NasTheme.Accent, 0.12F)))
                    using (Pen line = new Pen(highContrast ? SystemColors.Highlight : NasTheme.Blend(NasTheme.Border, NasTheme.Accent, 0.45F), scale))
                    { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
                }
                int left = Px(13, scale), available = Math.Max(1, ClientSize.Width - left * 2);
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(e.Graphics, row.DisplayName, nameFont, new Rectangle(left, e.Bounds.Top + Px(7, scale), available, Px(24, scale)), ink, flags);
                TextRenderer.DrawText(e.Graphics, row.TypeLabel + " · " + row.Connection.Url, metadataFont, new Rectangle(left, e.Bounds.Top + Px(33, scale), available, Px(18, scale)), muted, flags);
                if (selected && Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(Rectangle.Round(bounds), -Px(3, scale), -Px(3, scale)), ink, surface);
            }
            protected override void Dispose(bool disposing) { if (disposing) { nameFont.Dispose(); metadataFont.Dispose(); } base.Dispose(disposing); }
        }

        private sealed class HistoryFrame : Panel
        {
            internal HistoryFrame() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent == null ? NasTheme.Background : Parent.BackColor);
                if (Width < 2 || Height < 2) return;
                float scale = e.Graphics.DpiX / 96F;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = NasTheme.RoundRect(new RectangleF(scale / 2F, scale / 2F, Width - scale, Height - scale), 8F * scale))
                using (SolidBrush fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Window : NasTheme.Surface))
                using (Pen line = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : NasTheme.Border, scale))
                { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
            }
        }
    }
}
