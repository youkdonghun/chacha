using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ChachaCapture
{
    public sealed class DashboardForm : Form
    {
        private readonly CaptureApplication app;
        private readonly FlowLayoutPanel history;
        private readonly Label count;
        private readonly Label shortcutLabel;
        private readonly Label status;
        private Label emptyHelp;
        public DashboardForm(CaptureApplication controller)
        {
            app = controller;
            SuspendLayout();
            Text = "Chacha Capture · " + Ui.VersionLabel;
            Icon = Ui.CreateIcon();
            BackColor = Ui.Background; ForeColor = Ui.Text;
            Font = Ui.Font(10, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1060, 750); MinimumSize = new Size(900, 650);
            Size = new Size(Math.Min(Width, Screen.PrimaryScreen.WorkingArea.Width), Math.Min(Height, Screen.PrimaryScreen.WorkingArea.Height));
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(30, 22, 30, 18), BackColor = Ui.Background };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            Controls.Add(root);

            Panel header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label logo = Ui.Label("CHACHA", 17, Ui.Text); logo.Font = Ui.Font(17, FontStyle.Bold); logo.Location = new Point(0, 5); header.Controls.Add(logo);
            Label edition = Ui.Label(Ui.VersionLabel + "  ·  나만의 캡처 도구", 8, Ui.Muted); edition.Location = new Point(145, 15); header.Controls.Add(edition);
            FlowLayoutPanel navigation = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 468, WrapContents = false, Padding = new Padding(0, 3, 0, 0), Margin = Padding.Empty };
            Button groups = Ui.Button("이미지 그룹", false, delegate { app.ShowGroups(); }); groups.Width = 108; navigation.Controls.Add(groups);
            Button pins = Ui.Button("플로팅 창 보기", false, delegate { app.ShowAllPins(); }); pins.Width = 132; navigation.Controls.Add(pins);
            Button updates = Ui.Button("업데이트", false, delegate { app.ShowUpdates(); }); updates.Width = 106; navigation.Controls.Add(updates);
            Button settings = Ui.Button("설정", false, delegate { app.ShowSettings(); }); settings.Width = 82; settings.Margin = Padding.Empty; navigation.Controls.Add(settings);
            header.Controls.Add(navigation);
            root.Controls.Add(header, 0, 0);

            Panel hero = new HeroPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(24) };
            Label eyebrow = Ui.Label("필요할 때, 바로 꺼내 쓰세요", 9, Ui.Accent); eyebrow.Location = new Point(26, 20); hero.Controls.Add(eyebrow);
            Label title = Ui.Label("찰칵 담고, 화면 위에 띄워요.", 24, Ui.Text); title.Font = Ui.Font(24, FontStyle.Bold); title.Location = new Point(23, 47); hero.Controls.Add(title);
            Label subtitle = Ui.Label("필요한 부분을 캡처하고, 설명을 더하고, 작업 옆에 띄워 두세요.", 10, Ui.Muted); subtitle.Location = new Point(27, 101); hero.Controls.Add(subtitle);
            Button capture = Ui.Button("＋   새 영역 캡처", true, delegate { app.BeginCapture(0, false, false); }); capture.SetBounds(26, 142, 164, 39); hero.Controls.Add(capture);
            shortcutLabel = Ui.Label("", 9, Ui.Muted); shortcutLabel.Location = new Point(208, 153); hero.Controls.Add(shortcutLabel);
            shortcutLabel.AutoSize = false; shortcutLabel.Height = 22; shortcutLabel.AutoEllipsis = true;
            hero.Resize += delegate { shortcutLabel.Width = Math.Max(40, hero.ClientSize.Width - shortcutLabel.Left - 20); };
            root.Controls.Add(hero, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0), WrapContents = false };
            Button paste = Ui.Button("클립보드 플로팅", false, delegate { app.PinClipboard(); }); paste.Width = 148; actions.Controls.Add(paste);
            Button file = Ui.Button("이미지 열기", false, delegate { app.OpenImage(); }); file.Width = 116; actions.Controls.Add(file);
            Button full = Ui.Button("전체 화면", false, delegate { app.BeginCapture(0, true, false); }); full.Width = 112; actions.Controls.Add(full);
            Button delay = Ui.Button("3초 후 캡처", false, delegate { app.BeginCapture(3, false, false); }); delay.Width = 118; actions.Controls.Add(delay);
            Button repeat = Ui.Button("최근 영역 재캡처", false, delegate { app.BeginCapture(0, false, true); }); repeat.Width = 156; actions.Controls.Add(repeat);
            Button whiteboard = Ui.Button("화이트보드", false, delegate { app.Whiteboard(Color.White); }); whiteboard.Width = 112; actions.Controls.Add(whiteboard);
            root.Controls.Add(actions, 0, 2);

            TableLayoutPanel library = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 16, 0, 0) };
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); library.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Panel libraryHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label recent = Ui.Label("최근 캡처", 12, Ui.Text); recent.Font = Ui.Font(12, FontStyle.Bold); libraryHeader.Controls.Add(recent);
            count = Ui.Label("", 9, Ui.Muted); count.Location = new Point(100, 5); libraryHeader.Controls.Add(count);
            Button folder = Ui.Button("저장 폴더 열기  ↗", false, delegate { app.OpenSaveFolder(); }); folder.Dock = DockStyle.Right; folder.Width = 162; folder.Height = 28; folder.Font = Ui.Font(9, FontStyle.Regular); libraryHeader.Controls.Add(folder);
            library.Controls.Add(libraryHeader, 0, 0);
            history = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0), BackColor = Ui.Background };
            library.Controls.Add(history, 0, 1); root.Controls.Add(library, 0, 3);
            Panel footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 7, 0, 0) };
            status = Ui.Label("●  준비됨   ·   창을 닫아도 트레이에서 계속 실행됩니다.", 9, Ui.Muted); status.AutoSize = false; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; footer.Controls.Add(status);
            Button exit = Ui.Button("종료", false, delegate { app.Shutdown(); }); exit.Font = Ui.Font(8, FontStyle.Regular); exit.Width = 84; exit.Dock = DockStyle.Right; footer.Controls.Add(exit);
            root.Controls.Add(footer, 0, 4);
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += delegate(object sender, DragEventArgs e) { string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null && paths.Length > 0) app.OpenImageFile(paths[0]); };
            Shown += delegate { RefreshHistory(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!app.Exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            RefreshSettings();
            ResumeLayout(true);
        }
        public void RefreshSettings()
        {
            shortcutLabel.Text = HotkeyLabel(app.Store.Settings.CaptureHotkey, "캡처") + "    ·    " + HotkeyLabel(app.Store.Settings.PinHotkey, "플로팅") + "    ·    " + HotkeyLabel(app.Store.Settings.ToggleHotkey, "숨기기 / 표시");
            if (emptyHelp != null && !emptyHelp.IsDisposed) emptyHelp.Text = EmptyHelpText();
        }
        private string EmptyHelpText() { return (String.IsNullOrWhiteSpace(app.Store.Settings.CaptureHotkey) ? "위의 새 영역 캡처를 눌러 시작하세요." : "위의 새 영역 캡처를 누르거나 " + app.Store.Settings.CaptureHotkey + " 키로 시작하세요.") + "\n이미지 파일을 이 창에 끌어다 놓아 편집할 수도 있습니다."; }
        private static string HotkeyLabel(string key, string action) { return String.IsNullOrWhiteSpace(key) ? action + " (미지정)" : key + "  " + action; }
        public void SetStatus(string text) { status.Text = "●  " + text; }
        public void RefreshHistory()
        {
            if (IsDisposed) return;
            history.SuspendLayout();
            emptyHelp = null;
            while (history.Controls.Count > 0) { Control c = history.Controls[0]; history.Controls.RemoveAt(0); c.Dispose(); }
            string[] paths = app.Store.History(); count.Text = paths.Length + "개 · 이 PC에 저장됨";
            if (paths.Length == 0)
            {
                Panel empty = new RoundedPanel { Width = Math.Max(100, history.ClientSize.Width - 20), Height = 170, BackColor = Ui.Surface };
                Label first = Ui.Label("아직 캡처한 이미지가 없어요.", 15, Ui.Text); first.Location = new Point(26, 32); empty.Controls.Add(first);
                emptyHelp = Ui.Label(EmptyHelpText(), 10, Ui.Muted); emptyHelp.Location = new Point(27, 78); empty.Controls.Add(emptyHelp); history.Controls.Add(empty);
            }
            foreach (string path in paths)
            {
                try
                {
                    HistoryCard card = new HistoryCard(path);
                    card.OpenRequested += delegate(string p) { app.OpenImageFile(p); };
                    card.PinRequested += delegate(string p) { app.PinImageFile(p); };
                    history.Controls.Add(card);
                }
                catch (Exception e) { if (!(e is IOException || e is ArgumentException || e is OutOfMemoryException)) throw; }
            }
            history.ResumeLayout();
        }
        private sealed class HeroPanel : RoundedPanel
        {
            public HeroPanel() { DoubleBuffered = true; BackColor = Color.FromArgb(33, 51, 57); }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent == null ? Ui.Background : Parent.BackColor);
                if (Width < 2 || Height < 2) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 22 * e.Graphics.DpiX / 96f))
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, BackColor, Color.FromArgb(34, 42, 60), 15f)) e.Graphics.FillPath(b, shape);
                    GraphicsState state = e.Graphics.Save(); e.Graphics.SetClip(shape);
                    using (Pen ring = new Pen(Color.FromArgb(17, Ui.Accent), 20))
                    { e.Graphics.DrawEllipse(ring, Width - 170, -90, 230, 230); e.Graphics.DrawEllipse(ring, Width - 245, 86, 150, 150); }
                    e.Graphics.Restore(state);
                    using (Pen outline = new Pen(Color.FromArgb(61, 84, 89))) e.Graphics.DrawPath(outline, shape);
                }
            }
        }
        private sealed class HistoryCard : RoundedPanel
        {
            public event Action<string> OpenRequested;
            public event Action<string> PinRequested;
            private readonly Bitmap thumbnail;
            private readonly string path;
            private readonly string dimensions;
            public HistoryCard(string file)
            {
                path = file; Size = new Size(214, 182); Margin = new Padding(0, 0, 14, 14); BackColor = Ui.Surface; DoubleBuffered = true; Cursor = Cursors.Hand;
                using (Bitmap image = Storage.LoadBitmap(file))
                {
                    dimensions = image.Width + " × " + image.Height;
                    thumbnail = new Bitmap(198, 112);
                    using (Graphics g = Graphics.FromImage(thumbnail))
                    {
                        g.Clear(Color.FromArgb(11, 16, 24)); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        float scale = Math.Min(198f / image.Width, 112f / image.Height);
                        int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
                        g.DrawImage(image, new Rectangle((198 - w) / 2, (112 - h) / 2, w, h));
                    }
                }
                Button pin = Ui.Button("플로팅", false, delegate { if (PinRequested != null) PinRequested(path); }); pin.SetBounds(146, 136, 58, 30); pin.Font = Ui.Font(8, FontStyle.Bold); Controls.Add(pin);
                Click += delegate { if (OpenRequested != null) OpenRequested(path); };
                AccessibleName = "캡처 " + dimensions + " 편집";
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                GraphicsState state = e.Graphics.Save();
                using (GraphicsPath imageShape = Theme.Round(new RectangleF(8, 8, 198, 112), 11))
                { e.Graphics.SetClip(imageShape); e.Graphics.DrawImageUnscaled(thumbnail, 8, 8); }
                e.Graphics.Restore(state);
                using (Font f = Ui.Font(9, FontStyle.Bold)) using (Brush b = new SolidBrush(Ui.Text)) e.Graphics.DrawString(dimensions, f, b, 10, 131);
                using (Font f = Ui.Font(8, FontStyle.Regular)) using (Brush b = new SolidBrush(Ui.Muted)) e.Graphics.DrawString(File.GetLastWriteTime(path).ToString("MM.dd  HH:mm:ss"), f, b, 10, 153);
            }
            protected override void Dispose(bool disposing) { if (disposing) thumbnail.Dispose(); base.Dispose(disposing); }
        }
    }

    public sealed class SettingsForm : Form
    {
        private readonly CaptureApplication app;
        private readonly SettingsPageHost tabs;
        private readonly HotkeyCaptureBox capture, pin, toggle, clickThrough, switchGroup, closePin;
        private readonly TextBox folder, quickFolder;
        private readonly CheckBox autoFloatCapture, cursor, detect, abort, restore, keep, tray, startup, autoSave, preferHtml, pasteFilePaths;
        private readonly ComboBox captureMode;
        private readonly NumericUpDown limit, closedLimit;
        private readonly Label validation;

        public SettingsForm(CaptureApplication controller)
        {
            app = controller;
            AppSettings s = app.Store.Settings, defaults = new AppSettings();
            SuspendLayout();
            Text = "Chacha Capture · 설정 · " + Ui.VersionLabel;
            Icon = Ui.CreateIcon(); BackColor = Ui.Background; ForeColor = Ui.Text; Font = Ui.Font(10, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(800, 682);

            Label title = Ui.Label("내 작업 방식에 맞게", 19, Ui.Text);
            title.Font = Ui.Font(19, FontStyle.Bold); title.Location = new Point(25, 21); Controls.Add(title);
            Label description = Ui.Label("단축키부터 플로팅까지, 편한 방식으로 맞춰 보세요.", 9, Ui.Muted);
            description.Location = new Point(28, 67); Controls.Add(description);

            SettingsNavigation navigation = new SettingsNavigation { Location = new Point(24, 105), Size = new Size(752, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Ui.Background,
                AccessibleName = "설정 범주" };
            Controls.Add(navigation);
            tabs = new SettingsPageHost(navigation) { Location = new Point(24, 151), Size = new Size(752, 431),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Ui.Background, TabStop = false };
            Controls.Add(tabs);
            Panel shortcutsPage = Page("단축키");
            Panel capturePage = Page("캡처");
            Panel storagePage = Page("저장 · 기록");
            Panel pastePage = Page("고정 · 시작");

            capture = ShortcutField(shortcutsPage, "영역 캡처", ValidHotkey(s.CaptureHotkey, defaults.CaptureHotkey), 18);
            pin = ShortcutField(shortcutsPage, "선택 영역 / 클립보드 플로팅", ValidHotkey(s.PinHotkey, defaults.PinHotkey), 74);
            toggle = ShortcutField(shortcutsPage, "모든 플로팅 창 숨기기 / 표시", ValidHotkey(s.ToggleHotkey, defaults.ToggleHotkey), 130);
            clickThrough = ShortcutField(shortcutsPage, "모든 플로팅 창 클릭 통과 전환", ValidHotkey(s.ClickThroughHotkey, defaults.ClickThroughHotkey), 186);
            switchGroup = ShortcutField(shortcutsPage, "다음 이미지 그룹으로 전환", ValidHotkey(s.SwitchGroupHotkey, defaults.SwitchGroupHotkey), 242);
            closePin = ShortcutField(shortcutsPage, "활성 플로팅 창 닫기", ValidHotkey(s.ClosePinHotkey, defaults.ClosePinHotkey), 298);
            closePin.AllowEscapeBinding = true;
            closePin.AccessibleDescription = "활성 플로팅 창을 닫는 키입니다. Esc를 눌러 지정할 수 있고 해제로 비워 둘 수 있습니다.";
            RoundedPanel shortcutHelp = new RoundedPanel { Location = new Point(20, 359), Size = new Size(702, 65), CornerRadius = 13, OutlineColor = Color.Transparent };
            shortcutsPage.Controls.Add(shortcutHelp);
            Note(shortcutHelp, "입력란 클릭 → 원하는 키 누르기 → 설정 저장 · 필요 없는 키는 해제\nTab: 다음 항목 · Esc: 입력 취소 (플로팅 닫기 항목에서는 Esc 지정)", 14, 10, 675, 47);

            autoFloatCapture = Check(capturePage, "Enter·저장 후 자동 플로팅 (Ctrl+C는 닫기)", s.AutoFloatCapture, 26);
            cursor = Check(capturePage, "처음부터 마우스 커서 포함", s.IncludeCursor, 88);
            Note(capturePage, "캡처 중 ` 키로 마우스 커서를 표시하거나 숨길 수 있습니다.", 47, 119, 620, 35);
            detect = Check(capturePage, "창과 화면 요소의 영역 자동 감지", s.AutoDetectElements, 165);
            Note(capturePage, "끄면 드래그로만 영역을 지정합니다. 켜면 Tab·휠로 감지 대상을 바꿉니다.", 47, 197, 620, 37);
            abort = Check(capturePage, "다른 앱으로 전환하면 캡처 취소", s.AbortOnFocusLoss, 248);
            Note(capturePage, "캡처를 유지한 채 다른 앱을 이용하려면 이 옵션을 끄세요.", 47, 280, 620, 36);
            Label captureModeLabel = Ui.Label("캡처 방식", 10, Ui.Text); captureModeLabel.Location = new Point(27, 322); capturePage.Controls.Add(captureModeLabel);
            captureMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(180, 317), Width = 518,
                BackColor = Ui.Surface, ForeColor = Ui.Text, FlatStyle = FlatStyle.Flat, AccessibleName = "캡처 방식" };
            captureMode.Items.AddRange(new object[] { "자동 · 호환 방식 우선 (기본)", "호환 방식 · GDI", "GPU 방식 · DXGI" });
            captureMode.SelectedIndex = Math.Max(0, Math.Min(2, (int)s.CaptureMode)); capturePage.Controls.Add(captureMode);
            Note(capturePage, "다른 앱과 함께 사용할 때는 자동 또는 호환 방식을 선택하세요.", 27, 356, 675, 28);
            Button copyDiagnostic = Ui.Button("최근 캡처 진단 복사", false, delegate
            {
                try { Clipboard.SetText(app.CaptureDiagnosticText); validation.Text = "최근 캡처 진단을 복사했습니다."; }
                catch (System.Runtime.InteropServices.ExternalException) { validation.Text = "클립보드를 사용 중입니다. 잠시 후 다시 눌러 주세요."; }
            });
            copyDiagnostic.SetBounds(27, 388, 200, 34); capturePage.Controls.Add(copyDiagnostic);
            Note(capturePage, "문제가 반복되면 진단 내용을 함께 알려주세요.", 240, 397, 470, 24);

            keep = Check(storagePage, "캡처 기록 보관", s.KeepHistory, 26);
            limit = Number(storagePage, Math.Max(1, Math.Min(200, s.HistoryLimit)), 1, 200, 470, 23);
            Note(storagePage, "개", 560, 30, 45, 24);
            Note(storagePage, "기록을 끄면 이후 캡처를 기록에 추가하지 않습니다. 기존 기록은 유지됩니다.", 47, 63, 622, 36);
            folder = FolderField(storagePage, "기본 저장 폴더", SafeFolder(s.SaveFolder, defaults.SaveFolder), 117);
            quickFolder = FolderField(storagePage, "빠른 저장 폴더", SafeFolder(s.QuickSaveFolder, defaults.QuickSaveFolder), 183);
            autoSave = Check(storagePage, "캡처와 편집 결과를 빠른 저장 폴더에 자동 저장", s.AutoSave, 259);
            Note(storagePage, "자동 저장과 별개로 Ctrl+Shift+S를 누르면 즉시 PNG 파일을 저장합니다.", 47, 293, 620, 40);
            keep.CheckedChanged += delegate { limit.Enabled = keep.Checked; };
            limit.Enabled = keep.Checked;

            restore = Check(pastePage, "다음 실행 시 고정 이미지와 그룹 복원", s.RestorePins, 25);
            Label closedText = Ui.Label("다시 불러올 수 있는 닫힌 고정 이미지", 10, Ui.Text);
            closedText.Location = new Point(27, 82); pastePage.Controls.Add(closedText);
            closedLimit = Number(pastePage, Math.Max(0, Math.Min(200, s.ClosedPinLimit)), 0, 200, 470, 78);
            Note(pastePage, "개", 560, 86, 45, 24);
            Note(pastePage, "0개로 설정하면 닫힌 고정 이미지를 보관하지 않습니다.", 47, 116, 620, 29);
            preferHtml = Check(pastePage, "클립보드에 HTML과 텍스트가 있으면 HTML 우선", s.PreferHtml, 168);
            pasteFilePaths = Check(pastePage, "이미지로 열 수 없는 파일은 경로를 텍스트로 고정", s.PasteFilePaths, 226);
            tray = Check(pastePage, "앱을 시작할 때 대시보드 대신 트레이에서 시작", s.StartInTray, 284);
            startup = Check(pastePage, "Windows에 로그인할 때 Chacha Capture 자동 실행", s.RunAtStartup, 333);

            validation = Ui.Label("", 9, Color.FromArgb(255, 179, 150));
            validation.AutoSize = false; validation.SetBounds(27, 593, 746, 35); Controls.Add(validation);
            Label version = Ui.Label(Ui.VersionLabel + " · Windows x64", 9, Ui.Muted); version.Location = new Point(27, 648); Controls.Add(version);
            Button save = Ui.Button("설정 저장", true, Save); save.SetBounds(618, 632, 156, 42); Controls.Add(save);
            Button cancel = Ui.Button("취소", false, delegate { Close(); }); cancel.SetBounds(500, 632, 108, 42); Controls.Add(cancel);
            CancelButton = cancel; AcceptButton = save;
            ResumeLayout(true);
        }

        private Panel Page(string title)
        {
            Panel page = new Panel { BackColor = Ui.Background, ForeColor = Ui.Text,
                AutoScroll = true, Padding = Padding.Empty, Margin = Padding.Empty,
                Font = Ui.Font(10, FontStyle.Regular) };
            tabs.AddPage(title, page);
            return page;
        }

        // A plain panel owns the pages so native tab chrome cannot paint white edges or scroll arrows.
        // Keep SelectedIndex as the single selection source for validation and keyboard navigation.
        private sealed class SettingsNavigation : FlowLayoutPanel
        {
            internal Func<Keys, bool> CommandKey;
            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                return (CommandKey != null && CommandKey(keyData)) || base.ProcessCmdKey(ref message, keyData);
            }
        }

        private sealed class SettingsPageHost : Panel
        {
            private readonly FlowLayoutPanel navigation;
            private readonly System.Collections.Generic.List<Panel> pages = new System.Collections.Generic.List<Panel>();
            private readonly System.Collections.Generic.List<Button> buttons = new System.Collections.Generic.List<Button>();
            private int selectedIndex;

            internal SettingsPageHost(SettingsNavigation header)
            {
                navigation = header;
                header.CommandKey = NavigateHeader;
                DoubleBuffered = true;
                BorderStyle = BorderStyle.None;
            }

            public int SelectedIndex
            {
                get { return selectedIndex; }
                set
                {
                    if (value < 0 || value >= pages.Count) throw new ArgumentOutOfRangeException("value");
                    if (value != selectedIndex && pages[selectedIndex].ContainsFocus) buttons[value].Focus();
                    selectedIndex = value;
                    for (int i = 0; i < pages.Count; i++)
                    {
                        bool active = i == selectedIndex;
                        pages[i].Visible = active;
                        buttons[i].BackColor = active ? Color.FromArgb(43, 74, 70) : Ui.Surface;
                        buttons[i].ForeColor = active ? Ui.Accent : Ui.Muted;
                        buttons[i].FlatAppearance.BorderColor = active ? Ui.Accent : Ui.Border;
                        buttons[i].AccessibleDescription = active ? "현재 선택한 설정 범주" : "누르면 해당 설정을 표시합니다";
                        buttons[i].Invalidate();
                    }
                    pages[selectedIndex].BringToFront();
                }
            }

            public void AddPage(string title, Panel page)
            {
                int index = pages.Count;
                page.Dock = DockStyle.Fill; page.TabStop = false; page.AccessibleName = title + " 설정";
                pages.Add(page); Controls.Add(page);
                Button button = Ui.Button(title, false, delegate { SelectedIndex = index; });
                button.Width = 180; button.Height = 42; button.Margin = new Padding(0, 0, index == 3 ? 0 : 10, 0);
                button.AccessibleName = title + " 설정";
                buttons.Add(button); navigation.Controls.Add(button);
                SelectedIndex = selectedIndex;
            }

            private bool NavigateHeader(Keys keyData)
            {
                if (pages.Count == 0) return false;
                int current = buttons.FindIndex(b => b.Focused);
                if (current < 0) current = selectedIndex;
                int target;
                if (keyData == Keys.Right || keyData == (Keys.Control | Keys.Tab)) target = (current + 1) % pages.Count;
                else if (keyData == Keys.Left || keyData == (Keys.Control | Keys.Shift | Keys.Tab)) target = (current + pages.Count - 1) % pages.Count;
                else if (keyData == Keys.Home) target = 0;
                else if (keyData == Keys.End) target = pages.Count - 1;
                else return false;
                SelectedIndex = target; buttons[target].Focus(); return true;
            }

            private static bool RecorderHasFocus(Control parent)
            {
                foreach (Control child in parent.Controls)
                    if (child.ContainsFocus && (child is HotkeyCaptureBox || RecorderHasFocus(child))) return true;
                return false;
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                if (RecorderHasFocus(this)) return base.ProcessCmdKey(ref message, keyData);
                if (pages.Count > 0 && (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab)))
                {
                    SelectedIndex = (selectedIndex + ((keyData & Keys.Shift) != 0 ? pages.Count - 1 : 1)) % pages.Count;
                    buttons[selectedIndex].Focus(); return true;
                }
                return base.ProcessCmdKey(ref message, keyData);
            }
        }

        private HotkeyCaptureBox ShortcutField(Control parent, string label, string value, int y)
        {
            Label caption = Ui.Label(label, 10, Ui.Text); caption.AutoSize = false; caption.SetBounds(27, y + 1, 310, 24); parent.Controls.Add(caption);
            Label hint = Ui.Label(String.IsNullOrWhiteSpace(value) ? "단축키 없이 사용 중" : "클릭해서 키를 변경하세요", 8, Ui.Muted); hint.Location = new Point(28, y + 27); parent.Controls.Add(hint);
            RoundedPanel input = new RoundedPanel { Location = new Point(350, y), Size = new Size(262, 46), CornerRadius = 12, Cursor = Cursors.Hand };
            parent.Controls.Add(input);
            HotkeyCaptureBox field = new HotkeyCaptureBox { Hotkey = value, Location = new Point(12, 11), Width = 238,
                BackColor = Ui.Surface, ForeColor = Ui.Text, BorderStyle = BorderStyle.None,
                Font = Ui.Font(11, FontStyle.Regular), AccessibleName = label, MaxLength = 80 };
            input.Controls.Add(field); input.Click += delegate { field.Focus(); };
            field.GotFocus += delegate { input.OutlineColor = Ui.Accent; input.Invalidate(); hint.Text = "지금 원하는 키를 눌러 주세요"; hint.ForeColor = Ui.Accent; };
            field.LostFocus += delegate { input.OutlineColor = Ui.Border; input.Invalidate(); hint.Text = String.IsNullOrWhiteSpace(field.Hotkey) ? "단축키 없이 사용 중" : "설정 저장을 누르면 적용됩니다"; hint.ForeColor = Ui.Muted; };
            field.HotkeyChanged += delegate { hint.Text = String.IsNullOrWhiteSpace(field.Hotkey) ? "해제됨 · 설정 저장으로 적용" : "인식됨 · 설정 저장으로 적용"; hint.ForeColor = Ui.Accent; if (validation != null) validation.Text = ""; };
            Button clear = Ui.Button("해제", false, delegate { field.Hotkey = String.Empty; });
            clear.SetBounds(624, y + 2, 96, 42); clear.AccessibleName = label + " 단축키 지우기";
            parent.Controls.Add(clear);
            return field;
        }

        private TextBox FolderField(Control parent, string label, string value, int y)
        {
            Label caption = Ui.Label(label, 10, Ui.Text); caption.Location = new Point(27, y + 7); parent.Controls.Add(caption);
            TextBox field = new TextBox { Text = value, Location = new Point(182, y), Width = 430,
                BackColor = Ui.Surface, ForeColor = Ui.Text, BorderStyle = BorderStyle.FixedSingle,
                Font = Ui.Font(10, FontStyle.Regular), AccessibleName = label };
            parent.Controls.Add(field);
            Button browse = Ui.Button("…", false, delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = label + " 선택", ShowNewFolderButton = true })
                {
                    if (Directory.Exists(field.Text)) dialog.SelectedPath = field.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath;
                }
            });
            browse.SetBounds(622, y - 1, 42, 31); browse.AccessibleName = label + " 찾아보기"; parent.Controls.Add(browse);
            return field;
        }

        private static CheckBox Check(Control parent, string text, bool value, int y)
        {
            CheckBox check = new CheckBox { Text = text, Checked = value, Location = new Point(27, y),
                AutoSize = true, ForeColor = Ui.Text, BackColor = Ui.Background, UseVisualStyleBackColor = false };
            parent.Controls.Add(check); return check;
        }

        private static NumericUpDown Number(Control parent, int value, int minimum, int maximum, int x, int y)
        {
            NumericUpDown number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value,
                Location = new Point(x, y), Width = 78, BackColor = Ui.Surface, ForeColor = Ui.Text,
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            parent.Controls.Add(number); return number;
        }

        private static void Note(Control parent, string text, int x, int y, int width, int height)
        {
            Label note = Ui.Label(text, 9, Ui.Muted); note.AutoSize = false;
            note.SetBounds(x, y, width, height); parent.Controls.Add(note);
        }

        private static string ValidHotkey(string value, string fallback)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            uint modifiers, key;
            return HotkeyWindow.Parse(value, out modifiers, out key) ? value : fallback;
        }

        private static string SafeFolder(string value, string fallback)
        {
            try { return String.IsNullOrWhiteSpace(value) ? fallback : Path.GetFullPath(value); }
            catch (ArgumentException) { return fallback; }
            catch (NotSupportedException) { return fallback; }
            catch (PathTooLongException) { return fallback; }
        }

        private bool ShowValidation(string message, int page, Control focus)
        {
            validation.Text = message; tabs.SelectedIndex = page;
            if (focus != null) focus.Focus();
            return false;
        }

        private bool ValidateFolder(TextBox field, string label, out string fullPath)
        {
            fullPath = null;
            try
            {
                if (String.IsNullOrWhiteSpace(field.Text)) return ShowValidation(label + "를 입력해 주세요.", 2, field);
                fullPath = Path.GetFullPath(field.Text.Trim());
                Directory.CreateDirectory(fullPath);
                return true;
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)) throw;
                return ShowValidation(label + "를 사용할 수 없습니다: " + e.Message, 2, field);
            }
        }

        private void Save(object sender, EventArgs e)
        {
            validation.Text = "";
            HotkeyCaptureBox[] fields = { capture, pin, toggle, clickThrough, switchGroup, closePin };
            string[] keys = fields.Select(f => f.Hotkey.Trim()).ToArray();
            uint modifiers, key;
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(keys[i])) continue;
                if (!HotkeyWindow.Parse(keys[i], out modifiers, out key))
                {
                    ShowValidation("입력란을 선택하고 단축키를 눌러 주세요: " + fields[i].AccessibleName, 0, fields[i]); return;
                }
                if (!seen.Add(modifiers + ":" + key))
                {
                    ShowValidation("각 동작에 서로 다른 단축키를 지정해 주세요.", 0, fields[i]); return;
                }
                if (i == 5 && modifiers == 4 && key == (uint)Keys.Escape)
                {
                    ShowValidation("Shift+Esc는 이미지 완전 삭제 키입니다. 닫기에는 Esc 또는 다른 키를 지정해 주세요.", 0, fields[i]); return;
                }
                string conflict;
                if (!HotkeyWindow.CheckAvailability(keys[i], out conflict))
                {
                    ShowValidation(fields[i].AccessibleName + ": " + conflict, 0, fields[i]); return;
                }
            }
            string savePath, quickPath;
            if (!ValidateFolder(folder, "기본 저장 폴더", out savePath) || !ValidateFolder(quickFolder, "빠른 저장 폴더", out quickPath)) return;
            AppSettings previous = app.Store.Settings;
            AppSettings next = new AppSettings
            {
                CaptureHotkey = keys[0], PinHotkey = keys[1], ToggleHotkey = keys[2], ClickThroughHotkey = keys[3], SwitchGroupHotkey = keys[4], ClosePinHotkey = keys[5],
                AutoFloatCapture = autoFloatCapture.Checked, CaptureMode = (DesktopCaptureMode)captureMode.SelectedIndex, PreferGpuCapture = previous.PreferGpuCapture, IncludeCursor = cursor.Checked, AutoDetectElements = detect.Checked, AbortOnFocusLoss = abort.Checked,
                RestorePins = restore.Checked, KeepHistory = keep.Checked, StartInTray = tray.Checked,
                AutoSave = autoSave.Checked, PreferHtml = preferHtml.Checked, PasteFilePaths = pasteFilePaths.Checked,
                HistoryLimit = (int)limit.Value, ClosedPinLimit = (int)closedLimit.Value,
                SaveFolder = savePath, QuickSaveFolder = quickPath,
                RunAtStartup = startup.Checked, ActiveGroup = previous.ActiveGroup, SettingsVersion = previous.SettingsVersion,
                LastSelection = previous.LastSelection
            };
            bool startupApplied = false;
            try
            {
                if (startup.Checked != previous.RunAtStartup) { app.SetRunAtStartup(startup.Checked); startupApplied = true; }
                app.Store.Settings = next;
                app.ApplySettings();
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception error)
            {
                app.Store.Settings = previous;
                if (startupApplied)
                {
                    try { app.SetRunAtStartup(previous.RunAtStartup); }
                    catch (Exception rollbackError)
                    {
                        ShowValidation("설정 저장에 실패했고 자동 실행 설정도 복원하지 못했습니다: " + rollbackError.Message, 3, startup);
                        return;
                    }
                }
                ShowValidation("설정을 저장하지 못했습니다: " + error.Message, tabs.SelectedIndex, null);
            }
        }
    }
}
