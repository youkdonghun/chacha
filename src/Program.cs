using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing.Printing;
using System.Globalization;
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
            int updateExitCode;
            if (UpdateService.TryHandleUpdate(args, out updateExitCode)) return updateExitCode;
            if (args.Contains("--self-test")) return SelfTests.Run(args);
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\ChachaCapture-54FB3242", out first))
            {
                if (!first)
                {
                    string runningVersion = HotkeyWindow.ExistingVersion();
                    Version oldVersion, newVersion;
                    if (args.Length == 0 && Version.TryParse(runningVersion, out oldVersion) &&
                        Version.TryParse(Application.ProductVersion, out newVersion) && oldVersion < newVersion)
                    {
                        HotkeyWindow.ShowExisting();
                        MessageBox.Show("현재 실행 중인 버전은 v" + oldVersion.ToString(3) + "입니다.\n방금 연 파일은 v" + newVersion.ToString(3) + "입니다.\n\n기존 앱의 트레이 메뉴에서 종료한 뒤 새 파일을 다시 실행해 주세요.",
                            "Chacha Capture · 이전 버전이 실행 중", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (!HotkeyWindow.SendCommand(args)) MessageBox.Show("Chacha Capture가 이미 실행 중입니다.\n트레이 아이콘에서 대시보드를 여세요.", "Chacha Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Report(e.Exception); };
                    using (CaptureApplication app = new CaptureApplication(args))
                    {
                        string updateError;
                        if (UpdateService.TryReadUpdateError(args, out updateError)) MessageBox.Show(updateError, "Chacha Capture · 업데이트 결과", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Application.Run(app);
                    }
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
        public bool UiTestMode { get; private set; }
        private readonly HotkeyWindow hotkeys;
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly List<PinForm> pins = new List<PinForm>();
        private readonly List<EditorForm> editors = new List<EditorForm>();
        private readonly HashSet<PinForm> editingPins = new HashSet<PinForm>();
        private readonly Dictionary<PinForm, EditorForm> pinEditors = new Dictionary<PinForm, EditorForm>();
        private readonly System.Windows.Forms.Timer persistTimer;
        private System.Windows.Forms.Timer captureTimer;
        private DashboardForm dashboard;
        private SettingsForm settingsForm;
        private UpdateForm updateForm;
        private CaptureOverlay overlay;
        private bool capturing;
        private bool restoring;
        private bool movingPeers;
        private string hotkeyConflict;
        public string CaptureDiagnosticText { get; private set; }
        private uint lastFileClipboardSequence;
        private bool hasPastedFiles;
        private readonly List<PinForm> closedPins = new List<PinForm>();
        private readonly Queue<string[]> commands = new Queue<string[]>();
        private readonly System.Windows.Forms.Timer commandTimer;
        public PinForm[] AllPins { get { return pins.Where(p => !p.IsDisposed).ToArray(); } }
        private IEnumerable<PinForm> ActivePins { get { return AllPins.Where(p => p.GroupId == Store.Settings.ActiveGroup); } }

        public CaptureApplication(string[] args)
        {
            UiTestMode = args.Contains("--ui-test");
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChachaCapture");
            // An isolated profile is useful when testing the actual executable.
            int profile = Array.IndexOf(args, "--data-dir");
            if (profile >= 0 && profile + 1 < args.Length) data = Path.GetFullPath(args[profile + 1]);
            Store = new Storage(data);
            CaptureDiagnosticText = "아직 캡처하지 않았습니다. 한 번 캡처한 뒤 확인해 주세요.";
            icon = Ui.CreateIcon(); hotkeys = new HotkeyWindow();
            hotkeys.Pressed += delegate(int id) { if (id == 0) ShowDashboard(); else if (id == 1) BeginCapture(0, false, false); else if (id == 2) PinClipboard(); else if (id == 3) TogglePins(); else if (id == 4) ToggleClickThrough(); else if (id == 5) NextGroup(); };
            commandTimer = new System.Windows.Forms.Timer { Interval = 50 };
            commandTimer.Tick += delegate { if (commands.Count == 0) { commandTimer.Stop(); return; } ProcessCommand(commands.Dequeue()); };
            hotkeys.CommandReceived += delegate(string[] command) { commands.Enqueue(command); commandTimer.Start(); };
            tray = new NotifyIcon { Icon = icon, Text = "Chacha Capture · 캡처하고 화면에 고정", Visible = true };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("새 영역 캡처", null, delegate { BeginCapture(0, false, false); });
            menu.Items.Add("전체 화면 캡처", null, delegate { BeginCapture(0, true, false); });
            menu.Items.Add("3초 후 캡처", null, delegate { BeginCapture(3, false, false); });
            menu.Items.Add("최근 영역 다시 캡처", null, delegate { BeginCapture(0, false, true); });
            menu.Items.Add("정확한 크기로 캡처…", null, delegate { CustomCapture(); });
            menu.Items.Add("화이트보드", null, delegate { Whiteboard(Color.White); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("클립보드 이미지 / 텍스트 고정", null, delegate { PinClipboard(); });
            menu.Items.Add("모든 고정 창 숨기기 / 표시", null, delegate { TogglePins(); });
            menu.Items.Add("고정 창 표시 및 클릭 통과 해제", null, delegate { ShowAllPins(); });
            menu.Items.Add("이미지 파일 열기", null, delegate { OpenImage(); });
            menu.Items.Add("이미지 그룹 관리…", null, delegate { ShowGroups(); });
            menu.Items.Add("다음 이미지 그룹", null, delegate { NextGroup(); });
            menu.Items.Add("커서 아래 이미지 클릭 통과", null, delegate { ToggleClickThrough(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("대시보드 · 캡처 기록", null, delegate { ShowDashboard(); });
            menu.Items.Add("설정", null, delegate { ShowSettings(); });
            menu.Items.Add("업데이트 확인…", null, delegate { ShowUpdates(); });
            menu.Items.Add("종료", null, delegate { Shutdown(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) BeginCapture(0, false, false); else if (e.Button == MouseButtons.Middle) PinClipboard(); };
            persistTimer = new System.Windows.Forms.Timer { Interval = 850 };
            persistTimer.Tick += delegate { persistTimer.Stop(); PersistPins(); };
            ApplySettings(); RestorePins();
            if (!args.Contains("--tray") && !Store.Settings.StartInTray) ShowDashboard();
            int fileIndex = Array.IndexOf(args, "--open");
            if (fileIndex >= 0 && fileIndex + 1 < args.Length) OpenImageFile(args[fileIndex + 1]);
            else if (args.Length > 0 && !args[0].StartsWith("--")) { commands.Enqueue(args); commandTimer.Start(); }
        }
        public void ApplySettings()
        {
            Store.SaveSettings();
            string conflict = hotkeys.Register(Store.Settings);
            if (!hotkeys.IsSuspended) hotkeyConflict = conflict;
            if (!String.IsNullOrEmpty(conflict)) Notify("단축키를 등록하지 못했습니다: " + conflict + "\n설정에서 다른 키를 지정하세요. 트레이 메뉴는 사용할 수 있습니다.");
            if (dashboard != null && !dashboard.IsDisposed) dashboard.RefreshSettings();
            foreach (EditorForm editor in editors) editor.SaveDirectory = Store.Settings.SaveFolder;
            foreach (PinForm pin in pins) pin.SaveDirectory = Store.Settings.SaveFolder;
            foreach (PinForm pin in pins) pin.QuickSaveDirectory = Store.Settings.QuickSaveFolder;
            foreach (EditorForm editor in editors) editor.QuickSaveDirectory = Store.Settings.QuickSaveFolder;
        }
        public void SetRunAtStartup(bool enabled)
        {
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("ChachaCapture", "\"" + Application.ExecutablePath + "\" --tray");
                else key.DeleteValue("ChachaCapture", false);
            }
        }
        public void ShowDashboard()
        {
            if (dashboard == null || dashboard.IsDisposed) dashboard = new DashboardForm(this);
            dashboard.RefreshHistory(); dashboard.Show(); dashboard.WindowState = FormWindowState.Normal; dashboard.Activate();
            if (!String.IsNullOrWhiteSpace(hotkeyConflict)) dashboard.SetStatus("단축키 충돌: " + hotkeyConflict + " · 설정에서 다른 키를 지정하거나 지워 주세요.");
        }
        public void ShowSettings()
        {
            if (settingsForm != null) { settingsForm.Activate(); return; }
            hotkeys.Suspend();
            try
            {
                using (SettingsForm form = new SettingsForm(this))
                {
                    settingsForm = form;
                    if (dashboard != null && dashboard.Visible) form.ShowDialog(dashboard); else form.ShowDialog();
                }
            }
            finally
            {
                settingsForm = null;
                if (!Exiting)
                {
                    string conflict = hotkeys.Resume(Store.Settings);
                    hotkeyConflict = conflict;
                    if (!String.IsNullOrEmpty(conflict)) Notify("단축키를 등록하지 못했습니다: " + conflict + "\n설정에서 다른 키를 지정하세요.");
                }
            }
        }
        public void ShowUpdates()
        {
            if (Exiting) return;
            if (updateForm != null && !updateForm.IsDisposed) { updateForm.Show(); updateForm.Activate(); return; }
            updateForm = new UpdateForm(this);
            updateForm.FormClosed += delegate { updateForm = null; };
            updateForm.Show(); updateForm.Activate();
        }
        public void Notify(string message)
        {
            tray.BalloonTipTitle = "Chacha Capture"; tray.BalloonTipText = message; tray.ShowBalloonTip(2500);
            if (dashboard != null && !dashboard.IsDisposed) dashboard.SetStatus(message.Replace("\n", " "));
        }
        public void BeginCapture(int seconds, bool fullscreen, bool repeat)
        { BeginCapture(seconds, fullscreen, repeat, Rectangle.Empty, null); }
        private void BeginCapture(double seconds, bool fullscreen, bool repeat, Rectangle requested, string output)
        {
            if (capturing || Exiting || settingsForm != null) return;
            if (repeat && (Store.Settings.LastWidth < 1 || Store.Settings.LastHeight < 1)) { Notify("먼저 영역을 한 번 캡처해 주세요."); return; }
            capturing = true;
            bool restoreDashboard = dashboard != null && dashboard.Visible;
            if (restoreDashboard) dashboard.Hide();
            // Fullscreen annotation/whiteboard windows must not become the next frozen desktop.
            List<EditorForm> hiddenEditors = editors.Where(e => !e.IsDisposed && e.Visible && e.WindowState != FormWindowState.Minimized).ToList();
            foreach (EditorForm editor in hiddenEditors) editor.Hide();
            captureTimer = new System.Windows.Forms.Timer { Interval = seconds > 0 ? Math.Max(1, (int)(seconds * 1000)) : 200 };
            captureTimer.Tick += delegate
            {
                captureTimer.Stop(); captureTimer.Dispose(); captureTimer = null;
                try
                {
                    Rectangle bounds; Bitmap cursor;
                    using (Bitmap desktop = CaptureOverlay.CaptureDesktopLayers(out bounds, out cursor, Store.Settings.CaptureMode))
                    using (cursor)
                    {
                        UpdateCaptureDiagnostic();
                        if ((fullscreen || repeat || !requested.IsEmpty) && output != null)
                        {
                            Rectangle captureBounds = fullscreen ? bounds : Rectangle.Intersect(bounds, repeat ? Store.Settings.LastSelection : requested);
                            if (captureBounds.Width < 1 || captureBounds.Height < 1) { Notify("최근 영역이 현재 화면 밖에 있습니다. 새 영역을 선택해 주세요."); capturing = false; RestoreCaptureEditors(hiddenEditors, true); if (restoreDashboard) ShowDashboard(); return; }
                            using (Bitmap composite = CaptureOverlay.ComposeDesktop(desktop, cursor, Store.Settings.IncludeCursor))
                            using (Bitmap image = composite.Clone(new Rectangle(captureBounds.X - bounds.X, captureBounds.Y - bounds.Y, captureBounds.Width, captureBounds.Height), PixelFormat.Format32bppArgb))
                            { RestoreCaptureEditors(hiddenEditors, false); CompleteOutput(image, captureBounds, output); }
                            capturing = false;
                            return;
                        }
                        Rectangle initial = fullscreen ? bounds : repeat ? Store.Settings.LastSelection : requested;
                        overlay = new CaptureOverlay(desktop, bounds, initial.IsEmpty ? Store.Settings.LastSelection : initial, cursor, Store.Settings.IncludeCursor);
                        overlay.AbortOnFocusLoss = Store.Settings.AbortOnFocusLoss;
                        overlay.CompleteOnSelection = output != null;
                        overlay.AutoDetectElements = Store.Settings.AutoDetectElements;
                        overlay.AutoFloatCapture = Store.Settings.AutoFloatCapture;
                        overlay.CaptureNotice = DesktopCapture.LastReport == null ? "" : DesktopCapture.LastReport.Notice;
                        List<CaptureHistoryItem> history = new List<CaptureHistoryItem>();
                        try
                        {
                            foreach (string path in Store.History().Take(25))
                            {
                                try { history.Add(new CaptureHistoryItem { Image = Storage.LoadBitmap(path), ScreenBounds = Store.HistoryBounds(path) }); } catch (IOException) { } catch (ArgumentException) { }
                            }
                            overlay.SetHistory(history);
                        }
                        finally { foreach (CaptureHistoryItem item in history) item.Dispose(); }
                        if (!initial.IsEmpty) overlay.SetSelection(initial);
                    }
                    CaptureResult pending = null;
                    overlay.Completed += delegate(CaptureResult result) { pending = result; };
                    overlay.FormClosed += delegate
                    {
                        overlay = null;
                        RestoreCaptureEditors(hiddenEditors, pending == null);
                        if (pending == null) { capturing = false; if (restoreDashboard && !Exiting) ShowDashboard(); return; }
                        // Finish the old overlay before showing any new topmost image/editor.
                        try { using (pending) { if (!Exiting) CompleteCapture(pending, output); } }
                        catch (Exception e) { Program.Report(e); }
                        finally { capturing = false; }
                    };
                    overlay.Show(); overlay.Activate();
                }
                catch (Exception e) { UpdateCaptureDiagnostic(); capturing = false; RestoreCaptureEditors(hiddenEditors, true); if (restoreDashboard) ShowDashboard(); Program.Report(e); }
            };
            captureTimer.Start();
        }
        private void UpdateCaptureDiagnostic()
        {
            DesktopCaptureReport report = DesktopCapture.LastReport;
            if (report == null) return;
            CaptureDiagnosticText = "Chacha Capture " + Application.ProductVersion + " · Windows x64" + Environment.NewLine +
                "선택한 방식: " + Store.Settings.CaptureMode + Environment.NewLine +
                "사용한 방식: " + (report.Backend ?? "실패") + Environment.NewLine +
                (report.Notice ?? "") + Environment.NewLine + report.Details;
            // Only API names, dimensions and errors are recorded, never captured image contents.
            try { File.WriteAllText(Path.Combine(Store.Root, "capture-diagnostics.txt"), CaptureDiagnosticText); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private void RestoreCaptureEditors(List<EditorForm> hidden, bool canceled)
        {
            if (Exiting) return;
            foreach (EditorForm editor in hidden)
            {
                if (editor.IsDisposed) continue;
                // Preserve unfinished work, but keep an older full-screen canvas off the new capture.
                if (!canceled) { editor.ShowInTaskbar = true; editor.WindowState = FormWindowState.Minimized; }
                editor.Show();
            }
        }
        private void CompleteCapture(CaptureResult result, string output)
        {
            if (result.Outcome == CaptureOutcome.Color) { Clipboard.SetText(result.ColorHex); Notify(result.ColorHex + " 복사됨 · " + Store.Settings.PinHotkey + " 키로 색상표 플로팅"); return; }
            if (result.Outcome == CaptureOutcome.Edit) { EditInline(result.Image, result.Desktop, result.DesktopBounds, result.ScreenBounds, result.InitialTool); return; }
            if (output != null) { CompleteOutput(result.Image, result.ScreenBounds, output); return; }
            bool clipboardFailed = false;
            if (result.Outcome == CaptureOutcome.Copy)
            {
                try { ClipboardImages.Copy(result.Image); Notify(Store.Settings.AutoFloatCapture ? "복사 완료 · 이미지를 플로팅 창으로 띄웠습니다." : "이미지를 클립보드에 복사했습니다."); }
                catch (ExternalException) { clipboardFailed = true; Notify("클립보드가 사용 중입니다. 캡처 이미지는 플로팅 창으로 보관했습니다."); }
            }
            else if (result.Outcome == CaptureOutcome.Save) { if (!SaveImage(result.Image)) return; }
            else if (result.Outcome == CaptureOutcome.QuickSave) QuickSave(result.Image);
            else if (result.Outcome == CaptureOutcome.Print) { if (!PrintImage(result.Image)) return; }
            if (result.Outcome == CaptureOutcome.Pin || Store.Settings.AutoFloatCapture || clipboardFailed) PinImage(result.Image, result.ScreenBounds);
            Store.Settings.LastSelection = result.ScreenBounds;
            RecordImage(result.Image, result.ScreenBounds, result.Outcome != CaptureOutcome.QuickSave);
            Store.SaveSettings();
        }
        private void RecordImage(Bitmap image) { RecordImage(image, Rectangle.Empty); }
        private void RecordImage(Bitmap image, Rectangle bounds, bool autoSave = true)
        {
            try { Store.AddHistory(image, bounds); if (autoSave && Store.Settings.AutoSave) QuickSave(image); if (dashboard != null && dashboard.Visible) dashboard.RefreshHistory(); }
            catch (Exception e) { Notify("캡처는 완료했지만 기록을 저장하지 못했습니다: " + e.Message); }
        }
        public void EditImage(Bitmap image)
        {
            EditorForm form = new EditorForm(image); form.SaveDirectory = Store.Settings.SaveFolder; form.Icon = (Icon)icon.Clone(); editors.Add(form);
            if (UiTestMode) form.ShowInTaskbar = true;
            form.QuickSaveDirectory = Store.Settings.QuickSaveFolder;
            form.ImageCommitted += delegate(Bitmap rendered) { using (rendered) RecordImage(rendered); };
            form.PinRequested += delegate(Bitmap rendered) { using (rendered) PinImage(rendered); };
            form.FormClosed += delegate { editors.Remove(form); };
            form.Show(); form.Activate();
        }
        private void EditInline(Bitmap image, Bitmap desktop, Rectangle desktopBounds, Rectangle imageBounds, string tool, bool whiteboard = false)
        {
            EditorForm form = new EditorForm(image, desktop, desktopBounds, imageBounds); form.SaveDirectory = Store.Settings.SaveFolder; form.QuickSaveDirectory = Store.Settings.QuickSaveFolder; editors.Add(form);
            if (UiTestMode) form.ShowInTaskbar = true;
            form.WhiteboardMode = whiteboard;
            form.AutoFloatCapture = Store.Settings.AutoFloatCapture;
            Bitmap floating = null; Rectangle floatingBounds = Rectangle.Empty;
            form.ImageCommitted += delegate(Bitmap rendered)
            {
                using (rendered)
                {
                    Rectangle current = form.CurrentScreenBounds;
                    if (Store.Settings.AutoFloatCapture && floating == null) { floating = (Bitmap)rendered.Clone(); floatingBounds = current; }
                    RecordImage(rendered, current); Store.Settings.LastSelection = current; Store.SaveSettings();
                }
            };
            form.PinRequested += delegate(Bitmap rendered) { if (floating != null) floating.Dispose(); floating = rendered; floatingBounds = form.CurrentScreenBounds; };
            form.FormClosed += delegate
            {
                editors.Remove(form);
                if (floating != null) { using (floating) { if (!Exiting) PinImage(floating, floatingBounds); } floating = null; }
            };
            form.SelectTool(String.IsNullOrEmpty(tool) ? "Move" : tool); form.Show(); form.Activate();
        }
        public void PinImage(Bitmap image)
        { PinImage(image, Rectangle.Empty); }
        public void PinImage(Bitmap image, Rectangle bounds)
        {
            PinForm pin = AddPin(image, Guid.NewGuid().ToString("N"));
            pin.ShowFloating(bounds); SchedulePersist();
        }
        private PinForm AddPin(Bitmap image, string id)
        {
            PinForm pin = new PinForm(image); pin.SaveDirectory = Store.Settings.SaveFolder; pin.Icon = (Icon)icon.Clone(); pin.PersistentId = id;
            if (UiTestMode) pin.ShowInTaskbar = true;
            pin.GroupId = Store.Settings.ActiveGroup;
            pin.QuickSaveDirectory = Store.Settings.QuickSaveFolder;
            pins.Add(pin);
            pin.EditRequested += delegate(Bitmap rendered)
            {
                using (rendered)
                {
                    if (editingPins.Contains(pin)) return;
                    Point position = pin.Location; double scale = pin.ScaleFactor; string sourceText = pin.SourceText;
                    EditorForm editor = new EditorForm(rendered, pin.ImageScreenBounds); editor.SaveDirectory = Store.Settings.SaveFolder; editor.Icon = (Icon)icon.Clone(); editors.Add(editor);
                    if (UiTestMode) editor.ShowInTaskbar = true;
                    editor.QuickSaveDirectory = Store.Settings.QuickSaveFolder;
                    editor.ImageCommitted += delegate(Bitmap edited) { using (edited) RecordImage(edited); };
                    editor.PinRequested += delegate(Bitmap edited) { edited.Dispose(); };
                    editor.FormClosed += delegate
                    {
                        if (!pin.IsDisposed && editor.HasChanges) using (Bitmap edited = editor.ExportImage()) { pin.ReplaceImage(edited); pin.SourceText = sourceText; pin.ScaleFactor = scale; pin.Location = position; }
                        editors.Remove(editor); editingPins.Remove(pin); pinEditors.Remove(pin);
                        if (!pin.IsDisposed && !Exiting && pin.GroupId == Store.Settings.ActiveGroup && !pin.ClosedByUser) pin.Show(); SchedulePersist();
                    };
                    editingPins.Add(pin); pinEditors.Add(pin, editor); pin.Hide();
                    editor.Show(); editor.Activate();
                }
            };
            pin.StateChanged += SchedulePersist;
            pin.ReplaceRequested += delegate { ReplaceFromClipboard(pin); };
            pin.SelectAllRequested += delegate { foreach (PinForm p in ActivePins) p.IsSelected = p.Visible; };
            pin.SelectionToggleRequested += delegate { pin.IsSelected = !pin.IsSelected; };
            pin.ManageGroupsRequested += ShowGroups;
            pin.PreferencesRequested += ShowSettings;
            pin.DropRequested += delegate(IDataObject data) { PasteData(data); };
            pin.MovePeersRequested += delegate(Point delta)
            {
                if (movingPeers || !pin.IsSelected) return;
                movingPeers = true;
                try { foreach (PinForm peer in ActivePins.Where(p => p != pin && p.Visible && p.IsSelected)) peer.Location = new Point(peer.Left + delta.X, peer.Top + delta.Y); }
                finally { movingPeers = false; }
            };
            pin.HiddenByUser += delegate
            {
                closedPins.Remove(pin); closedPins.Add(pin);
                while (closedPins.Count > Math.Max(0, Store.Settings.ClosedPinLimit)) { PinForm oldest = closedPins[0]; closedPins.RemoveAt(0); oldest.Close(); }
                SchedulePersist();
            };
            pin.FormClosed += delegate
            {
                pins.Remove(pin);
                closedPins.Remove(pin);
                if (!Exiting) { try { File.Delete(Store.PinPath(pin.PersistentId)); File.Delete(Store.PinPath(pin.PersistentId) + ".gif"); } catch (IOException) { } SchedulePersist(); }
            };
            return pin;
        }
        public void PinClipboard()
        {
            PinForm recovered = closedPins.LastOrDefault(p => !p.IsDisposed && p.GroupId == Store.Settings.ActiveGroup);
            if (recovered != null) { closedPins.Remove(recovered); recovered.ClosedByUser = false; recovered.RestoreInteractive(); SchedulePersist(); return; }
            try
            {
                IDataObject data = Clipboard.GetDataObject(); uint sequence = GetClipboardSequenceNumber();
                string[] files = data == null ? null : data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && Store.Settings.PasteFilePaths && hasPastedFiles && sequence == lastFileClipboardSequence)
                { foreach (string file in files.Take(30)) using (ClipboardPayload payload = ClipboardContent.FromText(file, false)) AddPayload(payload); hasPastedFiles = false; }
                else { PasteData(data); hasPastedFiles = files != null; lastFileClipboardSequence = sequence; }
            }
            catch (Exception e) { Program.Report(e); }
        }
        public void RestorePin(PinForm pin)
        {
            if (pin == null || pin.IsDisposed) return;
            if (pin.GroupId != Store.Settings.ActiveGroup) SwitchGroup(pin.GroupId);
            pin.ClosedByUser = false; closedPins.Remove(pin); pin.RestoreInteractive(); SchedulePersist();
        }
        private void PasteData(IDataObject data)
        {
            List<ClipboardPayload> payloads = ClipboardContent.Read(data, Store.Settings.PreferHtml, Store.Settings.PasteFilePaths);
            try { if (payloads.Count == 0) Notify("클립보드에 이미지, 텍스트 또는 이미지 파일을 복사해 주세요."); foreach (ClipboardPayload payload in payloads) AddPayload(payload); }
            finally { foreach (ClipboardPayload payload in payloads) payload.Dispose(); }
        }
        private PinForm AddPayload(ClipboardPayload payload)
        {
            if (payload.Image == null) return null;
            PinForm pin = AddPin(payload.Image, Guid.NewGuid().ToString("N"));
            if (payload.Animation != null) pin.LoadAnimation(payload.Animation);
            pin.SourceText = payload.SourceText;
            pin.ShowFloating(); SchedulePersist(); return pin;
        }
        private void ReplaceFromClipboard(PinForm pin)
        {
            try
            {
                List<ClipboardPayload> content = ClipboardContent.Read(Clipboard.GetDataObject(), Store.Settings.PreferHtml, Store.Settings.PasteFilePaths);
                try { if (content.Count == 0 || content[0].Image == null) return; pin.ReplaceImage(content[0].Image); if (content[0].Animation != null) pin.LoadAnimation(content[0].Animation); pin.SourceText = content[0].SourceText; SchedulePersist(); }
                finally { foreach (ClipboardPayload payload in content) payload.Dispose(); }
            }
            catch (Exception e) { Program.Report(e); }
        }
        public void TogglePins()
        {
            CommitPinEditors();
            bool anyVisible = ActivePins.Any(p => p.Visible);
            foreach (PinForm pin in ActivePins) { if (anyVisible) pin.Hide(); else if (!pin.ClosedByUser) { pin.RestoreInteractive(); pin.Show(); } }
            SchedulePersist();
        }
        public void ShowAllPins()
        {
            CommitPinEditors();
            if (pins.Count == 0) { Notify("고정된 이미지가 없습니다. " + Store.Settings.PinHotkey + " 키로 클립보드 이미지를 고정하세요."); return; }
            foreach (PinForm pin in ActivePins) { pin.ClosedByUser = false; closedPins.Remove(pin); pin.RestoreInteractive(); pin.Show(); pin.BringToFront(); }
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
                    byte[] animation = pin.ExportAnimation();
                    if (animation != null) { File.WriteAllBytes(path + ".gif.tmp", animation); if (File.Exists(path + ".gif")) File.Replace(path + ".gif.tmp", path + ".gif", null); else File.Move(path + ".gif.tmp", path + ".gif"); }
                    else if (File.Exists(path + ".gif")) File.Delete(path + ".gif");
                    records.Add(new PinRecord { Id = pin.PersistentId, X = pin.Left, Y = pin.Top, Width = pin.Width, Height = pin.Height, ScaleFactor = pin.ScaleFactor, Opacity = pin.Opacity, Visible = pin.Visible || editingPins.Contains(pin), TopMost = pin.TopMost, GroupId = pin.GroupId, SourceText = pin.SourceText, ClosedByUser = pin.ClosedByUser, HasAnimation = animation != null, AnimationTransform = pin.AnimationTransformState, AnimationFrame = pin.CurrentFrame, AnimationPlaying = pin.IsPlaying, AnimationSpeed = pin.PlaybackSpeed });
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
                            pin.GroupId = Store.Groups.Any(g => g.Id == record.GroupId) ? record.GroupId : "default"; pin.ClosedByUser = record.ClosedByUser;
                            if (record.HasAnimation && File.Exists(Store.PinPath(record.Id) + ".gif"))
                            {
                                try { pin.LoadAnimatedFile(Store.PinPath(record.Id) + ".gif"); pin.AnimationTransformState = record.AnimationTransform; pin.SeekFrame(record.AnimationFrame); pin.SetPlaybackSpeed(record.AnimationSpeed); pin.SetPlaying(record.AnimationPlaying); }
                                catch (Exception e) { if (!(e is IOException || e is ArgumentException || e is OutOfMemoryException || e is InvalidDataException)) throw; pin.ReplaceImage(image); }
                            }
                            pin.SourceText = record.SourceText;
                            Rectangle work = Screen.FromPoint(new Point(record.X, record.Y)).WorkingArea;
                            int width = Math.Max(48, Math.Min(work.Width, record.Width)), height = Math.Max(48, Math.Min(work.Height, record.Height));
                            pin.ScaleFactor = record.ScaleFactor > 0 ? record.ScaleFactor : Math.Min((width - 4.0) / image.Width, (height - 4.0) / image.Height);
                            pin.Location = new Point(Math.Max(work.Left, Math.Min(work.Right - 48, record.X)), Math.Max(work.Top, Math.Min(work.Bottom - 48, record.Y)));
                            pin.Opacity = Math.Max(0.15, Math.Min(1, record.Opacity)); pin.TopMost = record.TopMost;
                            if (record.Visible && pin.GroupId == Store.Settings.ActiveGroup && !pin.ClosedByUser) pin.Show();
                            if (pin.ClosedByUser) closedPins.Add(pin);
                        }
                    }
                    catch (Exception e) { if (!(e is IOException || e is ArgumentException || e is OutOfMemoryException || e is InvalidDataException)) throw; }
                }
            }
            finally { restoring = false; }
        }
        public void OpenImage()
        {
            using (OpenFileDialog dlg = new OpenFileDialog { Title = "이미지 열기", Multiselect = true, Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.tga|모든 파일|*.*" }) if (dlg.ShowDialog() == DialogResult.OK) foreach (string file in dlg.FileNames) { if (Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase)) PinImageFile(file); else OpenImageFile(file); }
        }
        public void OpenImageFile(string path) { try { using (Bitmap image = Storage.LoadBitmap(path)) EditImage(image); } catch (Exception e) { Program.Report(e); } }
        public void PinImageFile(string path) { try { using (ClipboardPayload payload = ClipboardContent.FromFile(path)) AddPayload(payload); } catch (Exception e) { Program.Report(e); } }
        public void OpenSaveFolder() { try { Directory.CreateDirectory(Store.Settings.SaveFolder); Process.Start(Store.Settings.SaveFolder); } catch (Exception e) { Program.Report(e); } }
        public bool SaveImage(Bitmap image)
        {
            Directory.CreateDirectory(Store.Settings.SaveFolder);
            using (SaveFileDialog dlg = new SaveFileDialog { Title = "캡처 저장", InitialDirectory = Store.Settings.SaveFolder, FileName = "Chacha-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", Filter = "PNG 이미지|*.png|JPEG 이미지|*.jpg|BMP 이미지|*.bmp", AddExtension = true })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                image.Save(dlg.FileName, dlg.FilterIndex == 2 ? ImageFormat.Jpeg : dlg.FilterIndex == 3 ? ImageFormat.Bmp : ImageFormat.Png); Notify("이미지를 저장했습니다."); return true;
            }
        }
        public void QuickSave(Bitmap image)
        {
            Directory.CreateDirectory(Store.Settings.QuickSaveFolder);
            string file = Path.Combine(Store.Settings.QuickSaveFolder, "Chacha-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) + ".png");
            image.Save(file, ImageFormat.Png); Notify("빠른 저장 완료: " + Path.GetFileName(file));
        }
        public static bool PrintImage(Bitmap image)
        {
            using (PrintDocument document = new PrintDocument()) using (PrintDialog dialog = new PrintDialog { Document = document, UseEXDialog = true })
            {
                document.DocumentName = "Chacha Capture";
                document.PrintPage += delegate(object sender, PrintPageEventArgs e) { double scale = Math.Min((double)e.MarginBounds.Width / image.Width, (double)e.MarginBounds.Height / image.Height); e.Graphics.DrawImage(image, e.MarginBounds.Left, e.MarginBounds.Top, (int)(image.Width * scale), (int)(image.Height * scale)); e.HasMorePages = false; };
                if (dialog.ShowDialog() != DialogResult.OK) return false;
                document.Print(); return true;
            }
        }
        public void ToggleClickThrough()
        {
            Point cursor = Cursor.Position;
            PinForm under = ActivePins.LastOrDefault(p => p.Visible && p.Bounds.Contains(cursor));
            if (under != null) under.SetClickThrough(!under.ClickThrough);
            else foreach (PinForm pin in AllPins.Where(p => p.ClickThrough)) pin.SetClickThrough(false);
            SchedulePersist();
        }
        public void ShowGroups() { using (GroupsForm form = new GroupsForm(this)) form.ShowDialog(); }
        public ImageGroup CreateGroup(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("그룹 이름을 입력해 주세요.");
            ImageGroup group = new ImageGroup { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() }; Store.Groups.Add(group); Store.SaveGroups(); return group;
        }
        public void SwitchGroup(string id)
        {
            if (!Store.Groups.Any(g => g.Id == id)) return;
            CommitPinEditors();
            foreach (PinForm pin in AllPins) { pin.IsSelected = false; if (pin.GroupId == id && !pin.ClosedByUser) pin.Show(); else pin.Hide(); }
            Store.Settings.ActiveGroup = id; Store.SaveSettings(); SchedulePersist();
            Notify("이미지 그룹: " + Store.Groups.First(g => g.Id == id).Name);
        }
        public void NextGroup()
        {
            int index = Store.Groups.FindIndex(g => g.Id == Store.Settings.ActiveGroup); SwitchGroup(Store.Groups[(index + 1) % Store.Groups.Count].Id);
        }
        public void RemoveGroup(string id)
        {
            if (id == "default") return;
            foreach (PinForm pin in AllPins.Where(p => p.GroupId == id)) pin.GroupId = "default";
            Store.Groups.RemoveAll(g => g.Id == id); Store.SaveGroups(); SwitchGroup("default");
        }
        public void MoveSelectedToGroup(string id)
        {
            if (!Store.Groups.Any(g => g.Id == id)) return;
            foreach (PinForm pin in AllPins.Where(p => p.IsSelected)) { pin.GroupId = id; pin.IsSelected = false; if (id != Store.Settings.ActiveGroup) pin.Hide(); }
            SchedulePersist();
        }
        public void ExportGroup(string path, string id)
        {
            PersistPins(); GroupFiles.Export(path, Store, Store.Groups.First(g => g.Id == id), Store.ReadPins().Where(p => p.GroupId == id));
        }
        private sealed class ImportItem : IDisposable
        {
            public PinRecord Record; public Bitmap Image; public byte[] Animation;
            public void Dispose() { if (Image != null) Image.Dispose(); }
        }
        public void ImportGroup(string path)
        {
            List<ImportItem> items = new List<ImportItem>();
            try
            {
                GroupArchive imported = GroupFiles.Read(path, delegate(PinRecord record, Bitmap image, byte[] gif) { if (gif != null) using (AnimatedImageSource probe = new AnimatedImageSource(gif)) { using (Bitmap frame = probe.GetFrame(0)) { } } items.Add(new ImportItem { Record = record, Image = new Bitmap(image), Animation = gif }); });
                ImageGroup group = CreateGroup(String.IsNullOrWhiteSpace(imported.Name) ? "가져온 그룹" : imported.Name);
                foreach (ImportItem item in items)
                {
                    PinForm pin = AddPin(item.Image, Guid.NewGuid().ToString("N")); pin.GroupId = group.Id;
                    if (item.Animation != null) { pin.LoadAnimation(item.Animation); pin.AnimationTransformState = item.Record.AnimationTransform; pin.SeekFrame(item.Record.AnimationFrame); pin.SetPlaybackSpeed(item.Record.AnimationSpeed); pin.SetPlaying(item.Record.AnimationPlaying); }
                    pin.SourceText = item.Record.SourceText;
                    pin.ScaleFactor = item.Record.ScaleFactor > 0 ? item.Record.ScaleFactor : 1;
                    pin.Opacity = Math.Max(.15, Math.Min(1, item.Record.Opacity)); pin.TopMost = item.Record.TopMost;
                    Rectangle work = Screen.FromPoint(new Point(item.Record.X, item.Record.Y)).WorkingArea;
                    pin.Location = new Point(Math.Max(work.Left, Math.Min(work.Right - 48, item.Record.X)), Math.Max(work.Top, Math.Min(work.Bottom - 48, item.Record.Y)));
                }
                SwitchGroup(group.Id);
            }
            finally { foreach (ImportItem item in items) item.Dispose(); }
        }
        public void Whiteboard(Color color)
        {
            Rectangle bounds = Screen.FromPoint(Cursor.Position).Bounds;
            using (Bitmap board = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics graphics = Graphics.FromImage(board)) graphics.Clear(color);
                EditInline(board, board, bounds, bounds, "Pen", true);
            }
        }
        public void CustomCapture()
        {
            using (Form dialog = new Form { Text = "정확한 영역 캡처", ClientSize = new Size(430, 266), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, Font = Ui.Font(10, FontStyle.Regular), BackColor = Ui.Background, ForeColor = Ui.Text, MaximizeBox = false, MinimizeBox = false })
            {
                Rectangle initial = Store.Settings.LastSelection; if (initial.IsEmpty) initial = new Rectangle(Cursor.Position, new Size(640, 480));
                string[] labels = { "X 좌표", "Y 좌표", "너비", "높이", "지연 (초)" }; int[] values = { initial.X, initial.Y, initial.Width, initial.Height, 0 }; NumericUpDown[] fields = new NumericUpDown[5];
                for (int i = 0; i < fields.Length; i++)
                {
                    Label label = Ui.Label(labels[i], 10, Ui.Text); label.SetBounds(20, 18 + i * 37, 180, 25); dialog.Controls.Add(label);
                    fields[i] = new NumericUpDown { Location = new Point(202, 17 + i * 37), Width = 200, Minimum = i < 2 ? -32768 : i == 4 ? 0 : 1, Maximum = i == 4 ? 60 : 32768, Value = values[i], BackColor = Ui.Surface, ForeColor = Ui.Text }; dialog.Controls.Add(fields[i]);
                }
                Button ok = Ui.Button("캡처 시작", true, null); ok.SetBounds(254, 213, 148, 35); ok.DialogResult = DialogResult.OK; dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                if (dialog.ShowDialog() == DialogResult.OK) BeginCapture((int)fields[4].Value, false, false, new Rectangle((int)fields[0].Value, (int)fields[1].Value, (int)fields[2].Value, (int)fields[3].Value), null);
            }
        }
        private void CompleteOutput(Bitmap image, Rectangle bounds, string output)
        {
            if (String.IsNullOrEmpty(output)) { EditImage(image); return; }
            if (output == "clipboard") ClipboardImages.Copy(image);
            else if (output == "pin") PinImage(image);
            else if (output == "quick-save") QuickSave(image);
            else if (output == "printer") { if (!PrintImage(image)) return; }
            else if (output == "file-dialog") { if (!SaveImage(image)) return; }
            else if (output != "success") { string file = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(file)); string ext = Path.GetExtension(file).ToLowerInvariant(); image.Save(file, ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png); }
            Store.Settings.LastSelection = bounds; Store.SaveSettings(); RecordImage(image, bounds, output != "quick-save");
        }
        public void ProcessCommand(string[] args)
        {
            try
            {
                if (args.Length == 0) { ShowDashboard(); return; }
                string command = args[0].ToLowerInvariant();
                if (command == "exit") { Shutdown(); return; }
                if (command == "snip")
                {
                    double delay = 0; string output = Argument(args, "-o") ?? Argument(args, "--output"); string time = Argument(args, "--delay");
                    if (time != null) delay = Math.Max(0, Math.Min(60, Double.Parse(time, CultureInfo.InvariantCulture)));
                    if (args.Contains("--custom")) { CustomCapture(); return; }
                    Rectangle area = Rectangle.Empty; int index = Array.IndexOf(args, "--area");
                    if (index >= 0 && index + 4 < args.Length) area = new Rectangle(Int32.Parse(args[index + 1]), Int32.Parse(args[index + 2]), Int32.Parse(args[index + 3]), Int32.Parse(args[index + 4]));
                    index = Array.IndexOf(args, "--size");
                    if (index >= 0 && index + 2 < args.Length) { int w = Int32.Parse(args[index + 1]), h = Int32.Parse(args[index + 2]); area = new Rectangle(Cursor.Position.X - w / 2, Cursor.Position.Y - h / 2, w, h); }
                    if (args.Contains("--active-screen")) area = Screen.FromPoint(Cursor.Position).Bounds;
                    if (args.Contains("--active-window")) { NativeRect rect; if (GetWindowRect(GetForegroundWindow(), out rect)) area = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom); }
                    if (!area.IsEmpty && (area.Width < 1 || area.Height < 1)) throw new ArgumentException("영역 크기는 1픽셀 이상이어야 합니다.");
                    BeginCapture(delay, args.Contains("--full"), args.Contains("--last"), area, output); return;
                }
                if (command == "paste")
                {
                    string plain = Argument(args, "--plain"), html = Argument(args, "--html");
                    if (plain != null || html != null) { using (ClipboardPayload payload = ClipboardContent.FromText(plain ?? html, html != null)) AddPayload(payload); }
                    else
                    {
                        int index = Array.IndexOf(args, "--files");
                        if (index >= 0) { string baseFolder = Environment.CurrentDirectory; for (int i = index + 1; i < args.Length && !args[i].StartsWith("--"); i++) { string file = Path.IsPathRooted(args[i]) ? args[i] : Path.Combine(baseFolder, args[i]); if (Directory.Exists(file)) baseFolder = file; else PinImageFile(file); } }
                        else PinClipboard();
                    }
                    int position = Array.IndexOf(args, "--pos"); if (position >= 0 && position + 2 < args.Length && pins.Count > 0) pins[pins.Count - 1].Location = new Point(Int32.Parse(args[position + 1]), Int32.Parse(args[position + 2])); return;
                }
                if (command == "toggle-images") { TogglePins(); return; }
                if (command == "show-images") { ShowAllPins(); return; }
                if (command == "empty-group") { CommitPinEditors(); foreach (PinForm pin in ActivePins.ToArray()) pin.Close(); return; }
                if (command == "show-tray-menu") { tray.ContextMenuStrip.Show(Cursor.Position); return; }
                if (command == "hide-images") { CommitPinEditors(); foreach (PinForm pin in ActivePins) pin.Hide(); SchedulePersist(); return; }
                if (command == "toggle-click-through") { ToggleClickThrough(); return; }
                if (command == "no-click-through") { foreach (PinForm pin in AllPins) pin.SetClickThrough(false); return; }
                if (command == "switch-groups" || command == "show-group-manager") { ShowGroups(); return; }
                if (command == "switch-group") { if (args.Length < 2) NextGroup(); else { ImageGroup group = Store.Groups.FirstOrDefault(g => g.Name == args[1]); if (group != null) SwitchGroup(group.Id); } return; }
                if (command == "create-group") { string name = args.Length > 1 ? args[1] : GroupsForm.Prompt("새 그룹 이름", ""); if (name != null) SwitchGroup(CreateGroup(name).Id); return; }
                if (command == "whiteboard") { Color color = Color.White; string value = Argument(args, "--color"); if (value != null) color = ColorTranslator.FromHtml(value); Whiteboard(color); return; }
                if (command == "open-preferences") { ShowSettings(); return; }
                if (command == "--open" && args.Length > 1) { OpenImageFile(args[1]); return; }
                ShowDashboard();
            }
            catch (Exception e) { Notify("명령을 실행하지 못했습니다: " + e.Message); }
        }
        private static string Argument(string[] args, string option) { int index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        public void ShutdownForUpdate()
        {
            foreach (EditorForm editor in editors.ToArray())
            {
                if (editor.IsDisposed) continue;
                if (!editor.IsPinEditing && (!editor.IsInline || !Store.Settings.AutoFloatCapture))
                    using (Bitmap image = editor.ExportImage()) PinImage(image, editor.CurrentScreenBounds);
                editor.CommitChanges();
            }
            Shutdown();
        }
        public void Shutdown()
        {
            if (Exiting) return;
            CommitPinEditors();
            persistTimer.Stop();
            try { PersistPins(); Store.SaveSettings(); }
            catch (Exception e) { Notify("설정을 저장하지 못했습니다: " + e.Message); }
            Exiting = true;
            if (updateForm != null && !updateForm.IsDisposed) updateForm.Close();
            if (captureTimer != null) { captureTimer.Stop(); captureTimer.Dispose(); captureTimer = null; }
            if (overlay != null) overlay.Close();
            foreach (EditorForm e in editors.ToArray()) e.Close();
            foreach (PinForm p in pins.ToArray()) p.Close();
            if (dashboard != null) dashboard.Close();
            tray.Visible = false; ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { commandTimer.Dispose(); persistTimer.Dispose(); hotkeys.Dispose(); tray.Dispose(); icon.Dispose(); }
            base.Dispose(disposing);
        }
        private void CommitPinEditors() { foreach (EditorForm editor in pinEditors.Values.ToArray()) if (!editor.IsDisposed) editor.CommitChanges(); }
    }
}
