using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ChachaCapture;

// Explicit desktop QA: shows a synthetic canvas, writes a synthetic clipboard image,
// and captures that canvas locally. This is intentionally separate from --self-test.
internal static class DesktopIntegration
{
    private static int checks;
    private static readonly List<string> notes = new List<string>();
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
    private static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    private static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
    private static void Pump(int milliseconds) { Stopwatch clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
    private static void Complete(CaptureApplication app, Bitmap image, CaptureOutcome outcome, Rectangle bounds)
    { using (CaptureResult result = new CaptureResult { Image = (Bitmap)image.Clone(), Outcome = outcome, ScreenBounds = bounds }) Call(app, "CompleteCapture", result, null); }
    private static void ClosePins(CaptureApplication app) { foreach (PinForm pin in app.AllPins) pin.Close(); }
    private static void Key(Form form, Keys keys)
    { Check((bool)Call(form, "ProcessCmdKey", Message.Create(IntPtr.Zero, 0x100, IntPtr.Zero, IntPtr.Zero), keys), "Shortcut was not handled: " + keys); }
    private static bool SamePixels(Bitmap expected, Bitmap actual)
    {
        if (actual == null || expected.Size != actual.Size) return false;
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb()) return false;
        return true;
    }
    private static void CheckClipboard(Bitmap expected, string message)
    { using (Image image = Clipboard.GetImage()) using (Bitmap actual = image == null ? null : new Bitmap(image)) Check(SamePixels(expected, actual), message); }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
    private static void CopyCompletionWorkflow(CaptureApplication app, string root)
    {
        app.Store.Settings.AutoFloatCapture = true;
        app.Store.Settings.AutoSave = false;
        app.Store.SaveSettings();
        Check(new Storage(root).Settings.AutoFloatCapture, "The Ctrl+C regression did not retain an existing enabled automatic-floating setting.");
        Rectangle desktopBounds = new Rectangle(0, 0, 1920, 1080);
        Rectangle area = new Rectangle(420, 300, 160, 100);
        using (Bitmap image = new Bitmap(area.Width, area.Height))
        using (Bitmap desktop = new Bitmap(desktopBounds.Width, desktopBounds.Height))
        {
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) image.SetPixel(x, y, Color.FromArgb((x * 3) % 256, (y * 7) % 256, (x + y) % 256));
            using (Graphics g = Graphics.FromImage(desktop)) { g.Clear(Color.DarkSlateBlue); g.DrawImageUnscaled(image, area.Location); }
            foreach (Keys command in new[] { Keys.Control | Keys.C, Keys.Enter, Keys.Control | Keys.T })
            {
                app.PinImage(image, new Rectangle(640, 300, image.Width, image.Height));
                PinForm unrelated = app.AllPins.Single();
                CaptureOutcome expectedOutcome = command == (Keys.Control | Keys.C) ? CaptureOutcome.CopyAndClose : command == Keys.Enter ? CaptureOutcome.Copy : CaptureOutcome.Pin;
                int completed = 0;
                if (expectedOutcome == CaptureOutcome.Pin) Clipboard.SetText("Chacha explicit floating retains clipboard");
                using (CaptureOverlay selection = new CaptureOverlay(desktop, desktopBounds, Rectangle.Empty, null, false, false))
                {
                    selection.AutoFloatCapture = true;
                    selection.FloatingHotkey = "";
                    selection.SetSelection(area);
                    selection.Completed += delegate(CaptureResult result)
                    {
                        completed++;
                        using (result)
                        {
                            Check(result.Outcome == expectedOutcome, command + " selected the wrong capture completion action.");
                            Check(result.ScreenBounds == area && SamePixels(image, result.Image), command + " changed the selected region or pixels.");
                            Call(app, "CompleteCapture", result, null);
                        }
                    };
                    Key(selection, command);
                    Pump(40);
                    Check(completed == 1 && !selection.Visible, command + " did not finish the selection once.");
                }
                Check(!unrelated.IsDisposed && unrelated.Visible && !unrelated.ClosedByUser, command + " closed an unrelated floating image.");
                Check(app.AllPins.Length == (expectedOutcome == CaptureOutcome.CopyAndClose ? 1 : 2), command + " ignored explicit close or optional automatic floating.");
                if (expectedOutcome == CaptureOutcome.Pin)
                    Check(Clipboard.GetText() == "Chacha explicit floating retains clipboard", "Explicit floating changed the clipboard.");
                else CheckClipboard(image, command + " did not copy the exact selected image.");
                ClosePins(app);
            }
            foreach (bool copyButton in new[] { false, true })
            {
                app.PinImage(image, new Rectangle(640, 300, image.Width, image.Height));
                PinForm unrelated = app.AllPins.Single();
                Call(app, "EditInline", image, desktop, desktopBounds, area, "Pen", false);
                EditorForm editor = ((List<EditorForm>)Field(app, "editors")).Last();
                Check(!editor.CopiedAndClosed, "A newly opened editor was already marked as copied and closed.");
                if (copyButton) Descendants(editor).OfType<Button>().Single(button => button.Visible && button.Text.StartsWith("복사", StringComparison.Ordinal)).PerformClick();
                else Key(editor, Keys.Control | Keys.C);
                Pump(40);
                string action = copyButton ? "Inline copy button" : "Inline Ctrl+C";
                Check(editor.IsDisposed && editor.CopiedAndClosed && !((List<EditorForm>)Field(app, "editors")).Contains(editor), action + " left the editor open or lost its successful-copy state.");
                Check(app.AllPins.Length == 1 && app.AllPins[0] == unrelated && unrelated.Visible, action + " created a floating image or affected an unrelated one.");
                CheckClipboard(image, action + " did not copy the exact edited image.");
                ClosePins(app);
            }
            app.PinImage(image, area);
            PinForm source = app.AllPins.Single();
            app.PinImage(image, new Rectangle(640, 300, image.Width, image.Height));
            PinForm peer = app.AllPins.Single(pin => pin != source);
            Key(source, Keys.Control | Keys.C);
            Pump(40);
            Check(!source.IsDisposed && !source.Visible && source.ClosedByUser, "Floating Ctrl+C did not close the copied image recoverably.");
            Check(peer.Visible && !peer.ClosedByUser && app.AllPins.Length == 2, "Floating Ctrl+C affected another floating image.");
            CheckClipboard(image, "Floating Ctrl+C changed the clipboard pixels.");
            app.PinClipboard();
            Check(source.Visible && !source.ClosedByUser && app.AllPins.Length == 2, "The floating shortcut did not restore the copied-and-closed image.");
            Key(source, Keys.Space);
            EditorForm pinEditor = ((List<EditorForm>)Field(app, "editors")).Last();
            Check(pinEditor.IsPinEditing && !source.Visible, "Floating edit did not hide its original image.");
            Rectangle crop = new Rectangle(17, 13, 80, 50);
            Call(pinEditor, "CropTo", crop);
            Check(pinEditor.HasChanges, "The pin-edit copy regression did not create a real image edit.");
            using (Bitmap edited = image.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                Key(pinEditor, Keys.Control | Keys.C);
                Pump(40);
                Check(pinEditor.IsDisposed && pinEditor.CopiedAndClosed, "Pin editor Ctrl+C did not complete and close its editor.");
                Check(!source.IsDisposed && !source.Visible && source.ClosedByUser, "Pin editor Ctrl+C reopened or destroyed its source image.");
                Check(peer.Visible && app.AllPins.Length == 2, "Pin editor Ctrl+C affected another image or created a replacement floating window.");
                CheckClipboard(edited, "Pin editor Ctrl+C did not copy the final cropped image.");
                using (Bitmap retained = source.ExportImage()) Check(SamePixels(edited, retained), "The recoverable source pin lost the final edited pixels.");
                app.PinClipboard();
                Check(source.Visible && !source.ClosedByUser && app.AllPins.Length == 2, "The copied pin edit could not be restored without duplicating its image.");
                using (Bitmap restored = source.ExportImage()) Check(SamePixels(edited, restored), "Restoring a copied pin edit reverted to the original pixels.");
            }
            ClosePins(app);
        }
        notes.Add("Ctrl+C regression: persisted automatic floating enabled; overlay, inline editor, copy button, floating image, edited floating image, and recovery passed.");
    }
    private static void OcrWorkflow(CaptureApplication app, string root)
    {
        Type tests = typeof(CaptureApplication).Assembly.GetType("ChachaCapture.OcrTests");
        Type service = typeof(CaptureApplication).Assembly.GetType("ChachaCapture.OcrService");
        FieldInfo cache = service.GetField("CacheRootForTest", BindingFlags.NonPublic | BindingFlags.Static);
        cache.SetValue(null, Path.Combine(root, "ocr"));
        try
        {
            using (Bitmap source = (Bitmap)tests.GetMethod("Sample", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 20, false }))
            {
                app.Store.Settings.AutoSave = true; app.Store.Settings.AutoFloatCapture = true;
                int history = app.Store.History().Length;
                int saved = Directory.Exists(app.Store.Settings.QuickSaveFolder) ? Directory.GetFiles(app.Store.Settings.QuickSaveFolder).Length : 0;
                OcrDialog(delegate { Complete(app, source, CaptureOutcome.Ocr, new Rectangle(Point.Empty, source.Size)); });
                Check(app.AllPins.Length == 0 && app.Store.History().Length == history, "OCR capture created an image pin or history.");
                Check((Directory.Exists(app.Store.Settings.QuickSaveFolder) ? Directory.GetFiles(app.Store.Settings.QuickSaveFolder).Length : 0) == saved, "OCR capture auto-saved a private image.");
                app.PinImage(source); PinForm pin = app.AllPins.Single();
                OcrDialog(delegate { Key(pin, Keys.Control | Keys.Shift | Keys.O); });
                Check(!pin.IsDisposed && !pin.ClosedByUser && pin.Visible, "Copying OCR text closed the source pin.");
                using (Bitmap unchanged = pin.ExportImage()) Check(SamePixels(source, unchanged), "OCR altered the source pin.");
                ClosePins(app);
                app.EditImage(source); EditorForm editor = ((List<EditorForm>)Field(app, "editors")).Last();
                OcrDialog(delegate { Descendants(editor).OfType<Button>().Single(b => b.Text == "텍스트 추출").PerformClick(); });
                Check(!editor.IsDisposed && !editor.CopiedAndClosed, "OCR copy closed the source editor.");
                editor.Close();
                app.Store.Settings.AutoSave = false;
            }
        }
        finally { cache.SetValue(null, null); }
    }

    private static void OcrDialog(Action trigger)
    {
        Exception failure = null; bool copied = false; Stopwatch watch = Stopwatch.StartNew();
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 })
        {
            timer.Tick += delegate
            {
                Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "OcrForm");
                if (dialog == null) return;
                try
                {
                    if (watch.ElapsedMilliseconds > 30000) throw new TimeoutException("OCR dialog did not finish.");
                    if ((bool)Field(dialog, "busy")) return;
                    TextBox result = (TextBox)Field(dialog, "result");
                    Check(result.Text.Contains("test@example.com"), "OCR UI did not receive real worker output.");
                    result.Text = "수정한 텍스트 English ? ! % @ :";
                    ((Button)Field(dialog, "copy")).PerformClick();
                    Check(Clipboard.ContainsText() && Clipboard.GetText() == result.Text, "OCR did not copy edited Unicode text.");
                    copied = true; timer.Stop(); dialog.Close();
                }
                catch (Exception error) { failure = error; timer.Stop(); dialog.Close(); }
            };
            timer.Start(); trigger(); timer.Stop();
        }
        if (failure != null) throw failure;
        Check(copied, "OCR entry point did not show the result dialog.");
    }

    [STAThread] private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "ChachaCapture-desktopqa-" + Guid.NewGuid().ToString("N"));
        string report = args[0];
        bool workflowOnly = args.Contains("--workflow-only");
        CaptureApplication app = null;
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            app = new CaptureApplication(new[] { "--tray", "--ui-test", "--data-dir", root });
            app.Store.Settings.SaveFolder = app.Store.Settings.QuickSaveFolder = Path.Combine(root, "saved");
            app.Store.Settings.CaptureHotkey = app.Store.Settings.PinHotkey = app.Store.Settings.ToggleHotkey = app.Store.Settings.ClickThroughHotkey = app.Store.Settings.SwitchGroupHotkey = "";
            app.Store.Settings.ClosePinHotkey = "";
            app.Store.Settings.AutoDetectElements = false; app.Store.Settings.AbortOnFocusLoss = false;
            app.ApplySettings();
            using (Bitmap image = new Bitmap(160, 100))
            {
                using (Graphics g = Graphics.FromImage(image)) g.Clear(Color.CornflowerBlue);
                Rectangle area = new Rectangle(420, 300, 160, 100);
                Complete(app, image, CaptureOutcome.Copy, area);
                Check(app.AllPins.Length == 1 && app.AllPins[0].Visible && app.AllPins[0].TopMost, "Copy did not automatically create a visible topmost floating image.");
                PinForm first = app.AllPins[0];
                Check(first.CloseHotkey == "", "New pins did not receive the optional close shortcut.");
                Check(first.ImageScreenBounds == area, "Floating image moved away from the captured region.");
                using (Bitmap pixels = first.ExportImage()) Check(pixels.GetPixel(5, 5).ToArgb() == Color.CornflowerBlue.ToArgb(), "Floating pixels changed.");
                Check(Field(first, "floatingToolbar") is Form && ((Form)Field(first, "floatingToolbar")).Visible, "Floating controls were not revealed.");
                ClosePins(app);
                app.Store.Settings.AutoFloatCapture = false;
                Complete(app, image, CaptureOutcome.Copy, area); Check(app.AllPins.Length == 0, "Disabled automatic floating still created a window.");
                Complete(app, image, CaptureOutcome.Pin, area); Check(app.AllPins.Length == 1, "Explicit floating failed with automatic floating disabled.");
                ClosePins(app); app.Store.Settings.AutoFloatCapture = true;
                Complete(app, image, CaptureOutcome.Pin, area); Check(app.AllPins.Length == 1, "Explicit floating created duplicate windows.");
                ClosePins(app);
                foreach (string action in new[] { "Enter", "PinImage", "QuickSaveImage" })
                {
                    int savedBefore = Directory.Exists(app.Store.Settings.QuickSaveFolder) ? Directory.GetFiles(app.Store.Settings.QuickSaveFolder).Length : 0;
                    int historyBefore = app.Store.History().Length;
                    app.Store.Settings.AutoSave = action == "PinImage";
                    using (Bitmap desktop = new Bitmap(1920, 1080)) Call(app, "EditInline", image, desktop, new Rectangle(0, 0, 1920, 1080), area, "Pen", false);
                    EditorForm editor = ((List<EditorForm>)Field(app, "editors")).Last();
                    bool shownBeforeClose = false; editor.FormClosing += delegate { shownBeforeClose = app.AllPins.Length != 0; };
                    if (action == "PinImage")
                    {
                        editor.Activate(); Pump(80);
                        Call(Field(app, "hotkeys"), "WndProc", Message.Create(IntPtr.Zero, 0x312, new IntPtr(2), IntPtr.Zero));
                    }
                    else if (action == "Enter") Key(editor, Keys.Enter);
                    else Call(editor, action);
                    Pump(40);
                    Check(editor.IsDisposed && !shownBeforeClose, action + " showed floating before the editor closed.");
                    Check(app.AllPins.Length == 1 && app.AllPins[0].Visible, action + " did not create exactly one floating image.");
                    if (action == "PinImage")
                    {
                        int savedAfter = Directory.Exists(app.Store.Settings.QuickSaveFolder) ? Directory.GetFiles(app.Store.Settings.QuickSaveFolder).Length : 0;
                        Check(savedBefore == savedAfter && historyBefore == app.Store.History().Length, "Floating the editor triggered automatic export or capture history.");
                    }
                    app.Store.Settings.AutoSave = false;
                    ClosePins(app);
                }
            }
            CopyCompletionWorkflow(app, root);
            OcrWorkflow(app, root);
            HotkeyWindow hotkeys = (HotkeyWindow)Field(app, "hotkeys");
            Exception modalError = null;
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 80 })
            {
                timer.Tick += delegate
                {
                    timer.Stop();
                    SettingsForm settings = (SettingsForm)Field(app, "settingsForm");
                    try
                    {
                        Check(hotkeys.IsSuspended, "Settings did not suspend global shortcuts.");
                        foreach (string name in new[] { "capture", "pin", "toggle", "clickThrough", "switchGroup", "closePin" })
                            Check(((HotkeyCaptureBox)Field(settings, name)).Hotkey == "", "Opening settings reset an unbound shortcut.");
                        Call(settings, "Save", settings, EventArgs.Empty);
                        Check(settings.DialogResult == DialogResult.OK, "Saving all shortcuts unbound failed.");
                    }
                    catch (Exception error) { modalError = error; settings.Close(); }
                };
                timer.Start(); app.ShowSettings();
            }
            if (modalError != null) throw modalError;
            Check(!hotkeys.IsSuspended, "Settings exit did not resume registration.");
            Check(new Storage(root).Settings.CaptureHotkey == "", "Unbound capture shortcut did not persist.");
            Check(new Storage(root).Settings.ClosePinHotkey == "", "Unbound local close shortcut did not persist.");
            if (!workflowOnly) using (Form fixture = new Form { Text = "Chacha capture test canvas", FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = Screen.PrimaryScreen.Bounds, BackColor = Color.FromArgb(32, 80, 128), TopMost = true })
            {
                fixture.Show(); fixture.Activate(); Pump(180);
                Rectangle bounds;
                using (Bitmap captured = DesktopCapture.Capture(out bounds, DesktopCaptureMode.Compatibility))
                {
                    Point point = new Point(fixture.Left + 70 - bounds.Left, fixture.Top + 70 - bounds.Top);
                    Check(captured.GetPixel(point.X, point.Y).ToArgb() == fixture.BackColor.ToArgb(), "GDI did not capture the synthetic desktop color.");
                    Check(DesktopCapture.LastReport != null && DesktopCapture.LastReport.Backend == DesktopCapture.LastBackend && DesktopCapture.LastBackend.StartsWith("GDI", StringComparison.Ordinal), "Compatibility capture reported a backend different from its pixels.");
                    Check(DesktopCapture.LastReport.Details.Contains("Mode=Compatibility") && DesktopCapture.LastReport.Details.Contains("ElapsedMs="), "Capture diagnostics lost the selected mode or timing.");
                    notes.Add("Compatibility backend: " + DesktopCapture.LastBackend);
                }
                try
                {
                    using (Bitmap gpu = DesktopCapture.CaptureDxgi(out bounds))
                    {
                        Point point = new Point(fixture.Left + 70 - bounds.Left, fixture.Top + 70 - bounds.Top);
                        Check(gpu.GetPixel(point.X, point.Y).ToArgb() == fixture.BackColor.ToArgb(), "DXGI changed the synthetic desktop color.");
                        Check(DesktopCapture.LastReport.Backend == "DXGI Desktop Duplication" && !DesktopCapture.LastReport.Details.Contains("GDI /"), "Explicit GPU capture silently replaced its frame with GDI.");
                        notes.Add("DXGI backend: passed");
                    }
                }
                catch (System.Runtime.InteropServices.COMException error) { notes.Add("DXGI unavailable: " + error.ErrorCode); }
                catch (InvalidOperationException error)
                {
                    if (error.InnerException == null) throw;
                    notes.Add("DXGI unavailable: " + error.Message + "; " + DesktopCapture.LastReport.Details);
                }
                // Exercise the exact reported GDI failure through the real capture coordinator,
                // then require an actual GPU frame and exact selected/clipboard pixels.
                app.Store.Settings.CaptureMode = DesktopCaptureMode.Compatibility; app.Store.SaveSettings();
                MethodInfo recoverCapture = typeof(DesktopCapture).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(Rectangle).MakeByRefType(), typeof(DesktopCaptureMode), typeof(bool), typeof(Func<uint, int?>) }, null);
                int blits = 0;
                Func<uint, int?> failedLayeredCopy = delegate(uint operation)
                {
                    blits++;
                    Check(operation == 0x40CC0020u, "Recovery attempted the basic copy that returned blank frames.");
                    return 288;
                };
                object[] captureArguments = { Rectangle.Empty, new Storage(root).Settings.CaptureMode, true, failedLayeredCopy };
                using (Panel marker = new Panel { Bounds = new Rectangle(100, 100, 80, 60), BackColor = Color.DeepPink })
                {
                    fixture.Controls.Add(marker); fixture.Refresh(); Pump(100);
                    using (Bitmap recoveredFrame = (Bitmap)recoverCapture.Invoke(null, captureArguments))
                    {
                        bounds = (Rectangle)captureArguments[0];
                        Point point = new Point(fixture.Left + 120 - bounds.Left, fixture.Top + 120 - bounds.Top);
                        Check(blits == 1 && recoveredFrame.GetPixel(point.X, point.Y).ToArgb() == marker.BackColor.ToArgb(), "Error 288 recovery did not read actual GPU screen pixels.");
                        Check(recoveredFrame.GetPixel(point.X - 50, point.Y - 50).ToArgb() == fixture.BackColor.ToArgb(), "Recovery returned a blank or single-color frame.");
                        Check(DesktopCapture.LastReport.Recovered && DesktopCapture.LastBackend == "DXGI Desktop Duplication" &&
                            DesktopCapture.LastReport.Details.Contains("288") && DesktopCapture.LastReport.Details.Contains("Recovery=Compatibility -> Graphics"), "Recovery did not disclose the original error and actual backend.");
                        Check(new Storage(root).Settings.CaptureMode == DesktopCaptureMode.Compatibility, "Recovery rewrote the user's saved preference.");
                        Rectangle region = new Rectangle(fixture.Left + 70, fixture.Top + 70, 120, 100);
                        using (Bitmap expected = recoveredFrame.Clone(new Rectangle(region.X - bounds.X, region.Y - bounds.Y, region.Width, region.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        using (CaptureOverlay selection = new CaptureOverlay(recoveredFrame, bounds, Rectangle.Empty, null, false, false))
                        {
                            selection.SetSelection(region); bool completed = false;
                            selection.Completed += delegate(CaptureResult result)
                            {
                                using (result)
                                {
                                    completed = true;
                                    Check(SamePixels(expected, result.Image), "Recovered frame changed during area selection.");
                                    ClipboardImages.Copy(result.Image);
                                    CheckClipboard(expected, "Recovered capture lost pixels when copied to the clipboard.");
                                }
                            };
                            Key(selection, Keys.Control | Keys.C);
                            Check(completed, "Recovered capture could not complete an area selection.");
                        }
                    }
                    // Keep the real DXGI session and its resource ownership. Mark its first
                    // acquired frame as pointer-only, then repaint our own fixture so the next
                    // acquired desktop must contain a different, known pixel color.
                    FieldInfo skipFrame = typeof(DesktopCapture).GetField("SkipDxgiFrameForTest", BindingFlags.NonPublic | BindingFlags.Static);
                    int acquiredFrames = 0;
                    skipFrame.SetValue(null, new Func<bool>(delegate
                    {
                        acquiredFrames++;
                        if (acquiredFrames != 1) return false;
                        marker.BackColor = Color.LimeGreen; marker.Refresh();
                        return true;
                    }));
                    try
                    {
                        using (Bitmap delayedFrame = (Bitmap)recoverCapture.Invoke(null, captureArguments))
                        {
                            bounds = (Rectangle)captureArguments[0];
                            Point point = new Point(fixture.Left + 120 - bounds.Left, fixture.Top + 120 - bounds.Top);
                            Check(acquiredFrames >= 2 && delayedFrame.GetPixel(point.X, point.Y).ToArgb() == Color.LimeGreen.ToArgb(), "A pointer-only first frame was accepted instead of the later desktop update.");
                            Check(delayedFrame.GetPixel(point.X - 50, point.Y - 50).ToArgb() == fixture.BackColor.ToArgb(), "Waiting for a desktop update lost surrounding screen pixels.");
                            Check(DesktopCapture.LastReport.Details.Contains("DXGI first desktop") && System.Text.RegularExpressions.Regex.IsMatch(DesktopCapture.LastReport.Details, @"skipped=[1-9][0-9]*;") && DesktopCapture.LastReport.Details.Contains("present=True"), "Desktop acquisition did not report its skipped initial frame.");
                            notes.Add("Injected 288 + pointer-only initial DXGI frame -> later real desktop pixels: passed; " + DesktopCapture.LastReport.Details.Replace(Environment.NewLine, " | "));
                        }
                    }
                    finally { skipFrame.SetValue(null, null); }
                    fixture.Controls.Remove(marker);
                }
                notes.Add("Injected GDI CAPTUREBLT error 288 -> actual DXGI -> area selection -> exact clipboard pixels: passed; saved Compatibility retained.");
                foreach (Color solid in new[] { Color.White, Color.Black, Color.Gray })
                {
                    fixture.BackColor = solid; fixture.Refresh(); Pump(100);
                    using (Bitmap captured = DesktopCapture.Capture(out bounds, DesktopCaptureMode.Automatic))
                    {
                        Point point = new Point(fixture.Left + 70 - bounds.Left, fixture.Top + 70 - bounds.Top);
                        Check(captured.GetPixel(point.X, point.Y).ToArgb() == solid.ToArgb(), "A legitimate solid desktop was discarded or changed.");
                        Check(DesktopCapture.LastBackend.StartsWith("GDI", StringComparison.Ordinal) && DesktopCapture.LastReport.Details.Contains("Mode=Automatic"), "Automatic still capture did not retain successful compatible pixels.");
                        Check(!DesktopCapture.LastReport.UniformFrame || DesktopCapture.LastReport.Notice.Contains("미리보기"), "A uniform desktop was not explained as a visual check.");
                        notes.Add("Solid " + solid.Name + ": preserved; uniform=" + DesktopCapture.LastReport.UniformFrame);
                    }
                }
                fixture.BackColor = Color.FromArgb(32, 80, 128); fixture.Refresh(); Pump(100);
                app.Whiteboard(Color.White); Pump(100);
                EditorForm whiteboard = ((List<EditorForm>)Field(app, "editors")).Last();
                app.BeginCapture(0, false, false); Pump(800);
                CaptureOverlay overlay = (CaptureOverlay)Field(app, "overlay");
                Check(overlay != null && overlay.Visible, "Capture did not open over a whiteboard.");
                Check(overlay.CaptureNotice == DesktopCapture.LastReport.Notice, "The overlay did not receive the capture diagnostics notice.");
                notes.Add("Application backend: " + DesktopCapture.LastBackend);
                Bitmap snapshot = (Bitmap)Field(overlay, "_desktop"); Rectangle desktopBounds = (Rectangle)Field(overlay, "_desktopBounds");
                Check(snapshot.GetPixel(fixture.Left + 70 - desktopBounds.Left, fixture.Top + 70 - desktopBounds.Top).ToArgb() == fixture.BackColor.ToArgb(), "Whiteboard leaked into the desktop capture.");
                overlay.Close(); Pump(40); Check(whiteboard.Visible && whiteboard.WindowState != FormWindowState.Minimized, "Canceled capture did not restore unfinished editor.");
                app.BeginCapture(0, false, false); Pump(800);
                overlay = (CaptureOverlay)Field(app, "overlay");
                overlay.SetSelection(new Rectangle(fixture.Left + 30, fixture.Top + 30, 180, 110));
                Call(overlay, "Finish", CaptureOutcome.Copy, null); Pump(60);
                Check(app.AllPins.Length == 1 && app.AllPins[0].Visible, "Actual overlay completion failed to float.");
                Check(Field(app, "overlay") == null && !(bool)Field(app, "capturing"), "Overlay remained active after completion.");
                Check(whiteboard.Visible && whiteboard.ShowInTaskbar && whiteboard.WindowState == FormWindowState.Minimized, "Unfinished editor was lost or still covered the captured desktop.");
                using (Bitmap pixels = app.AllPins[0].ExportImage()) Check(pixels.GetPixel(30, 30).ToArgb() == fixture.BackColor.ToArgb(), "Actual capture floated a blank/white image.");
                ClosePins(app); whiteboard.Close();
                app.Store.Settings.AutoSave = true;
                int savedCount = Directory.Exists(app.Store.Settings.QuickSaveFolder) ? Directory.GetFiles(app.Store.Settings.QuickSaveFolder).Length : 0;
                int historyCount = app.Store.History().Length;
                Clipboard.SetText("Chacha floating-only QA marker");
                app.BeginCapture(0, false, false); Pump(800);
                overlay = (CaptureOverlay)Field(app, "overlay");
                Call(hotkeys, "WndProc", Message.Create(IntPtr.Zero, 0x312, new IntPtr(2), IntPtr.Zero)); Pump(40);
                Check(app.AllPins.Length == 0 && Field(app, "overlay") == overlay && overlay.SelectedScreenBounds.IsEmpty,
                    "Floating before selection pasted clipboard data or closed the capture.");
                Rectangle directArea = new Rectangle(fixture.Left + 40, fixture.Top + 40, 135, 90);
                overlay.SetSelection(directArea);
                Call(hotkeys, "WndProc", Message.Create(IntPtr.Zero, 0x312, new IntPtr(2), IntPtr.Zero)); Pump(80);
                Check(Field(app, "overlay") == null && app.AllPins.Length == 1, "The global floating shortcut did not finish the current capture exactly once.");
                Check(app.AllPins[0].ImageScreenBounds == directArea, "The global floating shortcut used clipboard geometry instead of the selected region.");
                using (Bitmap direct = app.AllPins[0].ExportImage()) Check(direct.GetPixel(5, 5).ToArgb() == fixture.BackColor.ToArgb(), "The global floating shortcut used clipboard pixels instead of capture pixels.");
                Check(Clipboard.GetText() == "Chacha floating-only QA marker", "Floating-only unexpectedly replaced the clipboard.");
                int savedAfterPin = Directory.Exists(app.Store.Settings.QuickSaveFolder) ? Directory.GetFiles(app.Store.Settings.QuickSaveFolder).Length : 0;
                Check(savedAfterPin == savedCount && app.Store.History().Length == historyCount, "Floating-only exported a file or added history despite its explicit action.");
                app.Store.Settings.AutoSave = false; ClosePins(app); fixture.Close();
            }
            else notes.Add("Live desktop capture checks explicitly skipped (--workflow-only).");
            app.Store.Settings.AutoFloatCapture = false; app.Store.Settings.KeepHistory = false;
            using (Bitmap unfinished = new Bitmap(83, 47))
            {
                using (Graphics g = Graphics.FromImage(unfinished)) g.Clear(Color.Salmon);
                app.EditImage(unfinished);
                using (Bitmap desktop = new Bitmap(1920, 1080)) Call(app, "EditInline", unfinished, desktop, new Rectangle(0, 0, 1920, 1080), new Rectangle(80, 80, 83, 47), "Pen", false);
            }
            app.ShutdownForUpdate();
            Storage recovered = new Storage(root);
            Check(recovered.ReadPins().Count == 2, "Update shutdown lost unfinished editors when history/automatic floating were disabled.");
            foreach (PinRecord record in recovered.ReadPins()) using (Bitmap image = Storage.LoadBitmap(recovered.PinPath(record.Id)))
                Check(image.Size == new Size(83, 47) && image.GetPixel(10, 10).ToArgb() == Color.Salmon.ToArgb(), "Update shutdown did not preserve final editor pixels.");
            app.Dispose(); app = null;
            File.WriteAllText(report, "PASS " + checks + " assertions\r\n" + String.Join("\r\n", notes.ToArray()));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(report, "FAIL after " + checks + " assertions\r\n" + error + "\r\n" + String.Join("\r\n", notes.ToArray())); return 1;
        }
        finally
        {
            if (app != null) { app.Shutdown(); app.Dispose(); }
            string path = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith("ChachaCapture-desktopqa-", StringComparison.Ordinal) && Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
