using System;
using System.Windows.Forms;

namespace NasDesk
{
    internal sealed class AddressInput : TextBox
    {
        private bool selectAfterClick;
        internal event EventHandler EditRequested;

        private void RequestEditing()
        {
            if (!ReadOnly) return;
            EventHandler handler = EditRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!Char.IsControl(e.KeyChar)) RequestEditing();
            base.OnKeyPress(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete
                || e.Control && (e.KeyCode == Keys.V || e.KeyCode == Keys.X || e.KeyCode == Keys.Z)
                || e.Shift && e.KeyCode == Keys.Insert)
                RequestEditing();
            base.OnKeyDown(e);
        }

        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            if (kind == 0x0102 && !Char.IsControl((char)message.WParam.ToInt32()))
                RequestEditing();
            if (kind == 0x0201 || kind == 0x0204)
            {
                bool wasLocked = ReadOnly;
                RequestEditing();
                if (kind == 0x0201) selectAfterClick = wasLocked && !ReadOnly;
            }
            // Paste/cut/clear can also come from the native context menu. IME
            // composition needs an editable control before its first character.
            if (kind == 0x0300 || kind == 0x0302 || kind == 0x0303 || kind == 0x010D)
                RequestEditing();
            base.WndProc(ref message);
            if (kind == 0x0202 && selectAfterClick)
            {
                selectAfterClick = false;
                if (!ReadOnly) SelectAll();
            }
        }
    }
}
