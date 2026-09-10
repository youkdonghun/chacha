using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal static class AutoDetectionTests
    {
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void SetField(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
        private static object Call(object target, string name, params object[] arguments) { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments); }
        private static void Key(CaptureOverlay overlay, Keys key)
        { Call(overlay, "ProcessCmdKey", Message.Create(IntPtr.Zero, 0x100, IntPtr.Zero, IntPtr.Zero), key); }
        private static void Mouse(CaptureOverlay overlay, string method, MouseButtons button, int x, int y)
        { Call(overlay, method, new MouseEventArgs(button, 1, x, y, 0)); }

        internal static void ManualSelectionAndPin()
        {
            Rectangle desktopBounds = new Rectangle(-250, 40, 160, 100);
            using (Bitmap desktop = new Bitmap(desktopBounds.Width, desktopBounds.Height))
            {
                for (int y = 0; y < desktop.Height; y++) for (int x = 0; x < desktop.Width; x++) desktop.SetPixel(x, y, Color.FromArgb(x, y, 120));
                using (CaptureOverlay overlay = new CaptureOverlay(desktop, desktopBounds, Rectangle.Empty, null, false, false))
                {
                    Require(!(bool)Field(overlay, "_snapshotsCollected"), "Disabled auto detection must not query other windows or UI Automation during capture construction.");
                    ((List<Rectangle>)Field(overlay, "_windows")).Add(new Rectangle(0, 0, 160, 100));
                    ((List<IntPtr>)Field(overlay, "_windowHandles")).Add(IntPtr.Zero);
                    SetField(overlay, "_snapshotsCollected", true);
                    overlay.SetElementSnapshot(new[] { new Rectangle(-230, 55, 60, 40) });
                    Mouse(overlay, "OnMouseMove", MouseButtons.None, 30, 30);
                    Require(((Rectangle)Field(overlay, "_hoverWindow")).IsEmpty, "Disabled auto detection still highlights a window or UI element.");
                    Key(overlay, Keys.Tab);
                    Call(overlay, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 30, 30, 120));
                    Call(overlay, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 30, 30, -120));
                    Require(!overlay.AutoDetectElements && ((Rectangle)Field(overlay, "_hoverWindow")).IsEmpty && ((List<Rectangle>)Field(overlay, "_hoverHierarchy")).Count == 0,
                        "Tab or wheel re-enabled automatic detection after it was disabled in settings.");
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 30, 30);
                    Mouse(overlay, "OnMouseUp", MouseButtons.Left, 30, 30);
                    Require(overlay.SelectedScreenBounds.IsEmpty && !overlay.TryPinSelection(), "A stationary click in manual mode selected a window or created a pin.");
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 30, 30);
                    Mouse(overlay, "OnMouseMove", MouseButtons.Left, 50, 40);
                    Mouse(overlay, "OnMouseUp", MouseButtons.Left, 50, 40);
                    Require(overlay.SelectedScreenBounds == new Rectangle(-220, 70, 21, 11), "Manual drag lost its exact pixels or negative desktop origin.");
                    Call(overlay, "ResetSelection");
                    overlay.AutoDetectElements = true;
                    Mouse(overlay, "OnMouseMove", MouseButtons.None, 30, 30);
                    Require((Rectangle)Field(overlay, "_hoverWindow") == new Rectangle(20, 15, 60, 40), "Re-enabling auto detection did not select the supplied UI element.");
                    Key(overlay, Keys.Tab);
                    Require(overlay.AutoDetectElements && (Rectangle)Field(overlay, "_hoverWindow") == new Rectangle(0, 0, 160, 100),
                        "Window/element switching changed the master detection preference.");
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 30, 30);
                    overlay.AutoDetectElements = false;
                    Mouse(overlay, "OnMouseUp", MouseButtons.Left, 30, 30);
                    Require(overlay.SelectedScreenBounds.IsEmpty && ((Rectangle)Field(overlay, "_clickCandidate")).IsEmpty,
                        "Disabling detection retained a stale click-to-window selection.");
                    overlay.SetSelection(new Rectangle(-210, 60, 30, 20));
                    overlay.AutoDetectElements = false;
                    Require(overlay.SelectedScreenBounds == new Rectangle(-210, 60, 30, 20), "Disabling detection erased a user-selected region.");
                }
                CaptureResult result = null;
                try
                {
                    using (CaptureOverlay overlay = new CaptureOverlay(desktop, desktopBounds, Rectangle.Empty, null, false, false))
                    {
                        int completed = 0;
                        overlay.Completed += delegate(CaptureResult current)
                        {
                            result = current; completed++;
                            Require(!overlay.TryPinSelection(), "A re-entrant pin command completed the same capture twice.");
                        };
                        Require(!overlay.TryPinSelection() && completed == 0, "Pin without a region must be a no-op.");
                        Rectangle selected = new Rectangle(-210, 60, 30, 20);
                        overlay.SetSelection(selected);
                        Require(overlay.TryPinSelection(), "The configured pin action could not complete a valid selection.");
                        Require(completed == 1 && result != null && result.Outcome == CaptureOutcome.Pin && result.ScreenBounds == selected,
                            "Pin selection did not transfer exactly one capture with its screen coordinates.");
                        Require(result.Image.Size == selected.Size && result.Image.GetPixel(5, 6).ToArgb() == desktop.GetPixel(45, 26).ToArgb(),
                            "Pin selection returned other pixels instead of the selected frozen capture.");
                        Require(!overlay.TryPinSelection() && completed == 1, "Pin repeated after capture completion created another result.");
                    }
                }
                finally { if (result != null) result.Dispose(); }
                VerifyConfiguredPinKey(desktop, desktopBounds, "Ctrl+F9", Keys.Control | Keys.F9);
                VerifyConfiguredPinKey(desktop, desktopBounds, "", Keys.Control | Keys.T);
            }
        }

        private static void VerifyConfiguredPinKey(Bitmap desktop, Rectangle bounds, string configured, Keys completionKey)
        {
            using (CaptureOverlay overlay = new CaptureOverlay(desktop, bounds, Rectangle.Empty, null, false, false))
            {
                overlay.FloatingHotkey = configured;
                int completed = 0;
                overlay.Completed += delegate(CaptureResult result) { completed++; Require(result.Outcome == CaptureOutcome.Pin, "Configured floating key performed a different capture action."); result.Dispose(); };
                Key(overlay, Keys.Control | Keys.F9);
                Require(overlay.SelectedScreenBounds.IsEmpty && completed == 0, "Configured key promoted hover or empty state into a capture.");
                overlay.SetSelection(new Rectangle(bounds.X + 15, bounds.Y + 10, 30, 20));
                Key(overlay, Keys.F3); Key(overlay, Keys.F9);
                Require(completed == 0, "An unbound/default key or wrong modifiers still floated the capture.");
                Key(overlay, completionKey);
                Require(completed == 1, "Configured floating key or the retained Ctrl+T local action did not complete once.");
            }
        }
    }
}
