using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ChachaCapture
{
    /// <summary>Offline regression checks. Never starts the app controller or reads the clipboard/screen.</summary>
    internal static class SelfTests
    {
        private sealed class Result
        {
            internal string Name;
            internal string Error;
            internal long Milliseconds;
        }

        internal static int Run(string[] args)
        {
            string output;
            string renderDirectory;
            try
            {
                output = Path.GetFullPath(Option(args, "--test-output") ?? "self-test-report.json");
                renderDirectory = Option(args, "--render-dir");
                if (renderDirectory != null) renderDirectory = Path.GetFullPath(renderDirectory);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Invalid self-test options: " + error.Message);
                return 2;
            }

            List<Result> results = new List<Result>();
            string temporaryParent = Path.GetFullPath(Path.GetTempPath());
            string testRoot = Path.Combine(temporaryParent, "ChachaCapture-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(testRoot);
                Check(results, "Settings XML round-trip and atomic replacement", delegate { TestSettings(Path.Combine(testRoot, "settings")); });
                Check(results, "Interrupted settings replacement preserves previous file", delegate { TestAtomicFailure(Path.Combine(testRoot, "atomic")); });
                Check(results, "Corrupt settings fallback and retention bounds", delegate { TestSettingsFallback(Path.Combine(testRoot, "fallback")); });
                Check(results, "Pin metadata round-trip and path traversal rejection", delegate { TestPinStorage(Path.Combine(testRoot, "pins")); });
                Check(results, "Scaled pin session restores size and full-resolution pixels", delegate { TestScaledPinRestore(Path.Combine(testRoot, "scaled-pin")); });
                Check(results, "History retention and lossless PNG pixels", delegate { TestHistory(Path.Combine(testRoot, "history")); });
                Check(results, "Color parsing and text rendering without clipboard access", TestClipboardRendering);
                Check(results, "Hotkey parsing and malformed-key rejection", TestHotkeys);
                Check(results, "Keyboard chord recorder, modifier previews and navigation", CaptureRegressionTests.Recorder);
                Check(results, "Manual capture disables all automatic selection and pins exact pixels", AutoDetectionTests.ManualSelectionAndPin);
                Check(results, "Optional local pin close, aliases and toolbar key routing", PinCloseTests.Run);
                Check(results, "Drawing toolbar labels and floating without save side effects", EditorUiRegressionTests.Run);
                Check(results, "Optional hotkeys, recording suspension and automatic floating migration", delegate { CaptureRegressionTests.OptionalSettings(Path.Combine(testRoot, "optional-hotkeys")); });
                Check(results, "Opaque desktop pixels, padded RGB rows and negative stride", CaptureRegressionTests.Pixels);
                Check(results, "DXGI baked cursor detection respects valid output metadata", DesktopCursorTests.Detection);
                Check(results, "Capture backend selection, uniform frames and native failure diagnostics", DesktopCaptureTests.BackendSelectionAndDiagnostics);
                Check(results, "Update UI cancellation, retries and installer failure keep the app safe", UpdateUiRegressionTests.Run);
                Check(results, "Update release provenance, checksums, x64 format and installer paths (19 groups)", delegate { UpdateTests.Run(Path.Combine(testRoot, "updates")); });
                Check(results, "Pin clone ownership, full-resolution transforms and native click-through", TestPin);
                Check(results, "Editor copy ownership and crop undo/redo pixel fidelity", TestEditorHistory);
                Check(results, "Mosaic averages only the requested region", TestMosaic);
                Check(results, "Blur stays inside region and handles small regions", TestBlur);
                Check(results, "GIF frames, transforms, speed and paused session restore", delegate { ParityTests.Animation(Path.Combine(testRoot, "animation")); });
                Check(results, "Recoverable pin close, destruction and selected movement", ParityTests.PinLifecycle);
                Check(results, "Styled HTML clipboard rendering and fragment extraction", ParityTests.HtmlClipboard);
                Check(results, "TGA raw/RLE decoding, orientation, palette and alpha", delegate { ParityTests.Tga(Path.Combine(testRoot, "tga")); });
                Check(results, "History screen bounds and paired metadata retention", delegate { ParityTests.HistoryBounds(Path.Combine(testRoot, "history-bounds")); });
                Check(results, "Image group archive lossless metadata and asset round-trip", delegate { ParityTests.GroupRoundTrip(Path.Combine(testRoot, "group-roundtrip")); });
                Check(results, "Image group archive rejects malformed or missing assets", delegate { ParityTests.GroupValidation(Path.Combine(testRoot, "group-validation")); });
                Check(results, "Editor dirty state and inline screen bounds across undo/redo/clear", ParityTests.EditorChangeLifecycle);
                Check(results, "Pin editing preserves native pixels and rounded screen mapping", ParityTests.PinEditorMapping);
                Check(results, "Legacy hide shortcut migration preserves user settings", delegate { ParityTests.SettingsMigration(Path.Combine(testRoot, "migration")); });
                if (renderDirectory != null)
                    Check(results, "Deterministic fixture and UI previews", delegate { RenderPreviews(renderDirectory); });
            }
            catch (Exception error)
            {
                results.Add(new Result { Name = "Self-test setup", Error = error.ToString() });
            }
            finally
            {
                Check(results, "Remove generated temporary profile", delegate
                {
                    string resolved = Path.GetFullPath(testRoot);
                    string allowedParent = temporaryParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    Assert(resolved.StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase), "Cleanup target must remain under the temp directory.");
                    Assert(String.Equals(Path.GetDirectoryName(resolved).TrimEnd(Path.DirectorySeparatorChar), temporaryParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase), "Cleanup target must be a direct temp child.");
                    Assert(Path.GetFileName(resolved).StartsWith("ChachaCapture-selftest-", StringComparison.Ordinal), "Cleanup target must be a generated test profile.");
                    if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
                });
            }

            int failures = 0;
            foreach (Result result in results) if (result.Error != null) failures++;
            string report = BuildReport(results, failures, renderDirectory);
            try
            {
                string parent = Path.GetDirectoryName(output);
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(output, report, new UTF8Encoding(false));
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Unable to write self-test report: " + error.Message);
                return 2;
            }
            Console.WriteLine((results.Count - failures) + "/" + results.Count + " checks passed. Report: " + output);
            return failures == 0 ? 0 : 1;
        }

        private static string Option(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) || String.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException(name + " requires a path.");
            return args[index + 1];
        }

        private static void Check(List<Result> results, string name, Action test)
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            Result result = new Result { Name = name };
            try { test(); }
            catch (Exception error)
            {
                TargetInvocationException invocation = error as TargetInvocationException;
                result.Error = (invocation != null && invocation.InnerException != null ? invocation.InnerException : error).ToString();
            }
            timer.Stop(); result.Milliseconds = timer.ElapsedMilliseconds;
            results.Add(result);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void TestSettings(string directory)
        {
            Storage store = new Storage(directory);
            store.Settings.CaptureHotkey = "Ctrl+Shift+A";
            store.Settings.SaveFolder = Path.Combine(directory, "저장 폴더 & captures");
            store.Settings.LastSelection = new Rectangle(-1800, 72, 531, 307);
            store.Settings.HistoryLimit = 17;
            store.SaveSettings();
            Storage loaded = new Storage(directory);
            Assert(loaded.Settings.CaptureHotkey == "Ctrl+Shift+A", "Hotkey did not survive XML round-trip.");
            Assert(loaded.Settings.SaveFolder == store.Settings.SaveFolder, "Unicode or XML characters in the save path changed.");
            Assert(loaded.Settings.LastSelection == store.Settings.LastSelection, "Negative monitor coordinates changed.");
            Assert(loaded.Settings.HistoryLimit == 17, "History limit changed.");
            loaded.Settings.HistoryLimit = 9;
            loaded.SaveSettings();
            Assert(new Storage(directory).Settings.HistoryLimit == 9, "Replacing an existing settings file failed.");
            Assert(!File.Exists(Path.Combine(directory, "settings.xml.tmp")), "Successful atomic replacement left a temporary file.");
        }

        private static void TestAtomicFailure(string directory)
        {
            Storage store = new Storage(directory);
            store.Settings.HistoryLimit = 13;
            store.SaveSettings();
            string path = Path.Combine(directory, "settings.xml");
            bool rejected = false;
            using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                store.Settings.HistoryLimit = 27;
                try { store.SaveSettings(); }
                catch (IOException) { rejected = true; }
            }
            Assert(rejected, "Exclusive file lock did not reject replacement.");
            Assert(new Storage(directory).Settings.HistoryLimit == 13, "Failed replacement damaged the previously saved settings.");
            store.SaveSettings();
            Assert(new Storage(directory).Settings.HistoryLimit == 27, "Retry after releasing the file lock failed.");
            Assert(!File.Exists(path + ".tmp"), "Retry left a temporary settings file.");
        }

        private static void TestSettingsFallback(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "settings.xml"), "<AppSettings><broken>");
            Storage store = new Storage(directory);
            Assert(store.Settings.CaptureHotkey == "F1", "Corrupt settings did not fall back to defaults.");
            store.Settings.HistoryLimit = -5;
            store.SaveSettings();
            Assert(new Storage(directory).Settings.HistoryLimit == 1, "History lower bound was not enforced.");
            store.Settings.HistoryLimit = 999;
            store.SaveSettings();
            Assert(new Storage(directory).Settings.HistoryLimit == 200, "History upper bound was not enforced.");
        }

        private static void TestPinStorage(string directory)
        {
            Storage store = new Storage(directory);
            string id = Guid.NewGuid().ToString("N");
            List<PinRecord> records = new List<PinRecord>();
            records.Add(new PinRecord { Id = id, X = -1200, Y = 44, Width = 640, Height = 320, ScaleFactor = 0.25, Opacity = 0.65, Visible = false, TopMost = false });
            store.WritePins(records);
            List<PinRecord> loaded = store.ReadPins();
            Assert(loaded.Count == 1 && loaded[0].Id == id && loaded[0].X == -1200 && loaded[0].Width == 640, "Pin coordinates or identity changed.");
            Assert(loaded[0].Opacity == 0.65 && !loaded[0].Visible && !loaded[0].TopMost, "Pin display state changed.");
            Assert(loaded[0].ScaleFactor == 0.25, "Pin scale factor did not survive XML round-trip.");
            store.WritePins(new List<PinRecord>());
            Assert(store.ReadPins().Count == 0, "Replacing pin metadata failed.");
            Assert(Path.GetDirectoryName(store.PinPath(id)) == store.PinsDirectory, "Valid pin path escaped the pins directory.");
            foreach (string invalid in new string[] { null, "", "../outside", "..\\outside", "C:\\outside", "a/b", Guid.NewGuid().ToString("D"), new string('a', 33) })
            {
                bool rejected = false;
                try { store.PinPath(invalid); }
                catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Unsafe pin identifier was accepted: " + (invalid ?? "null"));
            }
        }

        private static void TestHistory(string directory)
        {
            Storage store = new Storage(directory);
            store.Settings.HistoryLimit = 2;
            using (Bitmap fixture = PixelFixture())
            {
                fixture.Save(Path.Combine(store.HistoryDirectory, "20000101-000000-001-first.png"), ImageFormat.Png);
                fixture.Save(Path.Combine(store.HistoryDirectory, "20000101-000000-002-second.png"), ImageFormat.Png);
                fixture.Save(Path.Combine(store.HistoryDirectory, "20000101-000000-003-third.png"), ImageFormat.Png);
                string path = store.AddHistory(fixture);
                string[] files = store.History();
                Assert(files.Length == 2 && files[0] == path, "History did not retain the newest images or enforce its limit.");
                Assert(Path.GetFileName(files[1]).Contains("third"), "History retained an older item over a newer one.");
                using (Bitmap loaded = Storage.LoadBitmap(path)) EqualPixels(fixture, loaded, "PNG round-trip");
                using (Bitmap loaded = Storage.LoadBitmap(path))
                {
                    string moved = path + ".moved";
                    File.Move(path, moved);
                    File.Move(moved, path);
                    Assert(loaded.GetPixel(7, 9).ToArgb() == fixture.GetPixel(7, 9).ToArgb(), "Loaded bitmap stopped working after its source file moved.");
                }
                store.Settings.KeepHistory = false;
                Assert(store.AddHistory(fixture) == null && store.History().Length == 2, "Disabled history still saved an image.");
            }
        }

        private static void TestScaledPinRestore(string directory)
        {
            Storage store = new Storage(directory);
            string id = Guid.NewGuid().ToString("N");
            using (Bitmap original = new Bitmap(320, 180, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(original)) g.Clear(Color.FromArgb(22, 95, 173));
                original.SetPixel(0, 0, Color.Red);
                original.SetPixel(319, 179, Color.Lime);
                using (PinForm pin = new PinForm(original))
                {
                    pin.ScaleFactor = 0.25;
                    Assert(pin.ClientSize == new Size(84, 49), "Quarter-scale pin did not have the expected image size plus frame.");
                    using (Bitmap fullImage = pin.ExportImage()) fullImage.Save(store.PinPath(id), ImageFormat.Png);
                    store.WritePins(new List<PinRecord> { new PinRecord { Id = id, Width = pin.Width, Height = pin.Height, ScaleFactor = pin.ScaleFactor } });
                }
                Storage reopened = new Storage(directory);
                PinRecord saved = reopened.ReadPins()[0];
                using (Bitmap restoredImage = Storage.LoadBitmap(reopened.PinPath(saved.Id)))
                using (PinForm restored = new PinForm(restoredImage))
                {
                    restored.ScaleFactor = saved.ScaleFactor;
                    Assert(restored.ScaleFactor == 0.25, "Restoring a pin changed its persisted scale factor.");
                    Assert(restored.ClientSize == new Size(84, 49), "Restoring a pin changed its displayed dimensions.");
                    using (Bitmap exported = restored.ExportImage()) EqualPixels(original, exported, "Restored scaled pin full-resolution export");
                    Assert(!restored.Visible, "Scaled pin restore test unexpectedly showed a window.");
                }
            }
        }

        private static void TestClipboardRendering()
        {
            Color color;
            Assert(ClipboardImages.TryColor("#3af", out color) && color.ToArgb() == Color.FromArgb(51, 170, 255).ToArgb(), "Three-digit hex color parsed incorrectly.");
            Assert(ClipboardImages.TryColor("#12AbEf", out color) && color.ToArgb() == Color.FromArgb(18, 171, 239).ToArgb(), "Six-digit hex color parsed incorrectly.");
            Assert(ClipboardImages.TryColor("RGB( 12, 34, 255 )", out color) && color.B == 255 && color.R == 12, "RGB color parsed incorrectly.");
            foreach (string invalid in new string[] { "", "#12", "#gggggg", "rgb(256, 0, 1)", "rgb(-1,0,0)", "rgb(1,2)", "red", "#12345678", "hello #fff" })
                Assert(!ClipboardImages.TryColor(invalid, out color), "Invalid color was accepted: " + invalid);
            Assert(ClipboardImages.RenderText("  ") == null, "Whitespace should not create an empty pinned note.");
            using (Bitmap swatch = ClipboardImages.RenderText("#123456"))
            {
                Assert(swatch.Size == new Size(300, 210), "Color swatch dimensions changed.");
                Assert(swatch.GetPixel(20, 20).ToArgb() == Color.FromArgb(18, 52, 86).ToArgb(), "Rendered swatch does not match the parsed color.");
            }
            using (Bitmap note = ClipboardImages.RenderText("Chacha Capture\n한글 메모 · deterministic text"))
                Assert(note.Width >= 220 && note.Width <= 700 && note.Height >= 100 && note.Height <= 1600, "Text note dimensions are outside rendering limits.");
            using (Bitmap longNote = ClipboardImages.RenderText(new string('W', 18000)))
                Assert(longNote.Width <= 700 && longNote.Height <= 1600, "Long text exceeded the note rendering limits.");
        }

        private static void TestHotkeys()
        {
            uint modifiers, key;
            Assert(HotkeyWindow.Parse("Ctrl+Shift+A", out modifiers, out key) && modifiers == 6 && key == (uint)Keys.A, "Ctrl+Shift+A parsed incorrectly.");
            Assert(HotkeyWindow.Parse(" alt + F12 ", out modifiers, out key) && modifiers == 1 && key == (uint)Keys.F12, "Whitespace/case handling failed.");
            Assert(HotkeyWindow.Parse("F3", out modifiers, out key) && modifiers == 0 && key == (uint)Keys.F3, "Unmodified F3 parsed incorrectly.");
            foreach (string invalid in new string[] { null, "", "Ctrl", "Ctrl++A", "A+B", "Ctrl+Banana", "Ctrl+999", "Ctrl+99999", "Ctrl+ControlKey", "Shift+Menu", "None" })
                Assert(!HotkeyWindow.Parse(invalid, out modifiers, out key), "Malformed hotkey was accepted: " + (invalid ?? "null"));
        }

        private static void TestPin()
        {
            using (Bitmap expected = PixelFixture())
            using (Bitmap supplied = (Bitmap)expected.Clone())
            using (PinForm pin = new PinForm(supplied))
            {
                supplied.SetPixel(3, 4, Color.Magenta);
                using (Bitmap exported = pin.ExportImage()) EqualPixels(expected, exported, "Pin input clone");
                pin.ScaleFactor = 0.75;
                using (Bitmap exported = pin.ExportImage()) EqualPixels(expected, exported, "Pin full-resolution export after scaling");
                DispatchKey(pin, Keys.D1);
                using (Bitmap rotated = (Bitmap)expected.Clone())
                {
                    rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    using (Bitmap exported = pin.ExportImage()) EqualPixels(rotated, exported, "Pin rotation export");
                    DispatchKey(pin, Keys.D3);
                    rotated.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    using (Bitmap exported = pin.ExportImage()) EqualPixels(rotated, exported, "Pin flipped export");
                }
                DispatchKey(pin, Keys.D3);
                DispatchKey(pin, Keys.D2);
                using (Bitmap exported = pin.ExportImage()) EqualPixels(expected, exported, "Pin counterclockwise rotation returns to source");
                pin.ReplaceImage(expected);
                using (Bitmap exported = pin.ExportImage()) EqualPixels(expected, exported, "Pin replacement");
                IntPtr handle = pin.Handle;
                pin.SetClickThrough(true);
                Assert(pin.ClickThrough, "Native click-through did not enable.");
                MethodInfo readStyle = typeof(PinForm).GetMethod("ReadWindowLong", BindingFlags.Static | BindingFlags.NonPublic);
                long style = ((IntPtr)readStyle.Invoke(null, new object[] { handle, -20 })).ToInt64();
                Assert((style & 0x80020) == 0x80020, "Click-through did not set transparent/layered window styles.");
                pin.SetClickThrough(false);
                style = ((IntPtr)readStyle.Invoke(null, new object[] { handle, -20 })).ToInt64();
                Assert(!pin.ClickThrough && (style & 0x20) == 0, "Native click-through did not clear.");
                Assert(!pin.Visible, "Pin test unexpectedly showed a window.");
            }
        }

        private static void TestEditorHistory()
        {
            using (Bitmap expected = PixelFixture())
            using (Bitmap supplied = (Bitmap)expected.Clone())
            using (EditorForm editor = new EditorForm(supplied))
            {
                supplied.SetPixel(3, 4, Color.Magenta);
                using (Bitmap rendered = (Bitmap)Invoke(editor, "RenderDocument")) EqualPixels(expected, rendered, "Editor input clone");
                Rectangle crop = new Rectangle(9, 7, 31, 23);
                Invoke(editor, "CropTo", crop);
                using (Bitmap wanted = expected.Clone(crop, PixelFormat.Format32bppArgb))
                {
                    using (Bitmap rendered = (Bitmap)Invoke(editor, "RenderDocument")) EqualPixels(wanted, rendered, "Editor crop");
                    Invoke(editor, "Undo");
                    using (Bitmap rendered = (Bitmap)Invoke(editor, "RenderDocument")) EqualPixels(expected, rendered, "Editor undo crop");
                    Invoke(editor, "Redo");
                    using (Bitmap rendered = (Bitmap)Invoke(editor, "RenderDocument")) EqualPixels(wanted, rendered, "Editor redo crop");
                }
                Assert(!editor.Visible, "Editor test unexpectedly showed a window.");
            }
        }

        private static void TestMosaic()
        {
            using (Bitmap original = PixelFixture())
            using (Bitmap processed = EditorForm.CopyBitmap(original))
            {
                Rectangle region = new Rectangle(8, 8, 8, 8);
                EditorForm.ApplyMosaic(processed, region, 4);
                AssertOutsideUnchanged(original, processed, region);
                long r = 0, g = 0, b = 0;
                for (int y = 8; y < 12; y++) for (int x = 8; x < 12; x++)
                { Color p = original.GetPixel(x, y); r += p.R; g += p.G; b += p.B; }
                int expected = Color.FromArgb((int)(r / 16), (int)(g / 16), (int)(b / 16)).ToArgb();
                for (int y = 8; y < 12; y++) for (int x = 8; x < 12; x++)
                    Assert(processed.GetPixel(x, y).ToArgb() == expected, "Mosaic block is not the average source color.");
                EditorForm.ApplyMosaic(processed, new Rectangle(-50, -50, 2, 2), 4);
                AssertOutsideUnchanged(original, processed, region);
            }
        }

        private static void TestBlur()
        {
            using (Bitmap original = new Bitmap(17, 17, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(original)) g.Clear(Color.Black);
                original.SetPixel(8, 8, Color.White);
                using (Bitmap processed = EditorForm.CopyBitmap(original))
                {
                    Rectangle region = new Rectangle(3, 3, 11, 11);
                    EditorForm.ApplyBlur(processed, region, 2);
                    AssertOutsideUnchanged(original, processed, region);
                    Assert(processed.GetPixel(8, 8).R > 0 && processed.GetPixel(8, 8).R < 255, "Blur did not soften the center impulse.");
                    Assert(processed.GetPixel(7, 8).R > 0, "Blur did not spread the center into neighboring pixels.");
                }
            }
            using (Bitmap solid = new Bitmap(3, 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(solid)) g.Clear(Color.FromArgb(42, 84, 126));
                using (Bitmap expected = (Bitmap)solid.Clone())
                {
                    EditorForm.ApplyBlur(solid, new Rectangle(-2, -2, 10, 10), 80);
                    EqualPixels(expected, solid, "Blur solid image with radius larger than image");
                }
            }
        }

        private static object Invoke(object instance, string method, params object[] arguments)
        {
            MethodInfo found = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(found != null, "Required behavior is unavailable: " + method);
            return found.Invoke(instance, arguments);
        }

        private static void DispatchKey(Form form, Keys keys)
        {
            Message message = Message.Create(IntPtr.Zero, 0x100, IntPtr.Zero, IntPtr.Zero);
            object[] arguments = new object[] { message, keys };
            Assert((bool)Invoke(form, "ProcessCmdKey", arguments), "Shortcut was not handled: " + keys);
        }

        private static Bitmap PixelFixture()
        {
            Bitmap fixture = new Bitmap(64, 48, PixelFormat.Format32bppArgb);
            for (int y = 0; y < fixture.Height; y++)
                for (int x = 0; x < fixture.Width; x++)
                    fixture.SetPixel(x, y, Color.FromArgb((x * 7 + y * 11) % 256, (x * 3 + y * 17) % 256, (x * 19 + y * 5) % 256));
            fixture.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));
            return fixture;
        }

        private static void EqualPixels(Bitmap expected, Bitmap actual, string operation)
        {
            Assert(expected.Size == actual.Size, operation + ": dimensions changed.");
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), operation + ": pixel changed at " + x + "," + y + ".");
        }

        private static void AssertOutsideUnchanged(Bitmap expected, Bitmap actual, Rectangle changedRegion)
        {
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    if (!changedRegion.Contains(x, y))
                        Assert(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Effect changed a pixel outside its selection.");
        }

        private static void RenderPreviews(string directory)
        {
            Directory.CreateDirectory(directory);
            using (Bitmap fixture = PreviewFixture())
            {
                fixture.Save(Path.Combine(directory, "fixture.png"), ImageFormat.Png);
                using (EditorForm editor = new EditorForm(fixture))
                {
                    editor.StartPosition = FormStartPosition.Manual;
                    editor.Location = new Point(0, 0);
                    editor.ClientSize = new Size(1240, 790);
                    CreateRenderHandles(editor);
                    editor.PerformLayout();
                    Invoke(editor, "FitImage");
                    using (Bitmap preview = new Bitmap(editor.Width, editor.Height))
                    {
                        editor.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                        AssertRenderedContent(preview);
                        preview.Save(Path.Combine(directory, "editor.png"), ImageFormat.Png);
                    }
                }
                using (PinForm pin = new PinForm(fixture))
                {
                    pin.ScaleFactor = 0.8;
                    pin.CreateControl();
                    using (Bitmap preview = new Bitmap(pin.Width, pin.Height))
                    {
                        pin.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                        AssertRenderedContent(preview);
                        preview.Save(Path.Combine(directory, "pin.png"), ImageFormat.Png);
                    }
                }
            }
        }

        private static Bitmap PreviewFixture()
        {
            Bitmap fixture = new Bitmap(960, 580, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(fixture))
            using (Font brand = new Font("Segoe UI", 25, FontStyle.Bold))
            using (Font title = new Font("Malgun Gothic", 19, FontStyle.Bold))
            using (Font body = new Font("Malgun Gothic", 11))
            using (Font number = new Font("Segoe UI", 32, FontStyle.Bold))
            using (Brush ink = new SolidBrush(Color.FromArgb(35, 48, 65)))
            using (Brush muted = new SolidBrush(Color.FromArgb(111, 125, 143)))
            using (Brush teal = new SolidBrush(Color.FromArgb(34, 176, 145)))
            using (Brush panel = new SolidBrush(Color.White))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(241, 245, 248));
                g.FillRectangle(panel, 0, 0, 960, 92);
                g.DrawString("STUDIO / WEEKLY", brand, ink, 34, 20);
                g.DrawString("작업 현황을 한눈에 확인하세요.", body, muted, 38, 106);
                string[] labels = { "완료한 작업", "진행 중", "다음 할 일" };
                string[] values = { "24", "08", "12" };
                for (int i = 0; i < 3; i++)
                {
                    int x = 35 + i * 302;
                    g.FillRectangle(panel, x, 154, 286, 142);
                    g.DrawString(labels[i], body, muted, x + 20, 171);
                    g.DrawString(values[i], number, i == 0 ? teal : ink, x + 18, 204);
                }
                g.FillRectangle(panel, 35, 318, 588, 223);
                g.DrawString("이번 주의 흐름", title, ink, 55, 335);
                for (int i = 0; i < 7; i++)
                {
                    int height = new int[] { 50, 85, 71, 113, 96, 129, 104 }[i];
                    g.FillRectangle(teal, 62 + i * 75, 510 - height, 38, height);
                }
                g.FillRectangle(panel, 641, 318, 284, 223);
                g.DrawString("다음 단계", title, ink, 661, 335);
                g.DrawString("01  시안 검토하기\n\n02  피드백 정리하기\n\n03  결과 공유하기", body, muted, 662, 384);
            }
            return fixture;
        }

        private static void CreateRenderHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateRenderHandles(child);
            control.PerformLayout();
        }

        private static void AssertRenderedContent(Bitmap preview)
        {
            HashSet<int> colors = new HashSet<int>();
            for (int y = 15; y < preview.Height; y += 17)
                for (int x = 15; x < preview.Width; x += 17)
                    colors.Add(preview.GetPixel(x, y).ToArgb());
            Assert(colors.Count > 16, "Preview is blank or its child controls were not rendered.");
        }

        private static string BuildReport(List<Result> results, int failures, string renderDirectory)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"application\": \"Chacha Capture\",\n  \"passed\": ").Append(failures == 0 ? "true" : "false");
            json.Append(",\n  \"architecture\": ").Append(Quote(IntPtr.Size == 8 ? "x64" : "x86"));
            json.Append(",\n  \"checks\": ").Append(results.Count).Append(",\n  \"failures\": ").Append(failures);
            json.Append(",\n  \"scope\": \"Synthetic images, temporary profile, invisible forms; no clipboard reads or desktop capture\"");
            json.Append(",\n  \"renderDirectory\": ").Append(renderDirectory == null ? "null" : Quote(renderDirectory));
            json.Append(",\n  \"results\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                Result result = results[i];
                json.Append("    { \"name\": ").Append(Quote(result.Name)).Append(", \"passed\": ").Append(result.Error == null ? "true" : "false");
                json.Append(", \"milliseconds\": ").Append(result.Milliseconds).Append(", \"error\": ").Append(result.Error == null ? "null" : Quote(result.Error)).Append(" }");
                if (i < results.Count - 1) json.Append(',');
                json.Append('\n');
            }
            json.Append("  ]\n}\n");
            return json.ToString();
        }

        private static string Quote(string value)
        {
            StringBuilder json = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '\\') json.Append("\\\\");
                else if (c == '"') json.Append("\\\"");
                else if (c == '\r') json.Append("\\r");
                else if (c == '\n') json.Append("\\n");
                else if (c == '\t') json.Append("\\t");
                else if (c < 32) json.Append("\\u").Append(((int)c).ToString("x4"));
                else json.Append(c);
            }
            return json.Append('"').ToString();
        }
    }
}
