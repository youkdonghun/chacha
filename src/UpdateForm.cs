using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChachaCapture
{
    public sealed class UpdateForm : Form
    {
        private enum UpdateState { Idle, Checking, Latest, Available, Downloading, Ready, Installing, Error }
        private readonly Action shutdown;
        private readonly Func<CancellationToken, Task<UpdateInfo>> check;
        private readonly Func<UpdateInfo, IProgress<UpdateProgress>, CancellationToken, Task<StagedUpdate>> download;
        private readonly Action<StagedUpdate> install;
        private readonly Action<StagedUpdate> discard;
        private readonly Icon ownedIcon;
        private readonly Label stateLabel, headline, description, currentVersion, progressText;
        private readonly ProgressBar progress;
        private readonly TextBox notes;
        private readonly Button checkButton, cancelButton, actionButton;
        private readonly LinkLabel releaseLink;
        private UpdateInfo release;
        private StagedUpdate staged;
        private CancellationTokenSource operation;
        private UpdateState state;
        private bool closing;
        private bool cancelRequested;
        private bool ownedIconDisposed;
        private bool installerStarted;

        public UpdateForm(CaptureApplication app)
            : this(app.ShutdownForUpdate, UpdateService.CheckAsync, UpdateService.DownloadAsync,
                delegate(StagedUpdate update) { UpdateService.StartInstaller(update, new string[] { "--data-dir", app.Store.Root }); }, UpdateService.Discard) { }

        internal UpdateForm(Action requestShutdown,
            Func<CancellationToken, Task<UpdateInfo>> checkForUpdate,
            Func<UpdateInfo, IProgress<UpdateProgress>, CancellationToken, Task<StagedUpdate>> downloadUpdate,
            Action<StagedUpdate> startInstaller)
            : this(requestShutdown, checkForUpdate, downloadUpdate, startInstaller, delegate { }) { }

        internal UpdateForm(Action requestShutdown,
            Func<CancellationToken, Task<UpdateInfo>> checkForUpdate,
            Func<UpdateInfo, IProgress<UpdateProgress>, CancellationToken, Task<StagedUpdate>> downloadUpdate,
            Action<StagedUpdate> startInstaller, Action<StagedUpdate> discardUpdate)
        {
            if (requestShutdown == null || checkForUpdate == null || downloadUpdate == null || startInstaller == null || discardUpdate == null)
                throw new ArgumentNullException("Update callbacks must be provided.");
            shutdown = requestShutdown; check = checkForUpdate; download = downloadUpdate; install = startInstaller; discard = discardUpdate;
            SuspendLayout();
            Text = "Chacha Capture · 업데이트";
            ownedIcon = Ui.CreateIcon(); Icon = ownedIcon;
            BackColor = Ui.Background; ForeColor = Ui.Text;
            Font = Ui.Font(10, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(668, 554);

            Label title = Ui.Label("항상 최신 Chacha로", 20, Ui.Text);
            title.Font = Ui.Font(20, FontStyle.Bold); title.Location = new Point(26, 23); Controls.Add(title);
            Label intro = Ui.Label("새 버전을 확인하고, 이 창에서 다운로드와 설치를 진행하세요.", 10, Ui.Muted);
            intro.Location = new Point(28, 73); Controls.Add(intro);

            Panel card = new RoundedPanel { BackColor = Ui.Surface, Location = new Point(28, 118), Size = new Size(612, 123) };
            Controls.Add(card);
            stateLabel = Ui.Label("업데이트", 9, Ui.Accent); stateLabel.Font = Ui.Font(9, FontStyle.Bold);
            stateLabel.SetBounds(19, 14, 176, 22); stateLabel.AutoSize = false; card.Controls.Add(stateLabel);
            currentVersion = Ui.Label("현재 v" + DisplayVersion(Assembly.GetExecutingAssembly().GetName().Version) + " · 64비트", 9, Ui.Muted);
            currentVersion.AutoSize = false; currentVersion.TextAlign = ContentAlignment.TopRight;
            currentVersion.SetBounds(280, 15, 311, 23); card.Controls.Add(currentVersion);
            headline = Ui.Label("최신 버전을 확인할 수 있습니다", 17, Ui.Text);
            headline.Font = Ui.Font(17, FontStyle.Bold); headline.AutoSize = false;
            headline.SetBounds(17, 40, 580, 36); headline.AutoEllipsis = true; card.Controls.Add(headline);
            description = Ui.Label("GitHub에 공개된 Chacha Capture 릴리스를 확인합니다.", 9, Ui.Muted);
            description.AutoSize = false; description.SetBounds(20, 85, 574, 29); card.Controls.Add(description);

            progress = new ProgressBar { Location = new Point(28, 257), Size = new Size(612, 9),
                Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, MarqueeAnimationSpeed = 24 };
            Controls.Add(progress);
            progressText = Ui.Label("", 9, Ui.Muted); progressText.AutoSize = false;
            progressText.SetBounds(28, 276, 612, 24); progressText.AutoEllipsis = true; Controls.Add(progressText);

            Label notesLabel = Ui.Label("릴리스 노트", 10, Ui.Text); notesLabel.Font = Ui.Font(10, FontStyle.Bold);
            notesLabel.Location = new Point(28, 311); Controls.Add(notesLabel);
            releaseLink = new LinkLabel { Text = "GitHub에서 보기  ↗", Location = new Point(446, 312), Size = new Size(194, 23),
                TextAlign = ContentAlignment.TopRight, LinkColor = Ui.Accent, ActiveLinkColor = Ui.Text, VisitedLinkColor = Ui.Accent,
                BackColor = Ui.Background, Font = Ui.Font(9, FontStyle.Regular), AccessibleName = "GitHub 릴리스 페이지 열기" };
            releaseLink.LinkClicked += delegate { OpenReleasePage(); }; Controls.Add(releaseLink);
            RoundedPanel notesCard = new RoundedPanel { Location = new Point(28, 342), Size = new Size(612, 117), Padding = new Padding(14, 10, 10, 10), CornerRadius = 14 };
            Controls.Add(notesCard);
            notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, BackColor = Ui.Surface, ForeColor = Ui.Text,
                BorderStyle = BorderStyle.None, Font = Ui.Font(9, FontStyle.Regular),
                Text = "확인이 끝나면 최신 버전의 변경 사항이 여기에 표시됩니다.",
                AccessibleName = "릴리스 노트", TabStop = true };
            notesCard.Controls.Add(notes);

            checkButton = Ui.Button("다시 확인", false, async delegate { await CheckForUpdatesAsync(); });
            checkButton.SetBounds(28, 483, 132, 42); Controls.Add(checkButton);
            cancelButton = Ui.Button("닫기", false, delegate { CancelOrClose(); });
            cancelButton.SetBounds(316, 483, 112, 42); Controls.Add(cancelButton);
            actionButton = Ui.Button("업데이트 확인", true, async delegate
            {
                if (state == UpdateState.Ready || (state == UpdateState.Error && staged != null)) InstallUpdate();
                else if (state == UpdateState.Available || (state == UpdateState.Error && release != null && release.IsUpdateAvailable)) await DownloadUpdateAsync();
                else if (state == UpdateState.Latest) Close();
                else await CheckForUpdatesAsync();
            });
            actionButton.SetBounds(440, 483, 200, 42); Controls.Add(actionButton);
            AcceptButton = actionButton; CancelButton = cancelButton;
            Shown += async delegate { await CheckForUpdatesAsync(); };
            state = UpdateState.Idle;
            UpdateButtons(); ResumeLayout(true);
        }

        internal string StatusName { get { return state.ToString(); } }

        internal async Task CheckForUpdatesAsync()
        {
            if (operation != null || closing || IsDisposed || state == UpdateState.Installing) return;
            DiscardStaged(); release = null;
            notes.Text = "최신 릴리스의 변경 사항을 가져오고 있습니다.";
            CancellationTokenSource request = StartOperation(UpdateState.Checking, "업데이트 확인 중", "새 버전이 있는지 확인하고 있습니다", "GitHub의 최신 정식 릴리스를 조회합니다.");
            try
            {
                UpdateInfo result = await check(request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (closing || IsDisposed) return;
                if (result == null) throw new InvalidOperationException("릴리스 정보를 가져오지 못했습니다.");
                release = result;
                if (!String.IsNullOrWhiteSpace(result.CurrentVersion)) currentVersion.Text = "현재 v" + VersionText(result.CurrentVersion) + " · 64비트";
                notes.Text = String.IsNullOrWhiteSpace(result.ReleaseNotes) ? "이 릴리스에는 별도의 변경 사항이 등록되어 있지 않습니다." : NormalizeLines(result.ReleaseNotes);
                if (result.IsUpdateAvailable) ShowAvailable(false);
                else SetState(UpdateState.Latest, "최신 버전", "최신 버전을 사용 중입니다", "현재 버전을 계속 사용하시면 됩니다.", "확인 완료  ·  " + DateTime.Now.ToString("HH:mm"));
            }
            catch (OperationCanceledException)
            {
                if (!closing && !IsDisposed)
                {
                    notes.Text = "다시 확인을 누르면 최신 버전의 변경 사항을 가져옵니다.";
                    SetState(UpdateState.Idle, "확인 중지", "업데이트 확인을 중지했습니다", "언제든지 다시 확인할 수 있습니다.", "");
                }
            }
            catch (Exception error)
            {
                if (!closing && !IsDisposed) ShowError("업데이트를 확인하지 못했습니다", error);
            }
            finally { FinishOperation(request); }
        }

        internal async Task DownloadUpdateAsync()
        {
            if (operation != null || closing || IsDisposed || release == null || !release.IsUpdateAvailable) return;
            DiscardStaged();
            notes.Text = String.IsNullOrWhiteSpace(release.ReleaseNotes) ? "이 릴리스에는 별도의 변경 사항이 등록되어 있지 않습니다." : NormalizeLines(release.ReleaseNotes);
            CancellationTokenSource request = StartOperation(UpdateState.Downloading, "다운로드", "v" + VersionText(release.Version) + " 다운로드 중", "다운로드가 끝나면 설치 버튼이 활성화됩니다.");
            try
            {
                Progress<UpdateProgress> reporter = new Progress<UpdateProgress>(delegate(UpdateProgress value)
                {
                    if (closing || IsDisposed || operation != request || cancelRequested || value == null) return;
                    int percent = value.Percent;
                    progress.Style = percent < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                    if (percent >= 0) progress.Value = Math.Max(0, Math.Min(100, percent));
                    string amounts = value.TotalBytes > 0 ? SizeText(value.BytesReceived) + " / " + SizeText(value.TotalBytes) : (value.BytesReceived > 0 ? SizeText(value.BytesReceived) : "");
                    progressText.Text = (String.IsNullOrWhiteSpace(value.Phase) ? "다운로드 중" : value.Phase) +
                        (String.IsNullOrEmpty(amounts) ? "" : "  ·  " + amounts) + (percent >= 0 ? "  ·  " + percent + "%" : "");
                });
                StagedUpdate result = await download(release, reporter, request.Token);
                if (request.IsCancellationRequested || closing || IsDisposed)
                {
                    TryDiscard(result);
                    request.Token.ThrowIfCancellationRequested();
                    return;
                }
                if (result == null) throw new InvalidOperationException("다운로드한 업데이트를 준비하지 못했습니다.");
                staged = result;
                SetState(UpdateState.Ready, "설치 준비 완료", "다운로드와 파일 확인이 끝났습니다", "설치하면 Chacha Capture를 종료한 뒤 새 버전으로 다시 실행합니다.", "캡처 기록과 설정은 그대로 유지됩니다.");
                progress.Value = 100;
            }
            catch (OperationCanceledException) { if (!closing && !IsDisposed) ShowAvailable(true); }
            catch (Exception error) { if (!closing && !IsDisposed) ShowError("업데이트를 다운로드하지 못했습니다", error); }
            finally { FinishOperation(request); }
        }

        internal void InstallUpdate()
        {
            if (operation != null || closing || IsDisposed || staged == null || state == UpdateState.Installing) return;
            SetState(UpdateState.Installing, "설치 시작", "새 버전으로 다시 시작합니다", "열려 있는 이미지와 설정을 저장한 뒤 앱을 종료합니다.", "잠시만 기다려 주세요.");
            try
            {
                install(staged);
                installerStarted = true;
                staged = null;
            }
            catch (Exception error)
            {
                ShowError("설치를 시작하지 못했습니다", error);
                return;
            }
            // The installer is now waiting for this process to exit; preserve the app's normal shutdown path.
            try { shutdown(); }
            finally { Close(); }
        }

        private CancellationTokenSource StartOperation(UpdateState next, string label, string title, string text)
        {
            operation = new CancellationTokenSource(); cancelRequested = false;
            SetState(next, label, title, text, "");
            progress.Style = ProgressBarStyle.Marquee;
            return operation;
        }

        private void FinishOperation(CancellationTokenSource request)
        {
            if (operation == request) { operation = null; cancelRequested = false; }
            request.Dispose();
            if (!closing && !IsDisposed) UpdateButtons();
        }

        private void ShowAvailable(bool canceled)
        {
            SetState(UpdateState.Available, "새 버전", "v" + VersionText(release.Version) + " 업데이트가 있습니다",
                "다운로드한 뒤 원하는 시점에 설치할 수 있습니다.", canceled ? "다운로드를 중지했습니다. 다시 다운로드할 수 있습니다." :
                (release.AssetSize > 0 ? "Windows 64비트  ·  " + SizeText(release.AssetSize) : "Windows 64비트"));
        }

        private void ShowError(string title, Exception error)
        {
            SetState(UpdateState.Error, "다시 시도해 주세요", title, "아래 내용을 확인하거나 GitHub 릴리스 페이지를 이용해 주세요.", "");
            notes.Text = error.Message + (release == null || String.IsNullOrWhiteSpace(release.ReleaseNotes) ? "" : "\r\n\r\n릴리스 노트\r\n" + NormalizeLines(release.ReleaseNotes));
        }

        private void SetState(UpdateState next, string label, string title, string text, string detail)
        {
            state = next; stateLabel.Text = label; headline.Text = title; description.Text = text; progressText.Text = detail;
            stateLabel.ForeColor = next == UpdateState.Error ? Color.FromArgb(255, 179, 150) : Ui.Accent;
            progress.Style = ProgressBarStyle.Continuous; progress.Value = 0;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool busy = operation != null || state == UpdateState.Installing;
            checkButton.Enabled = !busy;
            cancelButton.Enabled = state != UpdateState.Installing && !cancelRequested;
            cancelButton.Text = busy ? (cancelRequested ? "중지 중…" : "중지") : "닫기";
            actionButton.Enabled = !busy;
            if (state == UpdateState.Ready || (state == UpdateState.Error && staged != null)) actionButton.Text = "설치하고 다시 시작";
            else if (state == UpdateState.Available || (state == UpdateState.Error && release != null && release.IsUpdateAvailable)) actionButton.Text = state == UpdateState.Error ? "다시 다운로드" : "다운로드";
            else if (state == UpdateState.Latest) actionButton.Text = "확인";
            else if (state == UpdateState.Checking) actionButton.Text = "확인 중…";
            else if (state == UpdateState.Downloading) actionButton.Text = "다운로드 중…";
            else if (state == UpdateState.Installing) actionButton.Text = "설치 시작 중…";
            else actionButton.Text = "업데이트 확인";
        }

        private void CancelOrClose()
        {
            if (operation == null) { Close(); return; }
            if (cancelRequested) return;
            CancellationTokenSource request = operation;
            cancelRequested = true;
            description.Text = "진행 중인 작업을 중지하고 있습니다.";
            UpdateButtons();
            request.Cancel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            closing = true;
            if (operation != null) operation.Cancel();
            DiscardStaged();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                if (operation != null) operation.Cancel();
                DiscardStaged();
                if (!ownedIconDisposed)
                {
                    ownedIconDisposed = true;
                    Icon = null;
                    if (ownedIcon != null) ownedIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void DiscardStaged()
        {
            StagedUpdate abandoned = staged;
            staged = null;
            TryDiscard(abandoned);
        }

        private void TryDiscard(StagedUpdate abandoned)
        {
            if (abandoned == null || installerStarted) return;
            try { discard(abandoned); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (System.Security.SecurityException) { }
        }

        private void OpenReleasePage()
        {
            string url = "https://github.com/youkdonghun/chacha/releases";
            Uri address;
            if (release != null && Uri.TryCreate(release.ReleaseUrl, UriKind.Absolute, out address) &&
                address.Scheme == Uri.UriSchemeHttps && address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                address.AbsolutePath.StartsWith("/youkdonghun/chacha/releases/", StringComparison.OrdinalIgnoreCase)) url = address.AbsoluteUri;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, "릴리스 페이지를 열지 못했습니다.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private static string DisplayVersion(Version value) { return value == null ? "1.2.0" : value.ToString(3); }
        private static string VersionText(string value) { return String.IsNullOrWhiteSpace(value) ? "최신" : value.Trim().TrimStart('v', 'V'); }
        private static string NormalizeLines(string value) { return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }
        private static string SizeText(long bytes)
        {
            return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : bytes >= 1024 ? (bytes / 1024.0).ToString("0.0") + " KB" : Math.Max(0, bytes) + " B";
        }
    }
}
