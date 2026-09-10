using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ChachaCapture
{
    public static class Ui
    {
        public static readonly Color Background = Color.FromArgb(16, 21, 30);
        public static readonly Color Surface = Color.FromArgb(25, 33, 45);
        public static readonly Color Border = Color.FromArgb(43, 55, 71);
        public static readonly Color Accent = Color.FromArgb(94, 234, 196);
        public static readonly Color Muted = Color.FromArgb(148, 163, 184);
        public static readonly Color Text = Color.FromArgb(236, 242, 248);
        public static Font Font(float size, FontStyle style) { return new Font("맑은 고딕", size, style); }
        public static Label Label(string text, float size, Color color)
        {
            return new Label { Text = text, Font = Font(size, FontStyle.Regular), ForeColor = color, AutoSize = true, BackColor = Color.Transparent };
        }
        public static Button Button(string text, bool primary, EventHandler clicked)
        {
            Button b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Text, Font = Font(10, FontStyle.Bold), Cursor = Cursors.Hand, Height = 42, Margin = new Padding(0, 0, 10, 10), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(131, 248, 215) : Border;
            if (clicked != null) b.Click += clicked;
            return b;
        }
        public static Icon CreateIcon()
        {
            using (Bitmap b = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush bg = new SolidBrush(Background)) g.FillEllipse(bg, 1, 1, 62, 62);
                using (Pen p = new Pen(Accent, 6))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLines(p, new Point[] { new Point(17, 28), new Point(17, 17), new Point(28, 17) });
                    g.DrawLines(p, new Point[] { new Point(36, 17), new Point(47, 17), new Point(47, 28) });
                    g.DrawLines(p, new Point[] { new Point(47, 36), new Point(47, 47), new Point(36, 47) });
                    g.DrawLines(p, new Point[] { new Point(28, 47), new Point(17, 47), new Point(17, 36) });
                }
                using (SolidBrush dot = new SolidBrush(Color.White)) g.FillEllipse(dot, 28, 28, 8, 8);
                IntPtr handle = b.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    }

    public sealed class AppSettings
    {
        public string CaptureHotkey = "F1";
        public string PinHotkey = "F3";
        public string ToggleHotkey = "Shift+F3";
        public string ClickThroughHotkey = "Ctrl+Alt+F3";
        public string SwitchGroupHotkey = "Ctrl+Shift+F3";
        public int SettingsVersion = 2;
        public bool PreferHtml = true;
        public bool PasteFilePaths = true;
        public bool AutoDetectElements = true;
        public bool AbortOnFocusLoss = true;
        public bool AutoSave;
        public bool RunAtStartup;
        public int ClosedPinLimit = 1;
        public string ActiveGroup = "default";
        public string QuickSaveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Chacha Capture");
        public bool IncludeCursor;
        public bool RestorePins = true;
        public bool KeepHistory = true;
        public bool StartInTray;
        public int HistoryLimit = 50;
        public string SaveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Chacha Capture");
        public int LastX, LastY, LastWidth, LastHeight;
        [XmlIgnore] public Rectangle LastSelection { get { return new Rectangle(LastX, LastY, LastWidth, LastHeight); } set { LastX = value.X; LastY = value.Y; LastWidth = value.Width; LastHeight = value.Height; } }
    }

    public sealed class PinRecord
    {
        public string Id;
        public int X, Y, Width, Height;
        public double Opacity = 1;
        public double ScaleFactor;
        public bool Visible = true;
        public bool TopMost = true;
        public string GroupId = "default";
        public string SourceText;
        public bool ClosedByUser;
        public bool HasAnimation;
        public string AnimationTransform;
        public int AnimationFrame;
        public bool AnimationPlaying = true;
        public double AnimationSpeed = 1;
    }

    public sealed class ImageGroup
    {
        public string Id;
        public string Name;
        public override string ToString() { return Name; }
    }

    public sealed class CaptureRecord
    {
        public string File;
        public Rectangle ScreenBounds;
    }

    public sealed class Storage
    {
        public readonly string Root;
        public string HistoryDirectory { get { return Path.Combine(Root, "history"); } }
        public string PinsDirectory { get { return Path.Combine(Root, "pins"); } }
        public AppSettings Settings;
        public List<ImageGroup> Groups;
        public Storage(string root)
        {
            Root = root; Directory.CreateDirectory(Root); Directory.CreateDirectory(HistoryDirectory); Directory.CreateDirectory(PinsDirectory);
            Settings = ReadXml<AppSettings>(Path.Combine(Root, "settings.xml")) ?? new AppSettings();
            Settings.HistoryLimit = Math.Max(1, Math.Min(200, Settings.HistoryLimit));
            Settings.ClosedPinLimit = Math.Max(0, Math.Min(100, Settings.ClosedPinLimit));
            if (String.IsNullOrWhiteSpace(Settings.SaveFolder)) Settings.SaveFolder = new AppSettings().SaveFolder;
            if (String.IsNullOrWhiteSpace(Settings.QuickSaveFolder)) Settings.QuickSaveFolder = Settings.SaveFolder;
            // v1 shipped a different hide shortcut; migrate its default, retaining user-defined shortcuts.
            string settingsPath = Path.Combine(Root, "settings.xml");
            if (File.Exists(settingsPath) && !File.ReadAllText(settingsPath).Contains("<SettingsVersion>")) { if (Settings.ToggleHotkey == "Ctrl+F3") Settings.ToggleHotkey = "Shift+F3"; }
            Groups = ReadXml<List<ImageGroup>>(Path.Combine(Root, "groups.xml")) ?? new List<ImageGroup>();
            Groups = Groups.Where(g => g != null && !String.IsNullOrWhiteSpace(g.Id) && !String.IsNullOrWhiteSpace(g.Name)).GroupBy(g => g.Id).Select(g => g.First()).ToList();
            if (!Groups.Any(g => g.Id == "default")) Groups.Insert(0, new ImageGroup { Id = "default", Name = "기본 그룹" });
            if (!Groups.Any(g => g.Id == Settings.ActiveGroup)) Settings.ActiveGroup = "default";
        }
        public void SaveSettings() { WriteXml(Path.Combine(Root, "settings.xml"), Settings); }
        public void SaveGroups() { WriteXml(Path.Combine(Root, "groups.xml"), Groups); SaveSettings(); }
        public string[] History() { return Directory.GetFiles(HistoryDirectory, "*.png").OrderByDescending(Path.GetFileName).ToArray(); }
        public string AddHistory(Bitmap image)
        { return AddHistory(image, Rectangle.Empty); }
        public string AddHistory(Bitmap image, Rectangle screenBounds)
        {
            if (!Settings.KeepHistory) return null;
            string path = Path.Combine(HistoryDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png");
            image.Save(path, ImageFormat.Png);
            WriteXml(path + ".xml", new CaptureRecord { File = Path.GetFileName(path), ScreenBounds = screenBounds });
            foreach (string older in History().Skip(Settings.HistoryLimit)) { try { File.Delete(older); File.Delete(older + ".xml"); } catch (IOException) { } }
            return path;
        }
        public Rectangle HistoryBounds(string path) { CaptureRecord record = ReadXml<CaptureRecord>(path + ".xml"); return record == null ? Rectangle.Empty : record.ScreenBounds; }
        public List<PinRecord> ReadPins() { return ReadXml<List<PinRecord>>(Path.Combine(Root, "pins.xml")) ?? new List<PinRecord>(); }
        public void WritePins(List<PinRecord> pins) { WriteXml(Path.Combine(Root, "pins.xml"), pins); }
        public string PinPath(string id)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException("잘못된 고정 이미지 ID입니다.");
            return Path.Combine(PinsDirectory, parsed.ToString("N") + ".png");
        }
        public static Bitmap LoadBitmap(string path)
        {
            if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase)) return TgaImage.Load(path);
            using (Image image = Image.FromFile(path)) return new Bitmap(image);
        }
        public static void WriteXml<T>(string path, T value)
        {
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) new XmlSerializer(typeof(T)).Serialize(stream, value);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static T ReadXml<T>(string path) where T : class
        {
            try { if (File.Exists(path)) using (FileStream stream = File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(stream); }
            catch (Exception e) { if (!(e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException)) throw; }
            return null;
        }
    }

    public static class ClipboardImages
    {
        public static void Copy(Bitmap image)
        {
            using (MemoryStream png = new MemoryStream())
            {
                image.Save(png, ImageFormat.Png); png.Position = 0;
                DataObject data = new DataObject(); data.SetData("PNG", false, png); data.SetData(DataFormats.Bitmap, true, image);
                Clipboard.SetDataObject(data, true, 8, 70);
            }
        }
        public static Bitmap Read()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsImage()) using (Image image = Clipboard.GetImage()) { if (image != null) return new Bitmap(image); }
                    if (Clipboard.ContainsFileDropList())
                    {
                        foreach (string path in Clipboard.GetFileDropList())
                        {
                            try { return Storage.LoadBitmap(path); } catch (Exception e) { if (!(e is ArgumentException || e is IOException || e is OutOfMemoryException)) throw; }
                        }
                    }
                    if (Clipboard.ContainsText()) return RenderText(Clipboard.GetText());
                    return null;
                }
                catch (ExternalException) { if (attempt == 4) throw; Thread.Sleep(60); }
            }
            return null;
        }
        public static Bitmap RenderText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (text.Length > 16000) text = text.Substring(0, 16000) + "…";
            Color color;
            if (TryColor(text, out color))
            {
                Bitmap swatch = new Bitmap(300, 210);
                using (Graphics g = Graphics.FromImage(swatch))
                using (Brush fill = new SolidBrush(color))
                using (Font font = Ui.Font(17, FontStyle.Bold))
                {
                    g.Clear(Ui.Surface); g.FillRectangle(fill, 0, 0, 300, 148);
                    using (Brush fg = new SolidBrush(Ui.Text)) g.DrawString("#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"), font, fg, 20, 165);
                }
                return swatch;
            }
            using (Font font = Ui.Font(14, FontStyle.Regular))
            {
                SizeF measured;
                using (Bitmap probe = new Bitmap(1, 1)) using (Graphics g = Graphics.FromImage(probe)) measured = g.MeasureString(text, font, 650);
                int width = Math.Max(220, Math.Min(700, (int)Math.Ceiling(measured.Width) + 48));
                int height = Math.Max(100, Math.Min(1600, (int)Math.Ceiling(measured.Height) + 48));
                Bitmap note = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(note)) using (Brush brush = new SolidBrush(Ui.Text))
                {
                    g.Clear(Ui.Surface); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(text, font, brush, new RectangleF(24, 24, width - 48, height - 48));
                }
                return note;
            }
        }
        public static bool TryColor(string text, out Color color)
        {
            color = Color.Empty;
            text = (text ?? "").Trim();
            if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)) { if (!text.EndsWith(")")) return false; text = text.Substring(4, text.Length - 5).Trim(); }
            else if (text.Contains("(") || text.Contains(")")) return false;
            if (Regex.IsMatch(text, "^#([a-fA-F0-9]{3}|[a-fA-F0-9]{6})$"))
            {
                color = ColorTranslator.FromHtml(text); return true;
            }
            Match m = Regex.Match(text, @"^(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})$", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                m = Regex.Match(text, @"^([01]?\.\d+|[01])[\s,]+([01]?\.\d+|[01])[\s,]+([01]?\.\d+|[01])$", RegexOptions.IgnoreCase);
                if (!m.Success) return false;
                double red = Double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), green = Double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), blue = Double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (red > 1 || green > 1 || blue > 1) return false;
                color = Color.FromArgb((int)Math.Round(red * 255), (int)Math.Round(green * 255), (int)Math.Round(blue * 255)); return true;
            }
            int r = Int32.Parse(m.Groups[1].Value), g = Int32.Parse(m.Groups[2].Value), b = Int32.Parse(m.Groups[3].Value);
            if (r > 255 || g > 255 || b > 255) return false;
            color = Color.FromArgb(r, g, b); return true;
        }
    }

    public sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;
        public event Action<string[]> CommandReceived;
        private readonly List<int> registered = new List<int>();
        public HotkeyWindow() { CreateHandle(new CreateParams { Caption = "ChachaCapture.Hotkeys", Parent = new IntPtr(-3) }); }
        public static bool ShowExisting()
        {
            IntPtr window = FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, "ChachaCapture.Hotkeys");
            return window != IntPtr.Zero && PostMessage(window, 0x8001, IntPtr.Zero, IntPtr.Zero);
        }
        [StructLayout(LayoutKind.Sequential)] private struct CopyData { public IntPtr Tag; public int Length; public IntPtr Data; }
        public static bool SendCommand(string[] args)
        {
            IntPtr window = FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, "ChachaCapture.Hotkeys");
            if (window == IntPtr.Zero) return false;
            if (args.Length == 0) return ShowExisting();
            string content = String.Join("\n", args.Select(a => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(a))).ToArray());
            if (content.Length > 16000) return false;
            IntPtr data = Marshal.StringToHGlobalUni(content);
            try { CopyData payload = new CopyData { Tag = new IntPtr(0x43484341), Length = (content.Length + 1) * 2, Data = data }; IntPtr result; return SendMessageTimeout(window, 0x4A, IntPtr.Zero, ref payload, 2, 2000, out result) != IntPtr.Zero; }
            finally { Marshal.FreeHGlobal(data); }
        }
        public static bool Parse(string text, out uint modifiers, out uint key)
        {
            modifiers = 0; key = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= 2;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= 1;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= 4;
                else
                {
                    Keys parsed;
                    if (key != 0 || !Enum.TryParse<Keys>(part, true, out parsed) || !Enum.IsDefined(typeof(Keys), parsed) || (int)parsed < 8 || (int)parsed > 255) return false;
                    key = (uint)parsed;
                }
            }
            return key != 0 && key != (uint)Keys.ControlKey && key != (uint)Keys.ShiftKey && key != (uint)Keys.Menu;
        }
        public string Register(AppSettings s)
        {
            Unregister();
            List<string> errors = new List<string>();
            string[] values = { s.CaptureHotkey, s.PinHotkey, s.ToggleHotkey, s.ClickThroughHotkey, s.SwitchGroupHotkey };
            for (int i = 0; i < values.Length; i++)
            {
                uint modifiers, key;
                if (!Parse(values[i], out modifiers, out key) || !RegisterHotKey(Handle, i + 1, modifiers | 0x4000, key)) errors.Add(values[i] ?? "(빈 단축키)");
                else registered.Add(i + 1);
            }
            return String.Join(", ", errors.ToArray());
        }
        private void Unregister() { foreach (int id in registered) UnregisterHotKey(Handle, id); registered.Clear(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x4A && CommandReceived != null)
            {
                CopyData payload = (CopyData)Marshal.PtrToStructure(m.LParam, typeof(CopyData));
                if (payload.Tag == new IntPtr(0x43484341) && payload.Length > 0 && payload.Length <= 32002 && payload.Data != IntPtr.Zero)
                {
                    string command = Marshal.PtrToStringUni(payload.Data, payload.Length / 2).TrimEnd('\0');
                    try { CommandReceived(command.Split('\n').Select(a => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(a))).ToArray()); m.Result = new IntPtr(1); }
                    catch (FormatException) { m.Result = IntPtr.Zero; }
                    return;
                }
            }
            if (Pressed != null) { if (m.Msg == 0x312) Pressed(m.WParam.ToInt32()); else if (m.Msg == 0x8001) Pressed(0); } base.WndProc(ref m);
        }
        public void Dispose() { Unregister(); DestroyHandle(); }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, ref CopyData lParam, uint flags, uint timeout, out IntPtr result);
    }
}
