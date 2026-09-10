using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            EnableDpi();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--self-test")) return SelfTests.Run(args);
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\ChachaCapture-54FB3242", out first))
            {
                if (!first) { if (!HotkeyWindow.ShowExisting()) MessageBox.Show("Chacha Capture가 이미 실행 중입니다.\n트레이 아이콘을 두 번 클릭하거나 F1 키로 캡처하세요.", "Chacha Capture", MessageBoxButtons.OK, MessageBoxIcon.Information); return 0; }
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Report(e.Exception); };
                    using (CaptureApplication app = new CaptureApplication(args)) Application.Run(app);
                    return 0;
                }
                catch (Exception e) { Report(e); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
        internal static void Report(Exception e)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChachaCapture"); Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "error.log"), DateTime.Now.ToString("O") + " " + e + Environment.NewLine);
            }
            catch { }
            MessageBox.Show("작업을 완료하지 못했습니다. 다시 시도해 주세요.\n\n" + e.Message, "Chacha Capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        private static void EnableDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
        }
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    }

    public sealed class CaptureApplication : ApplicationContext
    {
        public readonly Storage Store;
        public bool Exiting { get; private set; }
        private readonly HotkeyWindow hotkeys;
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly List<PinForm> pins = new List<PinForm>();
        private readonly List<EditorForm> editors = new List<EditorForm>();
        private readonly System.Windows.Forms.Timer persistTimer;
        private System.Windows.Forms.Timer captureTimer;
        private DashboardForm dashboard;
        private CaptureOverlay overlay;
        private bool capturing;
        private bool restoring;

        public CaptureApplication(string[] args)
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChachaCapture");
            // An isolated profile is useful when testing the actual executable.
            int profile = Array.IndexOf(args, "--data-dir");
            if (profile >= 0 && profile + 1 < args.Length) data = Path.GetFullPath(args[profile + 1]);
            Store = new Storage(data);
            icon = Ui.CreateIcon(); hotkeys = new HotkeyWindow();
            hotkeys.Pressed += delegate(int id) { if (id == 0) ShowDashboard(); else if (id == 1) BeginCapture(0, false, false); else if (id == 2) PinClipboard(); else if (id == 3) TogglePins(); };
            tray = new NotifyIcon { Icon = icon, Text = "Chacha Capture · 캡처하고 화면에 고정", Visible = true };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("새 영역 캡처", null, delegate { BeginCapture(0, false, false); });
            menu.Items.Add("전체 화면 캡처", null, delegate { BeginCapture(0, true, false); });
            menu.Items.Add("3초 후 캡처", null, delegate { BeginCapture(3, false, false); });
            menu.Items.Add("최근 영역 다시 캡처", null, delegate { BeginCapture(0, false, true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("클립보드 이미지 / 텍스트 고정", null, delegate { PinClipboard(); });
            menu.Items.Add("모든 고정 창 숨기기 / 표시", null, delegate { TogglePins(); });
            menu.Items.Add("고정 창 표시 및 클릭 통과 해제", null, delegate { ShowAllPins(); });
            menu.Items.Add("이미지 파일 열기", null, delegate { OpenImage(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("대시보드 · 캡처 기록", null, delegate { ShowDashboard(); });
            menu.Items.Add("설정", null, delegate { ShowSettings(); });
            menu.Items.Add("종료", null, delegate { Shutdown(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowDashboard(); };
            persistTimer = new System.Windows.Forms.Timer { Interval = 850 };
            persistTimer.Tick += delegate { persistTimer.Stop(); PersistPins(); };
            ApplySettings(); RestorePins();
            if (!args.Contains("--tray") && !Store.Settings.StartInTray) ShowDashboard();
            int fileIndex = Array.IndexOf(args, "--open");
            if (fileIndex >= 0 && fileIndex + 1 < args.Length) OpenImageFile(args[fileIndex + 1]);
        }
        public void ApplySettings()
        {
            Store.SaveSettings();
            string conflict = hotkeys.Register(Store.Settings);
            if (!String.IsNullOrEmpty(conflict)) Notify("단축키를 등록하지 못했습니다: " + conflict + "\n설정에서 다른 키를 지정하세요. 트레이 메뉴는 사용할 수 있습니다.");
            if (dashboard != null && !dashboard.IsDisposed) dashboard.RefreshSettings();
            foreach (EditorForm editor in editors) editor.SaveDirectory = Store.Settings.SaveFolder;
            foreach (PinForm pin in pins) pin.SaveDirectory = Store.Settings.SaveFolder;
        }
        public void ShowDashboard()
        {
            if (dashboard == null || dashboard.IsDisposed) dashboard = new DashboardForm(this);
            dashboard.RefreshHistory(); dashboard.Show(); dashboard.WindowState = FormWindowState.Normal; dashboard.Activate();
        }
        public void ShowSettings() { using (SettingsForm form = new SettingsForm(this)) { if (dashboard != null && dashboard.Visible) form.ShowDialog(dashboard); else form.ShowDialog(); } }
        public void Notify(string message)
        {
            tray.BalloonTipTitle = "Chacha Capture"; tray.BalloonTipText = message; tray.ShowBalloonTip(2500);
            if (dashboard != null && !dashboard.IsDisposed) dashboard.SetStatus(message.Replace("\n", " "));
        }
        public void BeginCapture(int seconds, bool fullscreen, bool repeat)
        {
            if (capturing || Exiting) return;
            if (repeat && (Store.Settings.LastWidth < 1 || Store.Settings.LastHeight < 1)) { Notify("먼저 영역을 한 번 캡처해 주세요."); return; }
            capturing = true;
            bool restoreDashboard = dashboard != null && dashboard.Visible;
            if (restoreDashboard) dashboard.Hide();
            captureTimer = new System.Windows.Forms.Timer { Interval = seconds > 0 ? seconds * 1000 : 200 };
            captureTimer.Tick += delegate
            {
                captureTimer.Stop(); captureTimer.Dispose(); captureTimer = null;
                try
                {
                    Rectangle bounds;
                    using (Bitmap desktop = CaptureOverlay.CaptureDesktop(Store.Settings.IncludeCursor, out bounds))
                    {
                        if (fullscreen || repeat)
                        {
                            Rectangle captureBounds = fullscreen ? bounds : Rectangle.Intersect(bounds, Store.Settings.LastSelection);
                            if (captureBounds.Width < 1 || captureBounds.Height < 1) { Notify("최근 영역이 현재 화면 밖에 있습니다. 새 영역을 선택해 주세요."); capturing = false; if (restoreDashboard) ShowDashboard(); return; }
                            using (Bitmap image = desktop.Clone(new Rectangle(captureBounds.X - bounds.X, captureBounds.Y - bounds.Y, captureBounds.Width, captureBounds.Height), PixelFormat.Format32bppArgb))
                            { RecordImage(image); EditImage(image); }
                            capturing = false;
                            return;
                        }
                        overlay = new CaptureOverlay(desktop, bounds, Store.Settings.LastSelection);
                    }
                    bool completed = false;
                    overlay.Completed += delegate(CaptureResult result)
                    {
                        completed = true;
                        try
                        {
                            using (result)
                            {
                                if (result.Outcome == CaptureOutcome.Color) { Clipboard.SetText(result.ColorHex); Notify(result.ColorHex + " 복사됨 · " + Store.Settings.PinHotkey + " 키로 색상표 고정"); return; }
                                Store.Settings.LastSelection = result.ScreenBounds; Store.SaveSettings();
                                RecordImage(result.Image);
                                if (result.Outcome == CaptureOutcome.Copy) { ClipboardImages.Copy(result.Image); Notify("이미지를 클립보드에 복사했습니다."); }
                                else if (result.Outcome == CaptureOutcome.Pin) PinImage(result.Image);
                                else if (result.Outcome == CaptureOutcome.Save) SaveImage(result.Image);
                                else EditImage(result.Image);
                            }
                        }
                        catch (Exception e) { Program.Report(e); }
                    };
                    overlay.FormClosed += delegate { overlay = null; capturing = false; if (!completed && restoreDashboard && !Exiting) ShowDashboard(); };
                    overlay.Show(); overlay.Activate();
                }
                catch (Exception e) { capturing = false; if (restoreDashboard) ShowDashboard(); Program.Report(e); }
            };
            captureTimer.Start();
        }
        private void RecordImage(Bitmap image)
        {
            try { Store.AddHistory(image); if (dashboard != null && dashboard.Visible) dashboard.RefreshHistory(); }
            catch (Exception e) { Notify("캡처는 완료했지만 기록을 저장하지 못했습니다: " + e.Message); }
        }
        public void EditImage(Bitmap image)
        {
            EditorForm form = new EditorForm(image); form.SaveDirectory = Store.Settings.SaveFolder; form.Icon = (Icon)icon.Clone(); editors.Add(form);
            form.ImageCommitted += delegate(Bitmap rendered) { using (rendered) RecordImage(rendered); };
            form.PinRequested += delegate(Bitmap rendered) { using (rendered) PinImage(rendered); };
            form.FormClosed += delegate { editors.Remove(form); };
            form.Show(); form.Activate();
        }
        public void PinImage(Bitmap image)
        {
            PinForm pin = AddPin(image, Guid.NewGuid().ToString("N"));
            pin.Show(); SchedulePersist();
        }
        private PinForm AddPin(Bitmap image, string id)
        {
            PinForm pin = new PinForm(image); pin.SaveDirectory = Store.Settings.SaveFolder; pin.Icon = (Icon)icon.Clone(); pin.PersistentId = id;
            pins.Add(pin);
            pin.EditRequested += delegate(Bitmap rendered)
            {
                using (rendered)
                {
                    EditorForm editor = new EditorForm(rendered); editor.SaveDirectory = Store.Settings.SaveFolder; editor.Icon = (Icon)icon.Clone(); editors.Add(editor);
                    editor.ImageCommitted += delegate(Bitmap edited) { using (edited) RecordImage(edited); };
                    editor.PinRequested += delegate(Bitmap edited) { using (edited) { if (!pin.IsDisposed) { pin.ReplaceImage(edited); pin.Show(); SchedulePersist(); } else PinImage(edited); } };
                    editor.FormClosed += delegate { editors.Remove(editor); };
                    editor.Show(); editor.Activate();
                }
            };
            pin.StateChanged += SchedulePersist;
            pin.FormClosed += delegate
            {
                pins.Remove(pin);
                if (!Exiting) { try { File.Delete(Store.PinPath(pin.PersistentId)); } catch (IOException) { } SchedulePersist(); }
            };
            return pin;
        }
        public void PinClipboard()
        {
            try { using (Bitmap image = ClipboardImages.Read()) { if (image == null) Notify("클립보드에 이미지, 텍스트 또는 이미지 파일을 복사해 주세요."); else PinImage(image); } }
            catch (Exception e) { Program.Report(e); }
        }
        public void TogglePins()
        {
            bool anyVisible = pins.Any(p => !p.IsDisposed && p.Visible);
            foreach (PinForm pin in pins.ToArray()) { if (pin.IsDisposed) continue; if (anyVisible) pin.Hide(); else { pin.RestoreInteractive(); pin.Show(); } }
            SchedulePersist();
        }
        public void ShowAllPins()
        {
            if (pins.Count == 0) { Notify("고정된 이미지가 없습니다. " + Store.Settings.PinHotkey + " 키로 클립보드 이미지를 고정하세요."); return; }
            foreach (PinForm pin in pins.ToArray()) { if (!pin.IsDisposed) { pin.RestoreInteractive(); pin.Show(); pin.BringToFront(); } }
            SchedulePersist();
        }
        private void SchedulePersist() { if (restoring || Exiting) return; persistTimer.Stop(); persistTimer.Start(); }
        private void PersistPins()
        {
            if (restoring) return;
            try
            {
                List<PinRecord> records = new List<PinRecord>();
                foreach (PinForm pin in pins.ToArray())
                {
                    if (pin.IsDisposed) continue;
                    string path = Store.PinPath(pin.PersistentId);
                    using (Bitmap image = pin.ExportImage()) image.Save(path + ".tmp", ImageFormat.Png);
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
                    records.Add(new PinRecord { Id = pin.PersistentId, X = pin.Left, Y = pin.Top, Width = pin.Width, Height = pin.Height, ScaleFactor = pin.ScaleFactor, Opacity = pin.Opacity, Visible = pin.Visible, TopMost = pin.TopMost });
                }
                Store.WritePins(records);
            }
            catch (Exception e) { Notify("고정 창 상태를 저장하지 못했습니다: " + e.Message); }
        }
        private void RestorePins()
        {
            if (!Store.Settings.RestorePins) return;
            restoring = true;
            try
            {
                foreach (PinRecord record in Store.ReadPins().Take(100))
                {
                    try
                    {
                        using (Bitmap image = Storage.LoadBitmap(Store.PinPath(record.Id)))
                        {
                            PinForm pin = AddPin(image, record.Id); pin.StartPosition = FormStartPosition.Manual;
                            Rectangle work = Screen.FromPoint(new Point(record.X, record.Y)).WorkingArea;
                            int width = Math.Max(48, Math.Min(work.Width, record.Width)), height = Math.Max(48, Math.Min(work.Height, record.Height));
                            pin.ScaleFactor = record.ScaleFactor > 0 ? record.ScaleFactor : Math.Min((width - 4.0) / image.Width, (height - 4.0) / image.Height);
                            pin.Location = new Point(Math.Max(work.Left, Math.Min(work.Right - 48, record.X)), Math.Max(work.Top, Math.Min(work.Bottom - 48, record.Y)));
                            pin.Opacity = Math.Max(0.15, Math.Min(1, record.Opacity)); pin.TopMost = record.TopMost;
                            if (record.Visible) pin.Show();
                        }
                    }
                    catch (Exception e) { if (!(e is IOException || e is ArgumentException || e is OutOfMemoryException || e is InvalidDataException)) throw; }
                }
            }
            finally { restoring = false; }
        }
        public void OpenImage()
        {
            using (OpenFileDialog dlg = new OpenFileDialog { Title = "편집할 이미지 열기", Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|모든 파일|*.*" }) if (dlg.ShowDialog() == DialogResult.OK) OpenImageFile(dlg.FileName);
        }
        public void OpenImageFile(string path) { try { using (Bitmap image = Storage.LoadBitmap(path)) EditImage(image); } catch (Exception e) { Program.Report(e); } }
        public void PinImageFile(string path) { try { using (Bitmap image = Storage.LoadBitmap(path)) PinImage(image); } catch (Exception e) { Program.Report(e); } }
        public void OpenSaveFolder() { try { Directory.CreateDirectory(Store.Settings.SaveFolder); Process.Start(Store.Settings.SaveFolder); } catch (Exception e) { Program.Report(e); } }
        public void SaveImage(Bitmap image)
        {
            Directory.CreateDirectory(Store.Settings.SaveFolder);
            using (SaveFileDialog dlg = new SaveFileDialog { Title = "캡처 저장", InitialDirectory = Store.Settings.SaveFolder, FileName = "Chacha-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", Filter = "PNG 이미지|*.png|JPEG 이미지|*.jpg|BMP 이미지|*.bmp", AddExtension = true })
            {
                if (dlg.ShowDialog() == DialogResult.OK) { image.Save(dlg.FileName, dlg.FilterIndex == 2 ? ImageFormat.Jpeg : dlg.FilterIndex == 3 ? ImageFormat.Bmp : ImageFormat.Png); Notify("이미지를 저장했습니다."); }
            }
        }
        public void Shutdown()
        {
            if (Exiting) return;
            persistTimer.Stop();
            try { PersistPins(); Store.SaveSettings(); }
            catch (Exception e) { Notify("설정을 저장하지 못했습니다: " + e.Message); }
            Exiting = true;
            if (captureTimer != null) { captureTimer.Stop(); captureTimer.Dispose(); captureTimer = null; }
            if (overlay != null) overlay.Close();
            foreach (EditorForm e in editors.ToArray()) e.Close();
            foreach (PinForm p in pins.ToArray()) p.Close();
            if (dashboard != null) dashboard.Close();
            tray.Visible = false; ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { persistTimer.Dispose(); hotkeys.Dispose(); tray.Dispose(); icon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
