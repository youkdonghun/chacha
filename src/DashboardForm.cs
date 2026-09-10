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
        public DashboardForm(CaptureApplication controller)
        {
            app = controller;
            SuspendLayout();
            Text = "Chacha Capture";
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
            Label edition = Ui.Label("CAPTURE  /  PERSONAL", 8, Ui.Muted); edition.Location = new Point(145, 15); header.Controls.Add(edition);
            Button settings = Ui.Button("설정", false, delegate { app.ShowSettings(); }); settings.Width = 84; settings.Dock = DockStyle.Right; settings.Height = 34; header.Controls.Add(settings);
            Button pins = Ui.Button("고정 창 보기", false, delegate { app.ShowAllPins(); }); pins.Width = 112; pins.Dock = DockStyle.Right; header.Controls.Add(pins);
            Button groups = Ui.Button("이미지 그룹", false, delegate { app.ShowGroups(); }); groups.Width = 112; groups.Dock = DockStyle.Right; header.Controls.Add(groups);
            root.Controls.Add(header, 0, 0);

            Panel hero = new HeroPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(24) };
            Label eyebrow = Ui.Label("YOUR SCREEN, AT HAND.", 9, Ui.Accent); eyebrow.Location = new Point(26, 20); hero.Controls.Add(eyebrow);
            Label title = Ui.Label("순간을 담고, 화면에 붙이세요.", 24, Ui.Text); title.Font = Ui.Font(24, FontStyle.Bold); title.Location = new Point(23, 47); hero.Controls.Add(title);
            Label subtitle = Ui.Label("필요한 부분만 캡처하고, 설명을 더하고, 작업 옆에 고정하세요.", 10, Ui.Muted); subtitle.Location = new Point(27, 101); hero.Controls.Add(subtitle);
            Button capture = Ui.Button("＋   새 영역 캡처", true, delegate { app.BeginCapture(0, false, false); }); capture.SetBounds(26, 142, 164, 39); hero.Controls.Add(capture);
            shortcutLabel = Ui.Label("", 9, Ui.Muted); shortcutLabel.Location = new Point(208, 153); hero.Controls.Add(shortcutLabel);
            shortcutLabel.AutoSize = false; shortcutLabel.Height = 22; shortcutLabel.AutoEllipsis = true;
            hero.Resize += delegate { shortcutLabel.Width = Math.Max(40, hero.ClientSize.Width - shortcutLabel.Left - 20); };
            root.Controls.Add(hero, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0), WrapContents = false };
            Button paste = Ui.Button("클립보드 고정", false, delegate { app.PinClipboard(); }); paste.Width = 140; actions.Controls.Add(paste);
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
        public void RefreshSettings() { shortcutLabel.Text = app.Store.Settings.CaptureHotkey + "  캡처    ·    " + app.Store.Settings.PinHotkey + "  고정    ·    " + app.Store.Settings.ToggleHotkey + "  숨기기 / 표시"; }
        public void SetStatus(string text) { status.Text = "●  " + text; }
        public void RefreshHistory()
        {
            if (IsDisposed) return;
            history.SuspendLayout();
            while (history.Controls.Count > 0) { Control c = history.Controls[0]; history.Controls.RemoveAt(0); c.Dispose(); }
            string[] paths = app.Store.History(); count.Text = paths.Length + "개 · 이 PC에 저장됨";
            if (paths.Length == 0)
            {
                Panel empty = new Panel { Width = 920, Height = 170, BackColor = Ui.Surface };
                Label first = Ui.Label("아직 캡처한 이미지가 없어요.", 15, Ui.Text); first.Location = new Point(26, 32); empty.Controls.Add(first);
                Label help = Ui.Label("위의 새 영역 캡처를 누르거나 " + app.Store.Settings.CaptureHotkey + " 키로 시작하세요.\n이미지 파일을 이 창에 끌어다 놓아 편집할 수도 있습니다.", 10, Ui.Muted); help.Location = new Point(27, 78); empty.Controls.Add(help); history.Controls.Add(empty);
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
        private sealed class HeroPanel : Panel
        {
            public HeroPanel() { DoubleBuffered = true; }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(28, 47, 51), Color.FromArgb(23, 31, 44), 15f)) e.Graphics.FillRectangle(b, ClientRectangle);
                using (Pen p = new Pen(Color.FromArgb(50, Ui.Accent), 1))
                {
                    for (int x = Width - 210; x < Width + 50; x += 38) e.Graphics.DrawLine(p, x, 0, x + 90, Height);
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            }
        }
        private sealed class HistoryCard : Panel
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
                Button pin = Ui.Button("고정", false, delegate { if (PinRequested != null) PinRequested(path); }); pin.SetBounds(155, 136, 49, 30); pin.Font = Ui.Font(8, FontStyle.Bold); Controls.Add(pin);
                Click += delegate { if (OpenRequested != null) OpenRequested(path); };
                AccessibleName = "캡처 " + dimensions + " 편집";
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); e.Graphics.DrawImageUnscaled(thumbnail, 8, 8);
                using (Font f = Ui.Font(9, FontStyle.Bold)) using (Brush b = new SolidBrush(Ui.Text)) e.Graphics.DrawString(dimensions, f, b, 10, 131);
                using (Font f = Ui.Font(8, FontStyle.Regular)) using (Brush b = new SolidBrush(Ui.Muted)) e.Graphics.DrawString(File.GetLastWriteTime(path).ToString("MM.dd  HH:mm:ss"), f, b, 10, 153);
                using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
            protected override void Dispose(bool disposing) { if (disposing) thumbnail.Dispose(); base.Dispose(disposing); }
        }
    }

    public sealed class SettingsForm : Form
    {
        private readonly CaptureApplication app;
        private readonly TabControl tabs;
        private readonly TextBox capture, pin, toggle, clickThrough, switchGroup, folder, quickFolder;
        private readonly CheckBox cursor, detect, abort, restore, keep, tray, startup, autoSave, preferHtml, pasteFilePaths;
        private readonly NumericUpDown limit, closedLimit;
        private readonly Label validation;

        public SettingsForm(CaptureApplication controller)
        {
            app = controller;
            AppSettings s = app.Store.Settings, defaults = new AppSettings();
            SuspendLayout();
            Text = "Chacha Capture · 설정";
            Icon = Ui.CreateIcon(); BackColor = Ui.Background; ForeColor = Ui.Text; Font = Ui.Font(10, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 620);

            Label title = Ui.Label("내 작업 방식에 맞게", 19, Ui.Text);
            title.Font = Ui.Font(19, FontStyle.Bold); title.Location = new Point(25, 21); Controls.Add(title);
            Label description = Ui.Label("캡처, 고정, 저장 동작을 한곳에서 조정하세요.", 9, Ui.Muted);
            description.Location = new Point(28, 67); Controls.Add(description);

            tabs = new TabControl { Location = new Point(24, 105), Size = new Size(712, 423),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(173, 36),
                Padding = new Point(12, 7), Font = Ui.Font(10, FontStyle.Bold), AccessibleName = "설정 범주" };
            tabs.DrawItem += DrawTab;
            Controls.Add(tabs);
            TabPage shortcutsPage = Page("단축키");
            TabPage capturePage = Page("캡처");
            TabPage storagePage = Page("저장 · 기록");
            TabPage pastePage = Page("고정 · 시작");

            capture = ShortcutField(shortcutsPage, "영역 캡처", ValidHotkey(s.CaptureHotkey, defaults.CaptureHotkey), 25);
            pin = ShortcutField(shortcutsPage, "클립보드 고정", ValidHotkey(s.PinHotkey, defaults.PinHotkey), 77);
            toggle = ShortcutField(shortcutsPage, "모든 고정 창 숨기기 / 표시", ValidHotkey(s.ToggleHotkey, defaults.ToggleHotkey), 129);
            clickThrough = ShortcutField(shortcutsPage, "모든 고정 창 클릭 통과 전환", ValidHotkey(s.ClickThroughHotkey, defaults.ClickThroughHotkey), 181);
            switchGroup = ShortcutField(shortcutsPage, "다음 이미지 그룹으로 전환", ValidHotkey(s.SwitchGroupHotkey, defaults.SwitchGroupHotkey), 233);
            Note(shortcutsPage, "예: F1, F3, Shift+F3, Ctrl+Alt+A\n각 동작에 서로 다른 키를 지정하세요. 다른 앱과 충돌하면 저장 후 안내합니다.", 27, 295, 645, 52);

            cursor = Check(capturePage, "처음부터 마우스 커서 포함", s.IncludeCursor, 28);
            Note(capturePage, "캡처 중 ` 키로 마우스 커서를 표시하거나 숨길 수 있습니다.", 47, 59, 620, 35);
            detect = Check(capturePage, "버튼과 입력란 등 화면 요소 자동 감지", s.AutoDetectElements, 114);
            Note(capturePage, "Tab 키로 창 선택과 요소 선택을 전환하고, 휠로 선택 범위를 조절합니다.", 47, 146, 620, 37);
            abort = Check(capturePage, "다른 앱으로 전환하면 캡처 취소", s.AbortOnFocusLoss, 203);
            Note(capturePage, "캡처를 유지한 채 다른 앱을 이용하려면 이 옵션을 끄세요.", 47, 235, 620, 36);
            Note(capturePage, "Enter 복사  ·  Ctrl+T 화면에 고정  ·  Alt 확대경  ·  Esc 취소", 27, 314, 650, 32);

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
            validation.AutoSize = false; validation.SetBounds(27, 536, 705, 26); validation.AutoEllipsis = true; Controls.Add(validation);
            Label version = Ui.Label("v1.1.0 · Windows x64", 9, Ui.Muted); version.Location = new Point(27, 577); Controls.Add(version);
            Button save = Ui.Button("설정 저장", true, Save); save.SetBounds(584, 567, 150, 38); Controls.Add(save);
            Button cancel = Ui.Button("취소", false, delegate { Close(); }); cancel.SetBounds(475, 567, 99, 38); Controls.Add(cancel);
            CancelButton = cancel; AcceptButton = save;
            ResumeLayout(true);
        }

        private TabPage Page(string title)
        {
            TabPage page = new TabPage(title) { BackColor = Ui.Background, ForeColor = Ui.Text,
                UseVisualStyleBackColor = false, AutoScroll = true, Padding = new Padding(0),
                Font = Ui.Font(10, FontStyle.Regular) };
            tabs.TabPages.Add(page);
            return page;
        }

        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            bool selected = e.Index == tabs.SelectedIndex;
            Rectangle bounds = e.Bounds;
            using (Brush brush = new SolidBrush(selected ? Ui.Surface : Ui.Background)) e.Graphics.FillRectangle(brush, bounds);
            if (selected)
                using (Brush accent = new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(accent, bounds.Left + 9, bounds.Bottom - 3, bounds.Width - 18, 3);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds,
                selected ? Ui.Accent : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        private TextBox ShortcutField(Control parent, string label, string value, int y)
        {
            Label caption = Ui.Label(label, 10, Ui.Text); caption.Location = new Point(27, y + 7); parent.Controls.Add(caption);
            TextBox field = new TextBox { Text = value, Location = new Point(338, y), Width = 326,
                BackColor = Ui.Surface, ForeColor = Ui.Text, BorderStyle = BorderStyle.FixedSingle,
                Font = Ui.Font(11, FontStyle.Regular), AccessibleName = label, MaxLength = 80 };
            parent.Controls.Add(field);
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
            TextBox[] fields = { capture, pin, toggle, clickThrough, switchGroup };
            string[] keys = fields.Select(f => f.Text.Trim()).ToArray();
            uint modifiers, key;
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (!HotkeyWindow.Parse(keys[i], out modifiers, out key))
                {
                    ShowValidation("단축키 형식을 확인해 주세요: " + fields[i].AccessibleName, 0, fields[i]); return;
                }
                if (!seen.Add(modifiers + ":" + key))
                {
                    ShowValidation("각 동작에 서로 다른 단축키를 지정해 주세요.", 0, fields[i]); return;
                }
            }
            string savePath, quickPath;
            if (!ValidateFolder(folder, "기본 저장 폴더", out savePath) || !ValidateFolder(quickFolder, "빠른 저장 폴더", out quickPath)) return;
            AppSettings previous = app.Store.Settings;
            AppSettings next = new AppSettings
            {
                CaptureHotkey = keys[0], PinHotkey = keys[1], ToggleHotkey = keys[2], ClickThroughHotkey = keys[3], SwitchGroupHotkey = keys[4],
                IncludeCursor = cursor.Checked, AutoDetectElements = detect.Checked, AbortOnFocusLoss = abort.Checked,
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
                app.SetRunAtStartup(startup.Checked);
                startupApplied = true;
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
