using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ChachaCapture
{
    /// <summary>Regression checks for persistent pins and expanded clipboard/file formats.</summary>
    internal static class ParityTests
    {
        internal static void Animation(string directory)
        {
            Storage store = new Storage(directory);
            byte[] data = TwoFrameGif();
            using (AnimatedImageSource decoded = new AnimatedImageSource(data))
            {
                Require(decoded.FrameCount == 2 && decoded.DelayForFrame(0) == 100 && decoded.DelayForFrame(1) == 200, "GIF frame count or encoded delays changed.");
                data[0] = 0;
                using (Bitmap first = decoded.GetFrame(0))
                {
                    Require(first.Size == new Size(2, 1), "GIF frame resolution changed.");
                    Pixel(first, 0, 0, Color.Red); Pixel(first, 1, 0, Color.Lime);
                }
                using (Bitmap second = decoded.GetFrame(1)) { Pixel(second, 0, 0, Color.Lime); Pixel(second, 1, 0, Color.Red); }
                data = decoded.ExportBytes();
            }
            string id = Guid.NewGuid().ToString("N");
            using (Bitmap seed = new Bitmap(2, 1))
            using (PinForm pin = new PinForm(seed))
            {
                pin.LoadAnimation(data);
                Require(pin.FrameCount == 2 && pin.IsPlaying, "Loading GIF did not enter playback mode.");
                pin.AnimationTransformState = "1";
                pin.StepFrame(1);
                Require(pin.CurrentFrame == 1 && !pin.IsPlaying, "Frame stepping did not pause at the next frame.");
                pin.SetPlaybackSpeed(2.5);
                pin.SeekFrame(0);
                Require(!pin.IsPlaying, "Seeking unexpectedly resumed paused playback.");
                pin.SeekFrame(1);
                using (Bitmap transformed = pin.ExportImage())
                {
                    Require(transformed.Size == new Size(1, 2), "GIF rotation did not preserve full-resolution frame dimensions.");
                    Pixel(transformed, 0, 0, Color.Lime); Pixel(transformed, 0, 1, Color.Red);
                    transformed.Save(store.PinPath(id), ImageFormat.Png);
                }
                byte[] exported = pin.ExportAnimation();
                File.WriteAllBytes(store.PinPath(id) + ".gif", exported);
                exported[0] = 0;
                Require(pin.ExportAnimation()[0] == (byte)'G', "Exported GIF bytes share mutable storage with the pin.");
                store.WritePins(new List<PinRecord> { new PinRecord { Id = id, HasAnimation = true, AnimationFrame = pin.CurrentFrame,
                    AnimationTransform = pin.AnimationTransformState, AnimationPlaying = pin.IsPlaying, AnimationSpeed = pin.PlaybackSpeed } });
            }
            PinRecord saved = new Storage(directory).ReadPins()[0];
            using (Bitmap seed = Storage.LoadBitmap(store.PinPath(id)))
            using (PinForm restored = new PinForm(seed))
            {
                restored.LoadAnimatedFile(store.PinPath(id) + ".gif");
                restored.AnimationTransformState = saved.AnimationTransform;
                restored.SeekFrame(saved.AnimationFrame);
                restored.SetPlaybackSpeed(saved.AnimationSpeed);
                restored.SetPlaying(saved.AnimationPlaying);
                Require(restored.CurrentFrame == 1 && restored.PlaybackSpeed == 2.5 && !restored.IsPlaying, "Restored GIF frame, speed or pause state changed.");
                using (Bitmap frame = restored.ExportImage()) PixelsEqual(seed, frame);
                restored.StepFrame(-1);
                using (Bitmap frame = restored.ExportImage()) { Pixel(frame, 0, 0, Color.Red); Pixel(frame, 0, 1, Color.Lime); }
                restored.SetPlaying(true);
                restored.SeekFrame(999);
                Require(restored.CurrentFrame == 1 && restored.IsPlaying, "Seeking did not clamp or preserve active playback.");
                restored.ReplaceImage(seed);
                Require(restored.ExportAnimation() == null && restored.FrameCount == 1 && !restored.IsPlaying, "Replacing a GIF with a still image left animation running.");
            }
        }

        internal static void PinLifecycle()
        {
            using (Bitmap image = new Bitmap(80, 40))
            using (PinForm pin = new PinForm(image))
            {
                IntPtr handle = pin.Handle;
                int hidden = 0, closed = 0;
                Point peerDelta = Point.Empty;
                pin.HiddenByUser += delegate { hidden++; };
                pin.FormClosed += delegate { closed++; };
                pin.MovePeersRequested += delegate(Point delta) { peerDelta.Offset(delta); };
                Point start = pin.Location;
                Key(pin, Keys.Right); Key(pin, Keys.Down);
                Require(pin.Location == new Point(start.X + 1, start.Y + 1) && peerDelta == new Point(1, 1), "Arrow movement or peer movement delta changed.");
                pin.Opacity = 0.5; pin.ScaleFactor = 0.75;
                Key(pin, Keys.Control | Keys.D0);
                Require(pin.Opacity == 1 && pin.ScaleFactor == 1, "Ctrl+0 did not reset both opacity and size.");
                Key(pin, Keys.Control | Keys.W);
                Require(pin.ClosedByUser && hidden == 1 && closed == 0 && !pin.IsDisposed, "Ctrl+W must close recoverably without destroying the pin.");
                pin.ClosedByUser = false;
                pin.Hide();
                Require(!pin.ClosedByUser && hidden == 1, "Global hiding was incorrectly treated as user closing.");
                Key(pin, Keys.Escape);
                Require(pin.ClosedByUser && hidden == 2 && !pin.IsDisposed, "Escape must retain the pin for recovery.");
                Key(pin, Keys.Shift | Keys.Escape);
                Require(pin.IsDisposed && closed == 1, "Shift+Escape must destroy the native pin window.");
            }
        }

        internal static void HtmlClipboard()
        {
            string fragment = "<div><b>Bold</b> <span style='color:#ff0000;background-color:#ffff00;font-size:24pt'>RED</span><br>한글 &amp; <i>italic</i></div><script>should-not-render</script>";
            using (ClipboardPayload payload = ClipboardContent.FromText(fragment, true))
            {
                Require(payload.SourceText.Contains("Bold RED") && payload.SourceText.Contains("한글 & italic") && !payload.SourceText.Contains("should-not-render"), "HTML text extraction lost content or included script text.");
                Require(payload.Image.Width == 680 && payload.Image.Height >= 80, "Styled HTML rendering returned unexpected dimensions.");
                int red = 0, yellow = 0;
                for (int y = 0; y < payload.Image.Height; y++)
                    for (int x = 0; x < payload.Image.Width; x++)
                    {
                        Color pixel = payload.Image.GetPixel(x, y);
                        if (pixel.R > 180 && pixel.G < 90 && pixel.B < 90) red++;
                        if (pixel.R > 180 && pixel.G > 180 && pixel.B < 90) yellow++;
                    }
                Require(red > 20 && yellow > 20, "HTML foreground/background styles were not rendered into pixels.");
            }
            DataObject data = new DataObject();
            data.SetData(DataFormats.Html, fragment);
            data.SetData(DataFormats.UnicodeText, "원본 plain text");
            List<ClipboardPayload> contents = ClipboardContent.Read(data, true, true);
            try { Require(contents.Count == 1 && contents[0].SourceText == "원본 plain text" && contents[0].Image.Width == 680, "HTML preference or source plain text was not retained."); }
            finally { foreach (ClipboardPayload item in contents) item.Dispose(); }
            contents = ClipboardContent.Read(data, false, true);
            try { Require(contents.Count == 1 && contents[0].SourceText == "원본 plain text" && contents[0].Image.GetPixel(0, 0).ToArgb() == Ui.Surface.ToArgb(), "Plain-text preference still rendered HTML."); }
            finally { foreach (ClipboardPayload item in contents) item.Dispose(); }

            string marker = "before<!--StartFragment--><b>한글</b><!--EndFragment-->after";
            Require(ClipboardContent.ExtractFragment(marker) == "<b>한글</b>", "CF_HTML comment markers were parsed incorrectly.");
            string prefix = "Version:1.0\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\n<html><body>앞";
            string selected = "<b>한글</b>";
            int start = Encoding.UTF8.GetByteCount(prefix), end = start + Encoding.UTF8.GetByteCount(selected);
            prefix = prefix.Replace("StartFragment:0000000000", "StartFragment:" + start.ToString("D10"))
                .Replace("EndFragment:0000000000", "EndFragment:" + end.ToString("D10"));
            Require(ClipboardContent.ExtractFragment(prefix + selected + "뒤</body></html>") == selected, "CF_HTML UTF-8 byte offsets were treated as character offsets.");
        }

        internal static void Tga(string directory)
        {
            Directory.CreateDirectory(directory);
            byte[] top = BuildTga(2, 2, 2, 24, 0x20, new byte[] { 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255 }, null);
            byte[] bottom = BuildTga(2, 2, 2, 24, 0, new byte[] { 255, 0, 0, 255, 255, 255, 0, 0, 255, 0, 255, 0 }, null);
            byte[] right = BuildTga(2, 2, 2, 24, 0x30, new byte[] { 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 0, 0 }, null);
            foreach (byte[] data in new byte[][] { top, bottom, right })
                using (MemoryStream stream = new MemoryStream(data))
                using (Bitmap bitmap = TgaImage.Decode(stream))
                {
                    Pixel(bitmap, 0, 0, Color.Red); Pixel(bitmap, 1, 0, Color.Lime);
                    Pixel(bitmap, 0, 1, Color.Blue); Pixel(bitmap, 1, 1, Color.White);
                    Require(stream.CanRead, "TGA decoder unexpectedly closed its caller's stream.");
                }
            string path = Path.Combine(directory, "fixture.tga"); File.WriteAllBytes(path, top);
            using (ClipboardPayload file = ClipboardContent.FromFile(path)) { Pixel(file.Image, 0, 0, Color.Red); Require(file.Image.Size == new Size(2, 2), "TGA clipboard file integration changed dimensions."); }
            byte[] rle = BuildTga(10, 3, 1, 24, 0x20, new byte[] { 0x81, 0, 0, 255, 0, 0, 255, 0 }, null);
            using (MemoryStream stream = new MemoryStream(rle))
            using (Bitmap bitmap = TgaImage.Decode(stream)) { Pixel(bitmap, 0, 0, Color.Red); Pixel(bitmap, 1, 0, Color.Red); Pixel(bitmap, 2, 0, Color.Lime); }
            byte[] alpha = BuildTga(2, 1, 1, 32, 0x28, new byte[] { 32, 64, 128, 127 }, null);
            using (MemoryStream stream = new MemoryStream(alpha))
            using (Bitmap bitmap = TgaImage.Decode(stream)) Pixel(bitmap, 0, 0, Color.FromArgb(127, 128, 64, 32));
            byte[] palette = BuildTga(1, 2, 1, 8, 0x20, new byte[] { 1, 0 }, new byte[] { 255, 0, 0, 0, 255, 255 });
            using (MemoryStream stream = new MemoryStream(palette))
            using (Bitmap bitmap = TgaImage.Decode(stream)) { Pixel(bitmap, 0, 0, Color.Yellow); Pixel(bitmap, 1, 0, Color.Blue); }
            Expect<InvalidDataException>(delegate
            {
                using (MemoryStream stream = new MemoryStream(BuildTga(10, 3, 1, 24, 0x20, new byte[] { 0x83, 0, 0, 255 }, null)))
                using (Bitmap ignored = TgaImage.Decode(stream)) { }
            }, "TGA RLE packet beyond the image boundary was accepted.");
            Expect<EndOfStreamException>(delegate
            {
                using (MemoryStream stream = new MemoryStream(BuildTga(2, 2, 2, 24, 0x20, new byte[] { 0, 0, 255 }, null)))
                using (Bitmap ignored = TgaImage.Decode(stream)) { }
            }, "Truncated TGA pixels were accepted.");
        }

        internal static void HistoryBounds(string directory)
        {
            Storage store = new Storage(directory);
            store.Settings.HistoryLimit = 1;
            using (Bitmap image = new Bitmap(32, 18))
            {
                string older = Path.Combine(store.HistoryDirectory, "20000101-old.png"); image.Save(older, ImageFormat.Png);
                Storage.WriteXml(older + ".xml", new CaptureRecord { File = Path.GetFileName(older), ScreenBounds = new Rectangle(-500, 30, 32, 18) });
                Rectangle bounds = new Rectangle(-1920, 175, 32, 18);
                string latest = store.AddHistory(image, bounds);
                Require(new Storage(directory).HistoryBounds(latest) == bounds, "History metadata did not preserve monitor origin and capture dimensions.");
                Require(!File.Exists(older) && !File.Exists(older + ".xml"), "History retention left orphan metadata or old pixels.");
                Require(store.HistoryBounds(Path.Combine(store.HistoryDirectory, "legacy.png")) == Rectangle.Empty, "Missing legacy metadata did not return empty bounds.");
                File.WriteAllText(latest + ".xml", "<broken>");
                Require(store.HistoryBounds(latest) == Rectangle.Empty, "Invalid history metadata was not handled gracefully.");
            }
        }

        internal static void GroupRoundTrip(string directory)
        {
            Storage store = new Storage(directory);
            ImageGroup group = new ImageGroup { Id = Guid.NewGuid().ToString("N"), Name = "개인 메모 & 사진" };
            string id = Guid.NewGuid().ToString("N");
            PinRecord record = new PinRecord { Id = id, GroupId = group.Id, X = -900, Y = 31, Width = 84, Height = 49,
                ScaleFactor = 0.25, Opacity = 0.65, TopMost = false, SourceText = "원문 <tag> & 메모", HasAnimation = true,
                AnimationTransform = "1", AnimationFrame = 1, AnimationPlaying = false, AnimationSpeed = 2.5 };
            byte[] gif = TwoFrameGif();
            using (Bitmap original = new Bitmap(32, 18, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(original)) g.Clear(Color.FromArgb(35, 78, 121));
                original.SetPixel(4, 6, Color.Magenta);
                original.Save(store.PinPath(id), ImageFormat.Png);
                File.WriteAllBytes(store.PinPath(id) + ".gif", gif);
                string path = Path.Combine(directory, "roundtrip.chachagroup");
                GroupFiles.Export(path, store, group, new PinRecord[] { record });
                int received = 0;
                Bitmap retained = null;
                try
                {
                    GroupArchive loaded = GroupFiles.Read(path, delegate(PinRecord item, Bitmap pixels, byte[] animation)
                    {
                        received++; retained = new Bitmap(pixels);
                        Require(item.Id == id && item.GroupId == group.Id && item.X == -900 && item.ScaleFactor == 0.25 && item.Opacity == 0.65 && !item.TopMost, "Group pin placement or display state changed.");
                        Require(item.SourceText == record.SourceText && item.AnimationTransform == "1" && item.AnimationFrame == 1 && item.AnimationSpeed == 2.5 && !item.AnimationPlaying, "Group source text or GIF playback state changed.");
                        BytesEqual(gif, animation);
                    });
                    Require(loaded.Name == group.Name && loaded.Images.Count == 1 && received == 1, "Group manifest or callback count changed.");
                    PixelsEqual(original, retained);
                    using (ZipArchive archive = ZipFile.OpenRead(path))
                        Require(archive.Entries.Count == 3 && archive.GetEntry("manifest.xml") != null && archive.GetEntry(id + ".png") != null && archive.GetEntry(id + ".gif") != null, "Exported group is missing its manifest or image assets.");
                }
                finally { if (retained != null) retained.Dispose(); }
            }
        }

        internal static void GroupValidation(string directory)
        {
            Directory.CreateDirectory(directory);
            string id = Guid.NewGuid().ToString("N");
            string path = Path.Combine(directory, "invalid.chachagroup");
            MakeArchive(path, null, null);
            RejectArchive(path, "Missing group manifest was accepted.");
            MakeArchive(path, new GroupArchive { Name = "Missing PNG", Images = new List<PinRecord> { new PinRecord { Id = id } } }, null);
            RejectArchive(path, "Missing group image asset was accepted.");
            MakeArchive(path, new GroupArchive { Name = "Traversal", Images = new List<PinRecord> { new PinRecord { Id = "../outside" } } }, null);
            RejectArchive(path, "Group path traversal ID was accepted.");
            MakeArchive(path, new GroupArchive { Name = "Null record", Images = new List<PinRecord> { null } }, null);
            RejectArchive(path, "Null group image record was not rejected cleanly.");
            List<PinRecord> tooMany = new List<PinRecord>();
            for (int i = 0; i < 101; i++) tooMany.Add(new PinRecord { Id = Guid.NewGuid().ToString("N") });
            MakeArchive(path, new GroupArchive { Name = "Too many", Images = tooMany }, null);
            RejectArchive(path, "Group image count limit was not enforced.");
            using (Bitmap image = new Bitmap(2, 1))
            using (MemoryStream png = new MemoryStream())
            {
                image.Save(png, ImageFormat.Png);
                MakeArchive(path, new GroupArchive { Name = "Missing GIF", Images = new List<PinRecord> { new PinRecord { Id = id, HasAnimation = true } } },
                    new Dictionary<string, byte[]> { { id + ".png", png.ToArray() } });
                RejectArchive(path, "Group marked animated silently accepted a missing GIF source.");
            }
        }

        internal static void EditorChangeLifecycle()
        {
            using (Bitmap desktop = new Bitmap(800, 600, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(desktop))
                {
                    g.Clear(Color.FromArgb(27, 40, 53));
                    g.FillRectangle(Brushes.White, 160, 120, 320, 200);
                    g.FillRectangle(Brushes.MediumAquamarine, 180, 145, 90, 65);
                }
                Rectangle desktopBounds = new Rectangle(-800, 0, 800, 600);
                Rectangle selectionBounds = new Rectangle(-640, 120, 320, 200);
                using (Bitmap source = desktop.Clone(new Rectangle(160, 120, 320, 200), PixelFormat.Format32bppArgb))
                using (EditorForm editor = new EditorForm(source, desktop, desktopBounds, selectionBounds))
                {
                    Realize(editor); EditorCall(editor, "FitImage");
                    Require(editor.IsInline && !editor.HasChanges, "A new inline editor must start unchanged.");
                    Require(editor.CurrentScreenBounds == selectionBounds, "Inline screen bounds lost the negative desktop origin.");
                    using (Bitmap exported = editor.ExportImage()) PixelsEqual(source, exported);
                    Draw(editor, "Rectangle", new Point(195, 150), new Point(375, 265));
                    Require(editor.HasChanges, "Adding an annotation did not mark the editor changed.");
                    using (Bitmap annotated = editor.ExportImage())
                    {
                        EditorCall(editor, "Undo");
                        Require(!editor.HasChanges, "Undoing the first annotation did not return to unchanged state.");
                        using (Bitmap exported = editor.ExportImage()) PixelsEqual(source, exported);
                        EditorCall(editor, "Redo");
                        Require(editor.HasChanges, "Redoing an annotation did not restore changed state.");
                        using (Bitmap exported = editor.ExportImage()) PixelsEqual(annotated, exported);
                    }
                    EditorCall(editor, "ClearEdits");
                    Require(!editor.HasChanges && editor.CurrentScreenBounds == selectionBounds, "Clearing annotations did not restore source state and screen bounds.");
                    Rectangle crop = new Rectangle(17, 13, 160, 100);
                    EditorCall(editor, "CropTo", crop);
                    Require(editor.HasChanges && editor.CurrentScreenBounds == new Rectangle(-623, 133, 160, 100), "Cropping did not update changed state and physical screen bounds.");
                    using (Bitmap expected = source.Clone(crop, PixelFormat.Format32bppArgb))
                    {
                        using (Bitmap exported = editor.ExportImage()) PixelsEqual(expected, exported);
                        EditorCall(editor, "Undo");
                        Require(!editor.HasChanges && editor.CurrentScreenBounds == selectionBounds, "Undo crop did not restore clean state and original location.");
                        EditorCall(editor, "Redo");
                        Require(editor.HasChanges && editor.CurrentScreenBounds == new Rectangle(-623, 133, 160, 100), "Redo crop did not restore changed state and cropped location.");
                        using (Bitmap exported = editor.ExportImage()) PixelsEqual(expected, exported);
                    }
                    EditorCall(editor, "ClearEdits");
                    EditorCall(editor, "Undo");
                    EditorCall(editor, "Redo");
                    Require(!editor.HasChanges && editor.CurrentScreenBounds == selectionBounds, "Clear left a stale crop or redo operation.");
                    using (Bitmap exported = editor.ExportImage()) PixelsEqual(source, exported);
                    Require(!editor.Visible, "Inline regression unexpectedly displayed a window.");
                }
            }
        }

        internal static void PinEditorMapping()
        {
            using (Bitmap source = new Bitmap(640, 480, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(source))
                {
                    g.Clear(Color.White);
                    g.FillRectangle(Brushes.MediumAquamarine, 60, 160, 240, 220);
                }
                source.SetPixel(41, 26, Color.Red);
                Rectangle display = new Rectangle(320, 190, 400, 299);
                using (EditorForm editor = new EditorForm(source, display))
                {
                    Realize(editor); EditorCall(editor, "FitImage");
                    Require(editor.IsPinEditing && !editor.IsInline && !editor.HasChanges, "Pin editor did not start in a clean pin-editing mode.");
                    Require(editor.CurrentScreenBounds == display, "Pin editor changed the displayed image bounds.");
                    PointF origin = (PointF)editor.GetType().GetProperty("ImageOrigin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor, null);
                    Require(Math.Abs(origin.X + editor.Left - display.X) < 0.01 && Math.Abs(origin.Y + editor.Top - display.Y) < 0.01, "Pin native image origin moved when the toolbars were laid out.");
                    PointF mapped = (PointF)EditorCall(editor, "ToImage", new Point((int)origin.X + 200, (int)origin.Y + 149), false);
                    Require(Math.Abs(mapped.X - 320) < 0.01 && Math.Abs(mapped.Y - 149 * 480.0 / 299) < 0.01, "Pin coordinates ignored the separately rounded horizontal/vertical display scales.");
                    using (Bitmap exported = editor.ExportImage()) PixelsEqual(source, exported);
                    Require(editor.Region != null && editor.Region.IsVisible((int)origin.X + 1, (int)origin.Y + 1), "Pin editor window region excludes its source image.");
                    Require(!editor.Region.IsVisible(1, editor.Height / 2), "Unused area around the pin is no longer click-through.");
                    Rectangle crop = new Rectangle(40, 25, 320, 240);
                    EditorCall(editor, "CropTo", crop);
                    Require(editor.HasChanges && editor.CurrentScreenBounds == new Rectangle(345, 206, 200, 150), "Pin crop lost its source-relative physical location or rounded display size.");
                    using (Bitmap expected = source.Clone(crop, PixelFormat.Format32bppArgb))
                    using (Bitmap exported = editor.ExportImage()) PixelsEqual(expected, exported);
                    EditorCall(editor, "Undo");
                    Require(!editor.HasChanges && editor.CurrentScreenBounds == display, "Pin crop undo did not restore clean state and native alignment.");
                    EditorCall(editor, "Redo");
                    Require(editor.HasChanges, "Pin crop redo did not restore changed state.");
                    EditorCall(editor, "ClearEdits");
                    Require(!editor.HasChanges && editor.CurrentScreenBounds == display, "Pin clear did not restore its original source bounds.");
                    Draw(editor, "Arrow", new Point((int)origin.X + 120, (int)origin.Y + 80), new Point((int)origin.X + 220, (int)origin.Y + 150));
                    Require(editor.HasChanges, "Drawing on a scaled pin did not mark it changed.");
                    using (Bitmap expected = editor.ExportImage())
                    {
                        Bitmap retained = null;
                        int committed = 0;
                        editor.ImageCommitted += delegate(Bitmap bitmap) { committed++; retained = bitmap; };
                        try
                        {
                            editor.CommitChanges();
                            Require(committed == 1 && editor.IsDisposed, "Applying pin edits did not emit exactly one owned image and close the editor.");
                            PixelsEqual(expected, retained);
                            Require(retained.Size == source.Size, "Applying a scaled pin exported display pixels instead of native image pixels.");
                        }
                        finally { if (retained != null) retained.Dispose(); }
                    }
                }
            }
        }

        internal static void SettingsMigration(string directory)
        {
            string legacy = Path.Combine(directory, "legacy-default");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "settings.xml"), "<AppSettings><ToggleHotkey>Ctrl+F3</ToggleHotkey><CaptureHotkey>Ctrl+Shift+A</CaptureHotkey></AppSettings>");
            Storage migrated = new Storage(legacy);
            Require(migrated.Settings.ToggleHotkey == "Shift+F3", "Legacy default hide shortcut was not migrated.");
            Require(migrated.Settings.CaptureHotkey == "Ctrl+Shift+A", "Hide shortcut migration modified another custom shortcut.");
            migrated.SaveSettings();
            Require(new Storage(legacy).Settings.ToggleHotkey == "Shift+F3", "Migrated shortcut was not durable after saving.");
            string custom = Path.Combine(directory, "legacy-custom");
            Directory.CreateDirectory(custom);
            File.WriteAllText(Path.Combine(custom, "settings.xml"), "<AppSettings><ToggleHotkey>Alt+F8</ToggleHotkey></AppSettings>");
            Require(new Storage(custom).Settings.ToggleHotkey == "Alt+F8", "Legacy custom hide shortcut was overwritten.");
            string current = Path.Combine(directory, "current-custom");
            Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(current, "settings.xml"), "<AppSettings><SettingsVersion>2</SettingsVersion><ToggleHotkey>Ctrl+F3</ToggleHotkey></AppSettings>");
            Require(new Storage(current).Settings.ToggleHotkey == "Ctrl+F3", "Current-version Ctrl+F3 preference was incorrectly migrated.");
        }

        private static object EditorCall(EditorForm editor, string method, params object[] arguments)
        {
            return editor.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(editor, arguments);
        }

        private static void Realize(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) Realize(child);
            control.PerformLayout();
        }

        private static void Draw(EditorForm editor, string tool, Point start, Point end)
        {
            editor.SelectTool(tool);
            object canvas = editor.GetType().GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
            EditorCall(editor, "CanvasMouseDown", canvas, new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
            EditorCall(editor, "CanvasMouseMove", canvas, new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
            EditorCall(editor, "CanvasMouseUp", canvas, new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
        }

        private static void RejectArchive(string path, string message)
        {
            bool received = false;
            Expect<InvalidDataException>(delegate { GroupFiles.Read(path, delegate { received = true; }); }, message);
            Require(!received, "Malformed group data reached the import callback.");
        }

        private static void MakeArchive(string path, GroupArchive manifest, Dictionary<string, byte[]> assets)
        {
            using (FileStream output = new FileStream(path, FileMode.Create))
            using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                if (manifest != null)
                    using (Stream stream = archive.CreateEntry("manifest.xml").Open()) new XmlSerializer(typeof(GroupArchive)).Serialize(stream, manifest);
                if (assets != null)
                    foreach (KeyValuePair<string, byte[]> asset in assets)
                        using (Stream stream = archive.CreateEntry(asset.Key).Open()) stream.Write(asset.Value, 0, asset.Value.Length);
            }
        }

        private static byte[] BuildTga(int type, int width, int height, int depth, int descriptor, byte[] pixels, byte[] palette)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((byte)0); writer.Write((byte)(palette == null ? 0 : 1)); writer.Write((byte)type);
                writer.Write((ushort)0); writer.Write((ushort)(palette == null ? 0 : palette.Length / 3)); writer.Write((byte)(palette == null ? 0 : 24));
                writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)width); writer.Write((ushort)height);
                writer.Write((byte)depth); writer.Write((byte)descriptor);
                if (palette != null) writer.Write(palette);
                writer.Write(pixels); writer.Flush(); return stream.ToArray();
            }
        }

        private static byte[] TwoFrameGif()
        {
            // Two full 2x1 frames: red/green, then green/red. Delays are 100 and 200 ms.
            string hex = "47 49 46 38 39 61 02 00 01 00 80 00 00 FF 00 00 00 FF 00 21 F9 04 00 0A 00 00 00 2C 00 00 00 00 02 00 01 00 00 02 02 44 0A 00 21 F9 04 00 14 00 00 00 2C 00 00 00 00 02 00 01 00 00 02 02 0C 0A 00 3B";
            string[] tokens = hex.Split(' '); byte[] bytes = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++) bytes[i] = Convert.ToByte(tokens[i], 16);
            return bytes;
        }

        private static void Key(Form form, Keys keys)
        {
            MethodInfo method = form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
            Message message = Message.Create(IntPtr.Zero, 0x100, IntPtr.Zero, IntPtr.Zero);
            Require((bool)method.Invoke(form, new object[] { message, keys }), "Keyboard shortcut was not handled: " + keys);
        }

        private static void PixelsEqual(Bitmap expected, Bitmap actual)
        {
            Require(actual != null && expected.Size == actual.Size, "Image dimensions did not survive round-trip.");
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Image pixel changed at " + x + "," + y + ".");
        }

        private static void BytesEqual(byte[] expected, byte[] actual)
        {
            Require(actual != null && expected.Length == actual.Length, "Encoded asset length changed.");
            for (int i = 0; i < expected.Length; i++) Require(expected[i] == actual[i], "Encoded asset byte changed at " + i + ".");
        }

        private static void Pixel(Bitmap bitmap, int x, int y, Color expected)
        {
            Require(bitmap.GetPixel(x, y).ToArgb() == expected.ToArgb(), "Unexpected decoded pixel at " + x + "," + y + ".");
        }

        private static void Expect<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
