using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal static class CaptureRegressionTests
    {
        private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Invoke(object value, string name, params object[] args)
        { return value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args); }
        private static void Press(HotkeyCaptureBox box, Keys key)
        { Invoke(box, "ProcessCmdKey", Message.Create(box.Handle, 0x100, (IntPtr)((int)key & 65535), IntPtr.Zero), key); }
        internal static void Recorder()
        {
            using (HotkeyCaptureBox box = new HotkeyCaptureBox())
            {
                box.Hotkey = "F1"; Invoke(box, "OnGotFocus", EventArgs.Empty);
                Press(box, Keys.Control | Keys.ControlKey);
                Assert(box.Hotkey == "F1" && box.Text == "Ctrl+…", "Modifier preview changed the saved chord.");
                Press(box, Keys.Control | Keys.Alt | Keys.K);
                Assert(box.Hotkey == "Ctrl+Alt+K", "Actual chord was not recorded.");
                Invoke(box, "OnKeyUp", new KeyEventArgs(Keys.ControlKey));
                Assert(box.Hotkey == "Ctrl+Alt+K", "Key release changed the chord.");
                Press(box, Keys.Escape); Assert(box.Hotkey == "F1", "Escape did not revert focus-entry value.");
                Press(box, Keys.Enter); Assert(box.Hotkey == "Enter", "Enter must be recordable.");
                Press(box, Keys.Back); Assert(box.Hotkey == "Back", "Backspace must be recordable.");
                Press(box, Keys.Delete); Assert(box.Hotkey == "Delete", "Delete must be recordable.");
                Press(box, Keys.Tab); Assert(box.Hotkey == "Delete", "Navigation Tab changed the binding.");
                Press(box, Keys.Shift | Keys.Tab); Assert(box.Hotkey == "Delete", "Navigation Shift+Tab changed the binding.");
                box.Hotkey = ""; Invoke(box, "OnGotFocus", EventArgs.Empty);
                Press(box, Keys.Control | Keys.ControlKey); Invoke(box, "OnLostFocus", EventArgs.Empty);
                Assert(box.Hotkey == "" && box.Text == "", "An unbound field gained a modifier-only shortcut.");
                Invoke(box, "OnGotFocus", EventArgs.Empty);
                Press(box, Keys.F9); Press(box, Keys.Escape); Assert(box.Hotkey == "", "Escape must also restore an empty binding.");
            }
            using (HotkeyCaptureBox box = new HotkeyCaptureBox())
            {
                // The native hook runs before the OS updates modifier state. Feed its
                // actual left/right key transitions rather than a preassembled chord.
                Keys data;
                Assert(!box.TranslateKeyboardInput(Keys.LControlKey, true, out data) && data == (Keys.Control | Keys.LControlKey), "Left Ctrl was not tracked before asynchronous state update.");
                Assert(!box.TranslateKeyboardInput(Keys.RShiftKey, true, out data) && (data & Keys.Modifiers) == (Keys.Control | Keys.Shift), "Right Shift was not added to the chord.");
                Assert(box.TranslateKeyboardInput(Keys.F1, true, out data) && data == (Keys.Control | Keys.Shift | Keys.F1), "Registered F1 could not be captured before normal window dispatch.");
                Press(box, data);
                Assert(box.Hotkey == "Ctrl+Shift+F1", "Native recorded chord did not reach the stored value.");
                Assert(box.TranslateKeyboardInput(Keys.F1, false, out data), "Captured key-up leaked into the focused TextBox.");
                box.TranslateKeyboardInput(Keys.LControlKey, false, out data);
                box.TranslateKeyboardInput(Keys.RShiftKey, false, out data);
                Assert(box.TranslateKeyboardInput(Keys.F3, true, out data) && data == Keys.F3, "Released modifiers remained stuck on the next shortcut.");
                box.TranslateKeyboardInput(Keys.F3, false, out data);
                box.TranslateKeyboardInput(Keys.LControlKey, true, out data);
                box.TranslateKeyboardInput(Keys.RControlKey, true, out data);
                box.TranslateKeyboardInput(Keys.LControlKey, false, out data);
                Assert(box.TranslateKeyboardInput(Keys.K, true, out data) && data == (Keys.Control | Keys.K), "Releasing left Ctrl cleared a still-held right Ctrl.");
                box.TranslateKeyboardInput(Keys.K, false, out data);
                box.TranslateKeyboardInput(Keys.RControlKey, false, out data);
                Assert(!box.TranslateKeyboardInput(Keys.Tab, true, out data), "The hook swallowed navigation Tab.");
                box.TranslateKeyboardInput(Keys.Tab, false, out data);
                box.TranslateKeyboardInput(Keys.LShiftKey, true, out data);
                Assert(!box.TranslateKeyboardInput(Keys.Tab, true, out data), "The hook swallowed navigation Shift+Tab.");
                box.TranslateKeyboardInput(Keys.Tab, false, out data);
                box.TranslateKeyboardInput(Keys.LShiftKey, false, out data);
                Assert(!box.TranslateKeyboardInput(Keys.LWin, true, out data), "The recorder swallowed the Windows key.");
                Assert(!box.TranslateKeyboardInput(Keys.E, true, out data), "The recorder swallowed a Windows system shortcut.");
                box.TranslateKeyboardInput(Keys.E, false, out data);
                box.TranslateKeyboardInput(Keys.LWin, false, out data);
                Assert(box.TranslateKeyboardInput(Keys.Enter, true, out data), "Enter was routed to dialog Save instead of recording.");
                box.TranslateKeyboardInput(Keys.Enter, false, out data);
            }
            for (int code = 0; code <= 255; code++) for (int mask = 0; mask < 8; mask++)
            {
                Keys chord = (Keys)code | ((mask & 1) != 0 ? Keys.Control : 0) | ((mask & 2) != 0 ? Keys.Alt : 0) | ((mask & 4) != 0 ? Keys.Shift : 0);
                string text; if (!HotkeyCaptureBox.TryFormat(chord, out text)) continue;
                uint modifiers, key;
                Assert(HotkeyWindow.Parse(text, out modifiers, out key) && key == code && modifiers == (uint)(((mask & 1) != 0 ? 2 : 0) | ((mask & 2) != 0 ? 1 : 0) | ((mask & 4) != 0 ? 4 : 0)), "Recorded chord does not round-trip: " + text);
            }
        }
        internal static void OptionalSettings(string directory)
        {
            Storage storage = new Storage(directory);
            storage.Settings.CaptureHotkey = storage.Settings.PinHotkey = storage.Settings.ToggleHotkey = storage.Settings.ClickThroughHotkey = storage.Settings.SwitchGroupHotkey = "";
            storage.Settings.AutoFloatCapture = false;
            storage.SaveSettings();
            AppSettings settings = new Storage(directory).Settings;
            Assert(settings.CaptureHotkey == "" && settings.PinHotkey == "" && settings.ToggleHotkey == "" && settings.ClickThroughHotkey == "" && settings.SwitchGroupHotkey == "", "Optional bindings did not survive restart.");
            Assert(!settings.AutoFloatCapture, "Disabled automatic floating did not survive restart.");
            File.WriteAllText(Path.Combine(directory, "settings.xml"), "<AppSettings><CaptureHotkey></CaptureHotkey></AppSettings>");
            settings = new Storage(directory).Settings;
            Assert(settings.CaptureHotkey == "" && settings.AutoFloatCapture, "Old profiles must preserve unbound keys and default automatic floating on.");
            using (HotkeyWindow window = new HotkeyWindow())
            {
                int calls = 0; window.Pressed += delegate { calls++; };
                AppSettings empty = new AppSettings { CaptureHotkey = "", PinHotkey = null, ToggleHotkey = " ", ClickThroughHotkey = "", SwitchGroupHotkey = "" };
                Assert(window.Register(empty) == "", "Unbound hotkeys were reported as registration errors.");
                window.Suspend(); Assert(window.IsSuspended, "Hotkey suspension not set.");
                Invoke(window, "WndProc", Message.Create(window.Handle, 0x312, new IntPtr(1), IntPtr.Zero));
                Assert(calls == 0, "Queued global shortcut fired during recording.");
                Assert(window.Register(new AppSettings()) == "" && window.IsSuspended, "Saving settings resumed global shortcuts too early.");
                Assert(window.Resume(empty) == "" && !window.IsSuspended, "Optional bindings did not resume cleanly.");
            }
            string error;
            Assert(HotkeyWindow.CheckAvailability("", out error) && error == "", "Cleared shortcuts were treated as unavailable.");
            Assert(!HotkeyWindow.CheckAvailability("Ctrl+F12", out error) && error.Contains("F12"), "Windows reserved F12 was reported as ready to apply.");
            foreach (string invalid in new[] { "LControlKey", "RControlKey", "LShiftKey", "RShiftKey", "LMenu", "RMenu", "LWin", "RWin" })
            {
                uint modifiers, key;
                Assert(!HotkeyWindow.Parse(invalid, out modifiers, out key), "Modifier-only input was accepted as a global shortcut: " + invalid);
            }
            using (HotkeyWindow owner = new HotkeyWindow())
            {
                string candidate = null;
                for (int key = (int)Keys.F13; key <= (int)Keys.F24; key++)
                {
                    string value = "Ctrl+Alt+Shift+" + ((Keys)key).ToString();
                    if (!HotkeyWindow.CheckAvailability(value, out error)) continue;
                    AppSettings reservation = new AppSettings { CaptureHotkey = value, PinHotkey = "", ToggleHotkey = "", ClickThroughHotkey = "", SwitchGroupHotkey = "" };
                    if (owner.Register(reservation) == "") { candidate = value; break; }
                }
                Assert(candidate != null, "No unclaimed shortcut was available for registration conflict regression.");
                Assert(!HotkeyWindow.CheckAvailability(candidate, out error) && error.Contains(candidate), "Already registered shortcut was reported as available.");
                owner.Suspend();
                Assert(HotkeyWindow.CheckAvailability(candidate, out error), "The availability probe leaked its temporary registration.");
                Assert(HotkeyWindow.CheckAvailability(candidate, out error), "A second availability probe could not reuse the released shortcut.");
            }
        }
        internal static void Pixels()
        {
            IntPtr memory = Marshal.AllocHGlobal(24);
            try
            {
                byte[] input = { 3, 2, 1, 0, 6, 5, 4, 0, 99, 99, 99, 99, 9, 8, 7, 0, 12, 11, 10, 0, 99, 99, 99, 99 };
                Marshal.Copy(input, 0, memory, input.Length);
                using (Bitmap image = DesktopCapture.ReadPixels(memory, 2, 2, 12, 4))
                {
                    Assert(image.GetPixel(0, 0).ToArgb() == Color.FromArgb(1, 2, 3).ToArgb(), "RGB with zero driver alpha was lost.");
                    Assert(image.GetPixel(1, 1).ToArgb() == Color.FromArgb(10, 11, 12).ToArgb(), "Row pitch/opaque alpha changed.");
                }
                using (Bitmap image = DesktopCapture.ReadPixels(IntPtr.Add(memory, 12), 2, 2, -12, 4))
                    Assert(image.GetPixel(0, 0).R == 7 && image.GetPixel(0, 1).R == 1, "Negative row stride orientation changed.");
                byte[] rgb = { 30, 20, 10, 60, 50, 40, 99, 99, 90, 80, 70, 120, 110, 100, 99, 99 };
                Marshal.Copy(rgb, 0, memory, rgb.Length);
                using (Bitmap image = DesktopCapture.ReadPixels(memory, 2, 2, 8, 3))
                    Assert(image.GetPixel(1, 1).ToArgb() == Color.FromArgb(100, 110, 120).ToArgb(), "24-bit DIB padding corrupted screen pixels.");
                bool rejected = false;
                try { using (Bitmap invalid = DesktopCapture.ReadPixels(memory, 2, 2, 3, 4)) { } } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Invalid row pitch was accepted.");
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
    }
}
