using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal static class PinCloseTests
    {
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private static bool Key(Control target, Keys keys)
        {
            MethodInfo method = target.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] arguments = { Message.Create(target.Handle, 0x100, (IntPtr)(keys & Keys.KeyCode), IntPtr.Zero), keys };
            return (bool)method.Invoke(target, arguments);
        }

        private static object Field(object target, string name)
        { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }

        internal static void Run()
        {
            using (Bitmap source = new Bitmap(37, 23))
            {
                source.SetPixel(12, 9, Color.FromArgb(19, 87, 203));
                using (PinForm pin = new PinForm(source))
                {
                    int hidden = 0;
                    pin.HiddenByUser += delegate { hidden++; };
                    Require(pin.CloseHotkey == "Esc", "The default local close binding changed.");
                    Require(Key(pin, Keys.Escape) && pin.ClosedByUser && hidden == 1 && !pin.IsDisposed,
                        "Default Esc must close recoverably.");
                    using (Bitmap retained = pin.ExportImage())
                        Require(retained.GetPixel(12, 9).ToArgb() == source.GetPixel(12, 9).ToArgb(), "Recoverable closing lost original image pixels.");

                    pin.ClosedByUser = false;
                    pin.CloseHotkey = "Ctrl+Shift+Q";
                    Require(!Key(pin, Keys.Escape) && !pin.ClosedByUser && hidden == 1,
                        "Changing the binding left the previous Esc command enabled.");
                    Require(!Key(pin, Keys.Control | Keys.Q) && !pin.ClosedByUser,
                        "Local close matching ignored Shift.");
                    Require(Key(pin, Keys.Control | Keys.Shift | Keys.Q) && pin.ClosedByUser && hidden == 2 && !pin.IsDisposed,
                        "The custom local binding did not close recoverably.");
                    ToolStripMenuItem close = (ToolStripMenuItem)Field(pin, "closeMenu");
                    Require(close.ShortcutKeyDisplayString == "Ctrl+Shift+Q / Ctrl+W", "The pin menu shows stale close-key instructions.");

                    pin.ClosedByUser = false;
                    pin.CloseHotkey = "";
                    Require(!Key(pin, Keys.Escape) && !pin.ClosedByUser, "Clearing the close binding did not disable Esc.");
                    Require(!Key(pin, Keys.Control | Keys.Shift | Keys.Q) && !pin.ClosedByUser, "Clearing the binding left the custom chord enabled.");
                    Require(close.ShortcutKeyDisplayString == "Ctrl+W", "An unbound shortcut still appears in the context menu.");
                    Require(Key(pin, Keys.Control | Keys.W) && pin.ClosedByUser && hidden == 3, "Fixed Ctrl+W no longer closes an unbound pin.");

                    pin.ClosedByUser = false;
                    pin.CloseHotkey = "Esc";
                    Require(Key(pin, Keys.Escape) && pin.ClosedByUser && hidden == 4, "The persisted Esc alias did not apply.");
                    pin.ClosedByUser = false;
                    pin.CloseHotkey = "Ctrl+Esc";
                    Require(Key(pin, Keys.Control | Keys.Escape) && pin.ClosedByUser && hidden == 5, "Esc alias inside a chord did not apply.");
                    pin.ClosedByUser = false;
                    pin.CloseHotkey = null;
                    Require(pin.CloseHotkey == "" && !Key(pin, Keys.Escape) && !pin.ClosedByUser, "A null optional binding restored Esc unexpectedly.");
                }

                using (PinForm pin = new PinForm(source))
                {
                    Type toolbarType = typeof(PinForm).GetNestedType("FloatingToolbarForm", BindingFlags.NonPublic);
                    using (Form toolbar = (Form)Activator.CreateInstance(toolbarType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new object[] { pin }, null))
                    {
                        int hidden = 0;
                        pin.HiddenByUser += delegate { hidden++; };
                        pin.CloseHotkey = "Ctrl+Shift+Q";
                        Require(Key(toolbar, Keys.Control | Keys.Shift | Keys.Q) && pin.ClosedByUser && hidden == 1 && !pin.IsDisposed,
                            "The floating toolbar did not route the close chord to its image.");
                        pin.ClosedByUser = false;
                        pin.CloseHotkey = "";
                        Require(!Key(toolbar, Keys.Escape) && !pin.ClosedByUser, "Toolbar forwarding restored disabled Esc.");
                        Require(Key(toolbar, Keys.Control | Keys.W) && pin.ClosedByUser && hidden == 2, "Toolbar Ctrl+W did not close the source image.");
                        Require(!toolbar.IsDisposed, "Recoverable image close unexpectedly destroyed the toolbar test object.");
                    }
                }

                using (PinForm pin = new PinForm(source))
                {
                    pin.CloseHotkey = "Shift+Esc";
                    int hidden = 0, closed = 0;
                    pin.HiddenByUser += delegate { hidden++; };
                    pin.FormClosed += delegate { closed++; };
                    Require(Key(pin, Keys.Shift | Keys.Escape) && pin.IsDisposed && closed == 1 && hidden == 0,
                        "The fixed permanent-delete command lost precedence over local settings.");
                }
            }
        }
    }
}
