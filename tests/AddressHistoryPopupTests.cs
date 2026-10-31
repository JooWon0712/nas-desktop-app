using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NasDesk
{
    internal static class AddressHistoryPopupTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(output, "history-popup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string palette = NasTheme.Current.Key;
            ThemeMode mode = NasTheme.Mode;
            int subscriptions = ThemeSubscriptions();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            using (Form owner = new Form { Opacity = 0, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(area.Left + 40, area.Top + 40), ClientSize = new Size(680, 420), AutoScaleMode = AutoScaleMode.None })
            using (TextBox input = new TextBox { Bounds = new Rectangle(20, 20, 460, 30), Text = "https://current-nas.test/" })
            using (Panel nativeHost = new Panel { Bounds = new Rectangle(20, 100, 120, 50) })
            {
                owner.Controls.Add(input);
                owner.Controls.Add(nativeHost);
                owner.Show();
                input.Focus();
                try
                {
                    using (AddressHistoryPopup empty = new AddressHistoryPopup(new List<AddressHistoryEntry>()))
                    {
                        empty.Opacity = 0;
                        int closed = 0;
                        empty.FormClosed += delegate { closed++; };
                        empty.ShowFor(input);
                        check(empty.Visible && input.Focused && empty.SelectedEntry == null
                            && !empty.HandleEditorKey(Keys.Enter) && !empty.HandleEditorKey(Keys.Down),
                            "An empty address chooser retains editor focus and does not capture an unselected Enter");
                        empty.EmptyMessage = "주소 기록을 읽지 못했습니다. 파일 접근 권한을 확인해 주세요.";
                        SavePreview(empty, Path.Combine(directory, "history-read-error.png"));
                        check(empty.EmptyMessage.Contains("읽지 못") && !empty.HandleEditorKey(Keys.Control | Keys.V)
                            && !empty.HandleEditorKey(Keys.Delete),
                            "The chooser can show a history read failure while leaving paste and delete keys to the address editor");
                        check(empty.HandleEditorKey(Keys.Escape) && empty.IsDisposed && closed == 1
                            && !Field<bool>(empty, "filterInstalled") && ThemeSubscriptions() == subscriptions,
                            "Escape closes only the chooser and releases its message filter and theme subscription");
                    }

                    List<AddressHistoryEntry> entries = new List<AddressHistoryEntry> {
                        new AddressHistoryEntry { Name = "작업실 NAS", Url = "https://office-nas.test:5001/", Type = "https" },
                        new AddressHistoryEntry { Name = "백업 서버", Url = "https://backup-nas.quickconnect.to/", Type = "quickconnect" },
                        new AddressHistoryEntry { Name = "", Url = "https://legacy-nas.test/", Type = "https" }
                    };
                    using (AddressHistoryPopup popup = new AddressHistoryPopup(entries))
                    {
                        popup.Opacity = 0;
                        popup.ShowFor(input);
                        Control surface = Field<Control>(popup, "surface");
                        IList rows = Field<IList>(surface, "entries");
                        Rectangle list = Property<Rectangle>(surface, "ListBounds");
                        Rectangle footer = Property<Rectangle>(surface, "FooterBounds");
                        Rectangle row = RowBounds(surface, 0);
                        check(rows.Count == 3 && ((AddressHistoryEntry)rows[0]).DisplayName == "작업실 NAS"
                            && ((AddressHistoryEntry)rows[2]).DisplayName == "legacy-nas.test"
                            && list.Contains(row) && row.Bottom <= footer.Top
                            && popup.ClientRectangle.Contains(footer) && Screen.FromControl(input).WorkingArea.Contains(popup.Bounds),
                            "Named and legacy address rows fit below the input with a separate management footer");
                        check(!popup.HandleEditorKey(Keys.Enter) && !popup.HandleEditorKey(Keys.Left)
                            && !popup.HandleEditorKey(Keys.Back) && !popup.HandleEditorKey(Keys.Control | Keys.V),
                            "An initially unhighlighted chooser leaves ordinary editing and Enter submission untouched");
                        input.SelectAll();
                        input.SelectedText = "https://typed-draft.test/";
                        check(input.Focused && input.Text == "https://typed-draft.test/" && popup.SelectedEntry == null,
                            "Address text can be replaced while the nonactivating chooser is open");
                        NasTheme.SetAppearance("forest", ThemeMode.Dark);
                        popup.HandleEditorKey(Keys.Down);
                        SavePreview(popup, Path.Combine(directory, "history-popup-dark.png"));
                        NasTheme.SetMode(ThemeMode.Light);
                        SavePreview(popup, Path.Combine(directory, "history-popup-light.png"));
                        check(popup.BackColor == NasTheme.Surface && surface.BackColor == NasTheme.Surface && input.Focused,
                            "Theme changes repaint the chooser while preserving address editor focus");
                        int chosen = 0;
                        popup.EntryChosen += delegate { chosen++; };
                        popup.HandleEditorKey(Keys.Down);
                        popup.HandleEditorKey(Keys.Up);
                        check(popup.HandleEditorKey(Keys.Enter) && chosen == 1 && popup.IsDisposed
                            && popup.SelectedEntry.Name == "작업실 NAS" && popup.SelectedEntry.Url == entries[0].Url
                            && !Object.ReferenceEquals(popup.SelectedEntry, entries[0]) && input.Text == "https://typed-draft.test/",
                            "Arrow keys and Enter return a copied address entry without modifying the editor or navigating");
                    }

                    using (AddressHistoryPopup popup = new AddressHistoryPopup(entries))
                    {
                        popup.Opacity = 0;
                        popup.ShowFor(input);
                        bool focusedAtChoice = false;
                        popup.EntryChosen += delegate { focusedAtChoice = input.Focused; };
                        Control surface = Field<Control>(popup, "surface");
                        Rectangle row = RowBounds(surface, 1);
                        Point point = new Point(row.Left + 12, row.Top + row.Height / 2);
                        Mouse(surface, "OnMouseDown", point);
                        Mouse(surface, "OnMouseUp", point);
                        check(focusedAtChoice && popup.IsDisposed && popup.SelectedEntry.Name == "백업 서버"
                            && popup.SelectedEntry.Kind == ConnectionKind.SynologyQuickConnect,
                            "Clicking a named row chooses its explicit connection type without taking editor focus");
                    }

                    using (AddressHistoryPopup popup = new AddressHistoryPopup(entries))
                    {
                        popup.Opacity = 0;
                        popup.ShowFor(input);
                        int managed = 0;
                        bool closedBeforeManage = false;
                        popup.ManageRequested += delegate { managed++; closedBeforeManage = popup.IsDisposed; };
                        Control surface = Field<Control>(popup, "surface");
                        Rectangle footer = Property<Rectangle>(surface, "FooterBounds");
                        Point point = new Point(footer.Left + 20, footer.Top + footer.Height / 2);
                        Mouse(surface, "OnMouseDown", point);
                        Mouse(surface, "OnMouseUp", point);
                        check(managed == 1 && closedBeforeManage && popup.SelectedEntry == null,
                            "The management footer closes the chooser before requesting the full address manager");
                    }

                    using (AddressHistoryPopup popup = new AddressHistoryPopup(entries))
                    {
                        popup.Opacity = 0;
                        popup.ShowFor(input);
                        Control surface = Field<Control>(popup, "surface");
                        Message inside = Message.Create(surface.Handle, 0x0201, (IntPtr)1, IntPtr.Zero);
                        bool insideConsumed = Application.FilterMessage(ref inside);
                        Message editor = Message.Create(input.Handle, 0x0201, (IntPtr)1, IntPtr.Zero);
                        bool editorConsumed = Application.FilterMessage(ref editor);
                        check(!insideConsumed && !editorConsumed && popup.Visible,
                            "Chooser and address clicks pass through the application filter without dismissing the chooser");
                        NativeWindow native = new NativeWindow();
                        try
                        {
                            native.CreateHandle(new CreateParams { ClassName = "STATIC", Parent = nativeHost.Handle,
                                Style = 0x40000000, Width = 10, Height = 10 });
                            Message outside = Message.Create(native.Handle, 0x0201, (IntPtr)1, IntPtr.Zero);
                            check(!Application.FilterMessage(ref outside) && popup.IsDisposed && popup.SelectedEntry == null
                                && !Field<bool>(popup, "filterInstalled") && ThemeSubscriptions() == subscriptions,
                                "An outside native child click closes the chooser, cleans subscriptions and passes the original click through");
                        }
                        finally { native.DestroyHandle(); }
                    }

                    List<AddressHistoryEntry> many = new List<AddressHistoryEntry>();
                    for (int index = 0; index < 35; index++)
                        many.Add(new AddressHistoryEntry { Name = "NAS " + index, Url = "https://fixture-" + index + ".test/", Type = "https" });
                    using (AddressHistoryPopup popup = new AddressHistoryPopup(many))
                    {
                        popup.Opacity = 0;
                        popup.ShowFor(input);
                        Control surface = Field<Control>(popup, "surface");
                        Point wheelPoint = new Point(popup.Left + 20, popup.Top + 60);
                        long packedPoint = ((long)(ushort)wheelPoint.Y << 16) | (ushort)wheelPoint.X;
                        Message wheel = Message.Create(input.Handle, 0x020A, (IntPtr)(-120 << 16), (IntPtr)packedPoint);
                        bool scrolled = Application.FilterMessage(ref wheel);
                        check(Field<IList>(surface, "entries").Count == 30 && scrolled && Field<int>(surface, "firstIndex") > 0
                            && input.Focused && popup.SelectedEntry == null,
                            "The bounded history list scrolls with the wheel while keeping focus and leaving entries unchosen");
                        for (int index = 0; index < 30; index++) popup.HandleEditorKey(Keys.Down);
                        Rectangle last = RowBounds(surface, 29);
                        check(Property<Rectangle>(surface, "ListBounds").Contains(last)
                            && popup.HandleEditorKey(Keys.Enter) && popup.SelectedEntry.Url == many[29].Url,
                            "Keyboard navigation reveals and chooses the final stored row without exceeding thirty entries");
                    }

                    owner.Location = new Point(area.Left + 40, area.Bottom - owner.Height - 10);
                    input.Top = owner.ClientSize.Height - input.Height - 10;
                    using (AddressHistoryPopup popup = new AddressHistoryPopup(entries))
                    {
                        popup.Opacity = 0;
                        popup.ShowFor(input);
                        Rectangle anchor = input.RectangleToScreen(input.ClientRectangle);
                        check(popup.Bottom <= anchor.Top && area.Contains(popup.Bounds) && input.Focused,
                            "A chooser near the screen bottom opens above the address and stays inside the working area");
                        popup.Dismiss();
                    }
                    check(ThemeSubscriptions() == subscriptions,
                        "All chooser lifecycles release theme handlers after selection, management, dismissal and disposal");
                }
                finally { NasTheme.SetAppearance(palette, mode); owner.Close(); }
            }
        }

        private static void SavePreview(Form form, string path)
        {
            using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(path, ImageFormat.Png); }
        }

        private static void Mouse(Control control, string method, Point point)
        {
            control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        }

        private static Rectangle RowBounds(Control surface, int index)
        {
            return (Rectangle)surface.GetType().GetMethod("RowBounds", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(surface, new object[] { index });
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        private static T Property<T>(object instance, string name)
        {
            return (T)instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance, null);
        }

        private static int ThemeSubscriptions()
        {
            Delegate value = typeof(NasTheme).GetField("Changed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Delegate;
            return value == null ? 0 : value.GetInvocationList().Length;
        }
    }
}
