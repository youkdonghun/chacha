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
            root.Controls.Add(header, 0, 0);

            Panel hero = new HeroPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(24) };
            Label eyebrow = Ui.Label("YOUR SCREEN, AT HAND.", 9, Ui.Accent); eyebrow.Location = new Point(26, 20); hero.Controls.Add(eyebrow);
            Label title = Ui.Label("순간을 담고, 화면에 붙이세요.", 24, Ui.Text); title.Font = Ui.Font(24, FontStyle.Bold); title.Location = new Point(23, 47); hero.Controls.Add(title);
            Label subtitle = Ui.Label("필요한 부분만 캡처하고, 설명을 더하고, 작업 옆에 고정하세요.", 10, Ui.Muted); subtitle.Location = new Point(27, 101); hero.Controls.Add(subtitle);
            Button capture = Ui.Button("＋   새 영역 캡처", true, delegate { app.BeginCapture(0, false, false); }); capture.SetBounds(26, 142, 164, 39); hero.Controls.Add(capture);
            shortcutLabel = Ui.Label("", 9, Ui.Muted); shortcutLabel.Location = new Point(208, 153); hero.Controls.Add(shortcutLabel);
            root.Controls.Add(hero, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0), WrapContents = false };
            Button paste = Ui.Button("클립보드 고정", false, delegate { app.PinClipboard(); }); paste.Width = 150; actions.Controls.Add(paste);
            Button file = Ui.Button("이미지 열기", false, delegate { app.OpenImage(); }); file.Width = 132; actions.Controls.Add(file);
            Button full = Ui.Button("전체 화면", false, delegate { app.BeginCapture(0, true, false); }); full.Width = 132; actions.Controls.Add(full);
            Button delay = Ui.Button("3초 후 캡처", false, delegate { app.BeginCapture(3, false, false); }); delay.Width = 132; actions.Controls.Add(delay);
            Button repeat = Ui.Button("최근 영역 재캡처", false, delegate { app.BeginCapture(0, false, true); }); repeat.Width = 168; actions.Controls.Add(repeat);
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
        private readonly TextBox capture, pin, toggle, folder;
        private readonly CheckBox cursor, restore, keep, tray;
        private readonly NumericUpDown limit;
        public SettingsForm(CaptureApplication controller)
        {
            app = controller; AppSettings s = app.Store.Settings;
            SuspendLayout();
            Text = "Chacha Capture · 설정"; Icon = Ui.CreateIcon(); BackColor = Ui.Background; ForeColor = Ui.Text; Font = Ui.Font(10, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(610, 590);
            Label title = Ui.Label("내 작업 방식에 맞게", 19, Ui.Text); title.Font = Ui.Font(19, FontStyle.Bold); title.Location = new Point(26, 23); Controls.Add(title);
            Label desc = Ui.Label("설정과 캡처 기록은 이 PC의 사용자 폴더에 저장됩니다.", 9, Ui.Muted); desc.Location = new Point(28, 66); Controls.Add(desc);
            capture = Field("영역 캡처", s.CaptureHotkey, 111);
            pin = Field("클립보드 고정", s.PinHotkey, 155);
            toggle = Field("모든 고정 창 숨기기 / 표시", s.ToggleHotkey, 199);
            Label example = Ui.Label("예: F1, F3, Ctrl+Shift+A  ·  다른 앱이 사용 중이면 안내합니다.", 9, Ui.Muted); example.Location = new Point(28, 245); Controls.Add(example);
            cursor = Check("마우스 커서 포함", s.IncludeCursor, 281);
            restore = Check("다음 실행 시 고정 이미지 복원", s.RestorePins, 315);
            keep = Check("캡처 기록 저장", s.KeepHistory, 349);
            tray = Check("시작할 때 대시보드 숨기기", s.StartInTray, 383);
            limit = new NumericUpDown { Minimum = 1, Maximum = 200, Value = s.HistoryLimit, Location = new Point(415, 349), Width = 80, BackColor = Ui.Surface, ForeColor = Ui.Text };
            Controls.Add(limit); Label unit = Ui.Label("개 보관", 9, Ui.Muted); unit.Location = new Point(505, 354); Controls.Add(unit);
            folder = Field("기본 저장 폴더", s.SaveFolder, 432); folder.Width = 287;
            Button browse = Ui.Button("…", false, delegate { using (FolderBrowserDialog dlg = new FolderBrowserDialog()) { dlg.SelectedPath = folder.Text; if (dlg.ShowDialog(this) == DialogResult.OK) folder.Text = dlg.SelectedPath; } }); browse.SetBounds(531, 430, 44, 32); Controls.Add(browse);
            Button save = Ui.Button("설정 저장", true, Save); save.SetBounds(425, 516, 150, 42); Controls.Add(save);
            Button cancel = Ui.Button("취소", false, delegate { Close(); }); cancel.SetBounds(313, 516, 100, 42); Controls.Add(cancel); CancelButton = cancel; AcceptButton = save;
            Label version = Ui.Label("v1.0.0 · Windows x64", 9, Ui.Muted); version.Location = new Point(28, 528); Controls.Add(version);
            ResumeLayout(true);
        }
        private TextBox Field(string label, string value, int y)
        {
            Label l = Ui.Label(label, 10, Ui.Text); l.Location = new Point(28, y + 4); Controls.Add(l);
            TextBox text = new TextBox { Text = value, Location = new Point(232, y), Width = 343, BackColor = Ui.Surface, ForeColor = Ui.Text, BorderStyle = BorderStyle.FixedSingle, Font = Ui.Font(11, FontStyle.Regular) }; Controls.Add(text); return text;
        }
        private CheckBox Check(string text, bool value, int y) { CheckBox c = new CheckBox { Text = text, Checked = value, Location = new Point(28, y), AutoSize = true, ForeColor = Ui.Text }; Controls.Add(c); return c; }
        private void Save(object sender, EventArgs e)
        {
            string[] hotkeys = { capture.Text.Trim(), pin.Text.Trim(), toggle.Text.Trim() };
            uint m, k;
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
            foreach (string key in hotkeys)
            {
                if (!HotkeyWindow.Parse(key, out m, out k)) { MessageBox.Show(this, "단축키 형식을 확인해 주세요: " + key); return; }
                if (!seen.Add(m + ":" + k)) { MessageBox.Show(this, "각 동작에 서로 다른 단축키를 지정해 주세요."); return; }
            }
            try { string full = Path.GetFullPath(folder.Text.Trim()); Directory.CreateDirectory(full); folder.Text = full; }
            catch (Exception ex) { MessageBox.Show(this, "저장 폴더를 사용할 수 없습니다.\n" + ex.Message); return; }
            AppSettings s = app.Store.Settings;
            s.CaptureHotkey = hotkeys[0]; s.PinHotkey = hotkeys[1]; s.ToggleHotkey = hotkeys[2];
            s.IncludeCursor = cursor.Checked; s.RestorePins = restore.Checked; s.KeepHistory = keep.Checked; s.StartInTray = tray.Checked; s.HistoryLimit = (int)limit.Value; s.SaveFolder = folder.Text;
            app.ApplySettings(); Close();
        }
    }
}
