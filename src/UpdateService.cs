using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ChachaCapture
{
    public sealed class UpdateInfo
    {
        public string CurrentVersion { get; internal set; }
        public string Version { get; internal set; }
        public string Tag { get; internal set; }
        public string Name { get; internal set; }
        public string ReleaseUrl { get; internal set; }
        public string ReleaseNotes { get; internal set; }
        public string DownloadUrl { get; internal set; }
        public string ChecksumUrl { get; internal set; }
        public string AssetApiUrl { get; internal set; }
        public string ChecksumApiUrl { get; internal set; }
        public long AssetSize { get; internal set; }
        public bool IsUpdateAvailable { get; internal set; }
    }

    public sealed class UpdateProgress
    {
        public string Phase { get; internal set; }
        public long BytesReceived { get; internal set; }
        public long TotalBytes { get; internal set; }
        public int Percent { get { return TotalBytes > 0 ? (int)Math.Min(100, BytesReceived * 100 / TotalBytes) : -1; } }
    }

    public sealed class StagedUpdate
    {
        public string ManifestPath { get; internal set; }
        public string ExecutablePath { get; internal set; }
        public string Version { get; internal set; }
        internal string Directory;
        internal string Checksum;
        internal string TargetPath;
        internal string Tag;
        internal string DownloadUrl;
    }

    /// <summary>Explicit user-initiated release updates, verified before a separate helper replaces the running executable.</summary>
    public static class UpdateService
    {
        private const string Repository = "youkdonghun/chacha";
        private const string ExecutableName = "ChachaCapture.exe";
        private const string ChecksumName = "SHA256SUMS.txt";
        private const string HelperName = "ChachaUpdater.exe";
        private const string StagePrefix = "ChachaCapture-Update-";
        private const long MaximumExecutableBytes = 64L * 1024 * 1024;
        private const string LatestEndpoint = "https://api.github.com/repos/youkdonghun/chacha/releases/latest";

        public static string CurrentVersion { get { return DisplayVersion(InstalledVersion()); } }

        public static Task<UpdateInfo> CheckAsync(CancellationToken cancellation)
        {
            return Task.Run(delegate
            {
                string token = ReadGitHubToken(cancellation);
                byte[] bytes = FetchBytes(new Uri(LatestEndpoint), 1024 * 1024, cancellation, null, 0, true, token);
                return ParseRelease(new UTF8Encoding(false, true).GetString(bytes), InstalledVersion());
            }, cancellation);
        }

        public static Task<StagedUpdate> DownloadAsync(UpdateInfo update, IProgress<UpdateProgress> progress, CancellationToken cancellation)
        {
            if (update == null) throw new ArgumentNullException("update");
            return Task.Run(delegate
            {
                cancellation.ThrowIfCancellationRequested();
                if (!update.IsUpdateAvailable || CompareVersionTag(update.Tag, InstalledVersion()) <= 0)
                    throw new InvalidOperationException("현재 버전보다 새로운 업데이트가 아닙니다.");
                Uri executableUri = ValidateAssetUri(update.DownloadUrl, update.Tag, ExecutableName);
                Uri checksumUri = ValidateAssetUri(update.ChecksumUrl, update.Tag, ChecksumName);
                if (!String.IsNullOrEmpty(update.AssetApiUrl)) executableUri = ValidateAssetApiUri(update.AssetApiUrl);
                if (!String.IsNullOrEmpty(update.ChecksumApiUrl)) checksumUri = ValidateAssetApiUri(update.ChecksumApiUrl);
                string token = ReadGitHubToken(cancellation);
                if (update.AssetSize <= 0 || update.AssetSize > MaximumExecutableBytes) throw new InvalidDataException("업데이트 파일 크기가 올바르지 않습니다.");
                Report(progress, "검증 정보 확인 중", 0, 0);
                string checksumText = new UTF8Encoding(false, true).GetString(FetchBytes(checksumUri, 128 * 1024, cancellation, null, 0, false, token));
                string expected = ParseChecksum(checksumText, ExecutableName);
                string stage = Path.Combine(Path.GetTempPath(), StagePrefix + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                ValidateStageDirectory(stage);
                string partial = Path.Combine(stage, "download.partial");
                string executable = Path.Combine(stage, ExecutableName);
                try
                {
                    using (FileStream file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        DownloadTo(executableUri, file, MaximumExecutableBytes, update.AssetSize, cancellation, progress, false, token);
                    cancellation.ThrowIfCancellationRequested();
                    if (new FileInfo(partial).Length != update.AssetSize) throw new InvalidDataException("다운로드한 파일 크기가 릴리스 정보와 다릅니다.");
                    Report(progress, "파일 확인 중", update.AssetSize, update.AssetSize);
                    VerifyChecksum(partial, expected);
                    ValidateAmd64Executable(partial);
                    ValidateApplicationVersion(partial, ParseVersionTag(update.Tag));
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(partial, executable);
                    Report(progress, "설치 준비 완료", update.AssetSize, update.AssetSize);
                    return new StagedUpdate { Directory = stage, ExecutablePath = executable, ManifestPath = Path.Combine(stage, "update.json"),
                        Version = update.Version, Checksum = expected, TargetPath = CurrentExecutablePath(), Tag = update.Tag, DownloadUrl = update.DownloadUrl };
                }
                catch
                {
                    DeleteDownloadStage(stage);
                    throw;
                }
            }, cancellation);
        }

        public static void StartInstaller(StagedUpdate update) { StartInstaller(update, new string[0]); }

        public static void Discard(StagedUpdate update)
        {
            if (update == null) return;
            string stage = ValidateStageDirectory(update.Directory);
            RequireSamePath(update.ExecutablePath, Path.Combine(stage, ExecutableName), "업데이트 임시 파일 경로가 올바르지 않습니다.");
            // Once handed to a helper, its own files must remain available until it exits.
            if (File.Exists(Path.Combine(stage, "update.json")) || File.Exists(Path.Combine(stage, HelperName))) return;
            DeleteDownloadStage(stage);
        }

        public static void StartInstaller(StagedUpdate update, string[] restartArgs)
        {
            if (update == null) throw new ArgumentNullException("update");
            string stage = ValidateStageDirectory(update.Directory);
            string executable = Path.Combine(stage, ExecutableName);
            RequireSamePath(update.ExecutablePath, executable, "업데이트 임시 파일 경로가 올바르지 않습니다.");
            RequireSamePath(update.ManifestPath, Path.Combine(stage, "update.json"), "설치 정보 경로가 올바르지 않습니다.");
            string target = CurrentExecutablePath();
            RequireSamePath(target, update.TargetPath, "업데이트를 내려받은 프로그램과 설치 대상이 다릅니다.");
            ValidateAssetUri(update.DownloadUrl, update.Tag, ExecutableName);
            VerifyChecksum(executable, update.Checksum);
            ValidateAmd64Executable(executable);
            ValidateApplicationVersion(executable, ParseVersionTag(update.Tag));
            string[] arguments = ValidateRestartArguments(restartArgs);
            string helper = Path.Combine(stage, HelperName);
            string ready = Path.Combine(stage, "ready.txt"), cancel = Path.Combine(stage, "cancel.txt"), error = Path.Combine(stage, "install-error.txt");
            if (File.Exists(ready)) File.Delete(ready);
            if (File.Exists(cancel)) File.Delete(cancel);
            if (File.Exists(error)) File.Delete(error);
            File.Copy(target, helper, true);
            InstallManifest manifest;
            using (Process current = Process.GetCurrentProcess())
            {
                manifest = new InstallManifest { FormatVersion = 1, Token = Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow.Ticks,
                    ParentPid = current.Id, ParentStartUtc = current.StartTime.ToUniversalTime().Ticks, TargetPath = target, StagedPath = executable,
                    HelperPath = helper, ExpectedHash = update.Checksum, OriginalHash = Sha256(target), ReleaseTag = update.Tag,
                    ReleaseVersion = update.Version, DownloadUrl = update.DownloadUrl, RestartArguments = arguments };
            }
            File.WriteAllText(update.ManifestPath, Serializer().Serialize(manifest), new UTF8Encoding(false));
            Process process = Process.Start(new ProcessStartInfo(helper, "--apply-update " + QuoteArgument(update.ManifestPath))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage, WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null) throw new InvalidOperationException("업데이트 설치 도우미를 시작하지 못했습니다.");
            using (process)
            {
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 10000)
                {
                    string readyToken;
                    if (TryReadSmallFile(ready, out readyToken) && readyToken.Trim() == manifest.Token) return;
                    if (process.HasExited) break;
                    Thread.Sleep(50);
                }
                File.WriteAllText(cancel, manifest.Token);
                process.WaitForExit(2000);
                string details;
                if (!TryReadSmallFile(error, out details)) details = "설치 도우미가 실행 중인 프로그램을 확인하지 못했습니다.";
                throw new InvalidOperationException(details);
            }
        }

        /// <summary>Call before the application's single-instance mutex or normal UI initialization.</summary>
        public static bool TryHandleUpdate(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length == 0 || args[0] != "--apply-update") return false;
            try
            {
                if (args.Length != 2) throw new ArgumentException("설치 정보 파일이 필요합니다.");
                ApplyUpdate(args[1]);
            }
            catch (Exception error)
            {
                exitCode = 1;
                try
                {
                    string own = CurrentExecutablePath();
                    if (String.Equals(Path.GetFileName(own), HelperName, StringComparison.OrdinalIgnoreCase))
                    {
                        string stage = ValidateStageDirectory(Path.GetDirectoryName(own));
                        File.WriteAllText(Path.Combine(stage, "install-error.txt"), "업데이트를 완료하지 못했습니다.\r\n" + error.Message, new UTF8Encoding(false));
                    }
                }
                catch { }
            }
            return true;
        }

        public static bool TryReadUpdateError(string[] args, out string message)
        {
            message = null;
            if (args == null) return false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "--update-error") continue;
                try
                {
                    string path = FullPath(args[i + 1]);
                    string stage = ValidateStageDirectory(Path.GetDirectoryName(path));
                    RequireSamePath(path, Path.Combine(stage, "install-error.txt"), "업데이트 오류 파일 경로가 올바르지 않습니다.");
                    if (TryReadSmallFile(path, out message)) return true;
                }
                catch { }
            }
            return false;
        }

        private static void ApplyUpdate(string manifestPath)
        {
            string helper = CurrentExecutablePath();
            if (!String.Equals(Path.GetFileName(helper), HelperName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("업데이트는 별도의 설치 도우미로만 적용할 수 있습니다.");
            string stage = ValidateStageDirectory(Path.GetDirectoryName(helper));
            RequireSamePath(manifestPath, Path.Combine(stage, "update.json"), "도우미와 설치 정보의 폴더가 다릅니다.");
            FileInfo information = new FileInfo(manifestPath);
            if (!information.Exists || information.Length > 65536) throw new InvalidDataException("설치 정보가 없거나 너무 큽니다.");
            InstallManifest manifest = Serializer().Deserialize<InstallManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.FormatVersion != 1 || manifest.ParentPid <= 0 || !Regex.IsMatch(manifest.Token ?? "", "^[a-f0-9]{32}$"))
                throw new InvalidDataException("설치 정보가 올바르지 않습니다.");
            long age = DateTime.UtcNow.Ticks - manifest.CreatedUtc;
            if (age < -TimeSpan.FromMinutes(1).Ticks || age > TimeSpan.FromMinutes(15).Ticks) throw new InvalidDataException("설치 정보가 만료되었습니다. 업데이트를 다시 시작하세요.");
            RequireSamePath(manifest.HelperPath, helper, "설치 도우미 경로가 다릅니다.");
            RequireSamePath(manifest.StagedPath, Path.Combine(stage, ExecutableName), "업데이트 원본 경로가 다릅니다.");
            string target = FullPath(manifest.TargetPath);
            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsInside(target, stage)) throw new InvalidDataException("실행 파일 설치 대상이 올바르지 않습니다.");
            ValidateAssetUri(manifest.DownloadUrl, manifest.ReleaseTag, ExecutableName);
            manifest.RestartArguments = ValidateRestartArguments(manifest.RestartArguments);
            VerifyChecksum(helper, manifest.OriginalHash);
            VerifyChecksum(target, manifest.OriginalHash);
            VerifyChecksum(manifest.StagedPath, manifest.ExpectedHash);
            ValidateAmd64Executable(manifest.StagedPath);
            Version release = ParseVersionTag(manifest.ReleaseTag);
            ValidateApplicationVersion(manifest.StagedPath, release);
            if (NormalizeVersion(release).CompareTo(NormalizeVersion(AssemblyName.GetAssemblyName(target).Version)) <= 0)
                throw new InvalidDataException("설치할 파일이 현재 실행 파일보다 새로운 버전이 아닙니다.");

            // Parent remains alive until StartInstaller receives this handshake. PID reuse is rejected by start time and path.
            using (Process parent = Process.GetProcessById(manifest.ParentPid))
            {
                if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != manifest.ParentStartUtc)
                    throw new InvalidOperationException("업데이트를 요청한 프로그램을 확인할 수 없습니다.");
                RequireSamePath(parent.MainModule.FileName, target, "업데이트 요청 프로세스와 대상 실행 파일이 다릅니다.");
                File.WriteAllText(Path.Combine(stage, "ready.txt"), manifest.Token);
                Stopwatch waiting = Stopwatch.StartNew();
                while (!parent.WaitForExit(200))
                {
                    if (File.Exists(Path.Combine(stage, "cancel.txt"))) throw new OperationCanceledException("설치가 취소되었습니다.");
                    if (waiting.ElapsedMilliseconds > 120000) throw new TimeoutException("프로그램이 종료되지 않아 업데이트를 적용하지 않았습니다.");
                }
            }

            string replacement = target + ".update-" + manifest.Token + ".tmp";
            string backup = target + ".backup-" + manifest.Token;
            bool replaced = false;
            try
            {
                if (File.Exists(Path.Combine(stage, "cancel.txt"))) throw new OperationCanceledException("설치가 취소되었습니다.");
                WaitForWritableTarget(target);
                VerifyChecksum(target, manifest.OriginalHash);
                VerifyChecksum(manifest.StagedPath, manifest.ExpectedHash);
                File.Copy(manifest.StagedPath, replacement, false);
                VerifyChecksum(replacement, manifest.ExpectedHash);
                File.Replace(replacement, target, backup);
                replaced = true;
                Process launched = Launch(target, manifest.RestartArguments);
                using (launched)
                {
                    if (launched.WaitForExit(4000) && !HasRunningTarget(target))
                        throw new InvalidOperationException("새 버전이 시작 직후 종료되었습니다.");
                }
            }
            catch (Exception failure)
            {
                string rollback = "기존 실행 파일을 유지했습니다.";
                bool restored = !replaced;
                if (replaced)
                {
                    try
                    {
                        if (File.Exists(target)) File.Replace(backup, target, null);
                        else File.Move(backup, target);
                        rollback = "이전 버전으로 복원했습니다.";
                        restored = true;
                    }
                    catch (Exception restoreError) { rollback = "자동 복원에 실패했습니다. 백업: " + backup + "\r\n" + restoreError.Message; }
                }
                string details = "업데이트를 완료하지 못했습니다.\r\n" + failure.Message + "\r\n" + rollback;
                string errorPath = Path.Combine(stage, "install-error.txt");
                try { File.WriteAllText(errorPath, details, new UTF8Encoding(false)); } catch { }
                bool notified = false;
                try
                {
                    if (restored && File.Exists(target) && !HasRunningTarget(target))
                    {
                        List<string> restart = new List<string>(manifest.RestartArguments);
                        restart.Add("--update-error"); restart.Add(errorPath);
                        using (Process previous = Launch(target, restart.ToArray())) { notified = true; }
                    }
                }
                catch { }
                if (!notified)
                    System.Windows.Forms.MessageBox.Show(details, "Chacha Capture 업데이트", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                throw new InvalidOperationException(failure.Message + "\r\n" + rollback, failure);
            }
            finally
            {
                try { if (File.Exists(replacement)) File.Delete(replacement); } catch { }
            }
            // The new application is now running. Housekeeping failures must never roll back a committed update.
            try { File.Copy(backup, Path.Combine(stage, "previous.exe"), true); File.Delete(backup); } catch { }
            try { File.WriteAllText(Path.Combine(stage, "install-result.txt"), "설치 완료: " + manifest.ReleaseVersion, new UTF8Encoding(false)); } catch { }
        }

        private static Process Launch(string target, string[] args)
        {
            string[] quoted = new string[args.Length];
            for (int i = 0; i < args.Length; i++) quoted[i] = QuoteArgument(args[i]);
            Process process = Process.Start(new ProcessStartInfo(target, String.Join(" ", quoted))
            { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) });
            if (process == null) throw new InvalidOperationException("프로그램을 다시 시작하지 못했습니다.");
            return process;
        }

        private static void WaitForWritableTarget(string target)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (true)
            {
                try { using (FileStream stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return; }
                catch (IOException) { if (wait.ElapsedMilliseconds >= 15000) throw; Thread.Sleep(150); }
            }
        }

        private static bool HasRunningTarget(string target)
        {
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)))
                using (process)
                {
                    try { if (!process.HasExited && SamePath(process.MainModule.FileName, target)) return true; }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            return false;
        }

        internal static UpdateInfo ParseRelease(string json, Version current)
        {
            Dictionary<string, object> release = Serializer().DeserializeObject(json) as Dictionary<string, object>;
            if (release == null || BooleanField(release, "draft") || BooleanField(release, "prerelease")) throw new InvalidDataException("안정 버전 릴리스 정보가 아닙니다.");
            string tag = StringField(release, "tag_name");
            Version version = ParseVersionTag(tag);
            string releaseUrl = "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag);
            string reportedPage = StringField(release, "html_url");
            if (reportedPage != releaseUrl) throw new InvalidDataException("릴리스 저장소 주소가 올바르지 않습니다.");
            object rawAssets;
            object[] assets = release.TryGetValue("assets", out rawAssets) ? rawAssets as object[] : null;
            if (assets == null || assets.Length > 100) throw new InvalidDataException("업데이트 첨부 파일 정보를 찾을 수 없습니다.");
            Dictionary<string, object> binary = null, sums = null;
            foreach (object raw in assets)
            {
                Dictionary<string, object> asset = raw as Dictionary<string, object>;
                if (asset == null) continue;
                string name = StringField(asset, "name");
                if (name == ExecutableName) { if (binary != null) throw new InvalidDataException("업데이트 실행 파일이 중복되었습니다."); binary = asset; }
                if (name == ChecksumName) { if (sums != null) throw new InvalidDataException("검증 파일이 중복되었습니다."); sums = asset; }
            }
            if (binary == null || sums == null) throw new InvalidDataException("릴리스에 ChachaCapture.exe 또는 SHA256SUMS.txt가 없습니다.");
            string download = StringField(binary, "browser_download_url"), checksum = StringField(sums, "browser_download_url");
            ValidateAssetUri(download, tag, ExecutableName); ValidateAssetUri(checksum, tag, ChecksumName);
            string assetApiUrl = StringField(binary, "url"), checksumApiUrl = StringField(sums, "url");
            if (!String.IsNullOrEmpty(assetApiUrl)) ValidateAssetApiUri(assetApiUrl);
            if (!String.IsNullOrEmpty(checksumApiUrl)) ValidateAssetApiUri(checksumApiUrl);
            long size = LongField(binary, "size"), checksumSize = LongField(sums, "size");
            if (size < 256 || size > MaximumExecutableBytes || checksumSize <= 0 || checksumSize > 128 * 1024) throw new InvalidDataException("업데이트 첨부 파일 크기가 올바르지 않습니다.");
            return new UpdateInfo { CurrentVersion = DisplayVersion(current), Version = DisplayVersion(version), Tag = tag,
                Name = StringField(release, "name"), ReleaseUrl = releaseUrl, ReleaseNotes = StringField(release, "body"),
                DownloadUrl = download, ChecksumUrl = checksum, AssetApiUrl = assetApiUrl, ChecksumApiUrl = checksumApiUrl,
                AssetSize = size, IsUpdateAvailable = NormalizeVersion(version).CompareTo(NormalizeVersion(current)) > 0 };
        }

        internal static int CompareVersionTag(string tag, Version current)
        {
            return NormalizeVersion(ParseVersionTag(tag)).CompareTo(NormalizeVersion(current));
        }

        private static Version ParseVersionTag(string tag)
        {
            if (!Regex.IsMatch(tag ?? "", @"^[vV]?\d+\.\d+(?:\.\d+){0,2}$")) throw new InvalidDataException("지원하지 않는 릴리스 버전 형식입니다.");
            string text = tag[0] == 'v' || tag[0] == 'V' ? tag.Substring(1) : tag;
            Version version;
            if (!Version.TryParse(text, out version)) throw new InvalidDataException("릴리스 버전을 읽을 수 없습니다.");
            return NormalizeVersion(version);
        }

        internal static Uri ValidateAssetUri(string text, string tag, string name)
        {
            ParseVersionTag(tag);
            if (name != ExecutableName && name != ChecksumName) throw new InvalidDataException("지원하지 않는 업데이트 파일입니다.");
            Uri uri;
            string path = "/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort ||
                !String.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != path || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException("업데이트 파일 주소가 공식 저장소와 일치하지 않습니다.");
            return uri;
        }

        internal static string ParseChecksum(string text, string expectedName)
        {
            if (expectedName != ExecutableName) throw new InvalidDataException("지원하지 않는 검증 파일 이름입니다.");
            string result = null;
            foreach (string raw in (text ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = Regex.Match(raw.TrimStart('\uFEFF'), @"^([a-fA-F0-9]{64})[ \t]+\*?([^\r\n]+?)\s*$");
                if (!match.Success || match.Groups[2].Value != expectedName) continue;
                if (result != null) throw new InvalidDataException("실행 파일 검증 값이 중복되었습니다.");
                result = match.Groups[1].Value.ToLowerInvariant();
            }
            if (result == null) throw new InvalidDataException("ChachaCapture.exe의 SHA-256 검증 값을 찾을 수 없습니다.");
            return result;
        }

        internal static Uri ValidateAssetApiUri(string text)
        {
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment) ||
                !Regex.IsMatch(uri.AbsolutePath, "^/repos/youkdonghun/chacha/releases/assets/[1-9][0-9]*$"))
                throw new InvalidDataException("업데이트 첨부 파일 API 주소가 공식 저장소와 일치하지 않습니다.");
            return uri;
        }

        internal static void ValidateAmd64Executable(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < 256 || reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("다운로드한 파일이 Windows 실행 파일이 아닙니다.");
                stream.Position = 0x3C;
                int offset = reader.ReadInt32();
                if (offset < 64 || offset > stream.Length - 24) throw new InvalidDataException("실행 파일 헤더가 손상되었습니다.");
                stream.Position = offset;
                if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("업데이트 파일이 Windows x64(AMD64) 실행 파일이 아닙니다.");
                int sections = reader.ReadUInt16();
                stream.Position = offset + 20;
                int optionalSize = reader.ReadUInt16(), characteristics = reader.ReadUInt16();
                if (sections < 1 || sections > 96 || optionalSize < 112 || (characteristics & 2) == 0 || (characteristics & 0x2000) != 0 ||
                    (long)offset + 24 + optionalSize + sections * 40 > stream.Length || reader.ReadUInt16() != 0x20B)
                    throw new InvalidDataException("유효한 x64 실행 파일 헤더가 아닙니다.");
                for (int i = 0; i < sections; i++)
                {
                    stream.Position = offset + 24 + optionalSize + i * 40 + 16;
                    uint length = reader.ReadUInt32(), position = reader.ReadUInt32();
                    if (length > 0 && ((long)position + length > stream.Length || position == 0)) throw new InvalidDataException("실행 파일 섹션이 손상되었습니다.");
                }
            }
        }

        private static void ValidateApplicationVersion(string path, Version expected)
        {
            AssemblyName identity = AssemblyName.GetAssemblyName(path);
            if (identity.Name != "ChachaCapture" || NormalizeVersion(identity.Version) != NormalizeVersion(expected))
                throw new InvalidDataException("업데이트 실행 파일의 이름 또는 버전이 릴리스 정보와 다릅니다.");
        }

        private static byte[] FetchBytes(Uri uri, long limit, CancellationToken cancellation, IProgress<UpdateProgress> progress, long expected, bool api, string token)
        {
            using (MemoryStream memory = new MemoryStream()) { DownloadTo(uri, memory, limit, expected, cancellation, progress, api, token); return memory.ToArray(); }
        }

        private static void DownloadTo(Uri initial, Stream target, long limit, long expected, CancellationToken cancellation, IProgress<UpdateProgress> progress, bool api, string token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Uri current = initial;
            Stopwatch total = Stopwatch.StartNew();
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                cancellation.ThrowIfCancellationRequested();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(current);
                request.Method = "GET"; request.UserAgent = "ChachaCapture/" + CurrentVersion;
                request.Accept = api ? "application/vnd.github+json" : "application/octet-stream";
                if (String.Equals(current.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                    if (!String.IsNullOrEmpty(token)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                }
                request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = false;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (cancellation.Register(delegate { request.Abort(); }))
                {
                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        {
                            int status = (int)response.StatusCode;
                            if (status >= 300 && status <= 399)
                            {
                                string location = response.Headers[HttpResponseHeader.Location];
                                Uri next;
                                if (redirects == 5 || String.IsNullOrEmpty(location) || !Uri.TryCreate(current, location, out next) || !AllowedRedirect(next, initial, api))
                                    throw new InvalidDataException("업데이트 다운로드의 이동 주소가 올바르지 않습니다.");
                                current = next; continue;
                            }
                            if (status != 200) throw new InvalidDataException("업데이트 서버가 정상 파일을 반환하지 않았습니다.");
                            if (response.ContentLength > limit) throw new InvalidDataException("업데이트 응답 크기가 허용 범위를 초과합니다.");
                            long received = 0;
                            long reportedTotal = expected > 0 ? expected : response.ContentLength;
                            byte[] buffer = new byte[65536];
                            using (Stream input = response.GetResponseStream())
                            {
                                int count;
                                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                                {
                                    cancellation.ThrowIfCancellationRequested();
                                    received += count;
                                    if (received > limit || (expected > 0 && received > expected)) throw new InvalidDataException("다운로드한 파일이 선언된 크기를 초과합니다.");
                                    if (total.ElapsedMilliseconds > 300000) throw new TimeoutException("다운로드 시간이 초과되었습니다. 다시 시도하세요.");
                                    target.Write(buffer, 0, count);
                                    Report(progress, "다운로드 중", received, reportedTotal);
                                }
                            }
                            cancellation.ThrowIfCancellationRequested();
                            return;
                        }
                    }
                    catch (WebException error)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        HttpWebResponse failed = error.Response as HttpWebResponse;
                        if (failed != null)
                        {
                            using (failed)
                            {
                                if (failed.StatusCode == HttpStatusCode.Unauthorized || failed.StatusCode == HttpStatusCode.NotFound)
                                    throw new InvalidOperationException("업데이트 저장소에 접근할 수 없습니다. GitHub CLI에서 gh auth login으로 로그인하거나, 이 저장소를 읽을 수 있는 GH_TOKEN 환경 변수를 설정한 후 다시 확인하세요.", error);
                                if (failed.StatusCode == HttpStatusCode.Forbidden || (int)failed.StatusCode == 429) throw new InvalidOperationException("GitHub 요청 한도에 도달했습니다. 잠시 후 다시 확인하세요.", error);
                            }
                        }
                        throw new IOException("업데이트 서버에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 시도하세요.", error);
                    }
                }
            }
            throw new InvalidDataException("다운로드 이동 횟수를 초과했습니다.");
        }

        private static bool AllowedRedirect(Uri next, Uri original, bool api)
        {
            if (next.Scheme != Uri.UriSchemeHttps || !next.IsDefaultPort || !String.IsNullOrEmpty(next.UserInfo) || !String.IsNullOrEmpty(next.Fragment)) return false;
            if (api) return String.Equals(next.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) && next.AbsolutePath == original.AbsolutePath;
            if (String.Equals(next.Host, "github.com", StringComparison.OrdinalIgnoreCase) || String.Equals(next.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
                return String.Equals(next.Host, original.Host, StringComparison.OrdinalIgnoreCase) && next.AbsolutePath == original.AbsolutePath;
            return String.Equals(next.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) || String.Equals(next.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadGitHubToken(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string token = NormalizeToken(Environment.GetEnvironmentVariable("GH_TOKEN"));
            if (token == null) token = NormalizeToken(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
            if (token != null) return token;
            string executable = null;
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string directory = entry.Trim().Trim('"');
                    if (!Path.IsPathRooted(directory)) continue;
                    string candidate = Path.Combine(directory, "gh.exe");
                    if (File.Exists(candidate)) { executable = Path.GetFullPath(candidate); break; }
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            if (executable == null) return null;
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo(executable, "auth token --hostname github.com")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    if (process == null) return null;
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> errors = process.StandardError.ReadToEndAsync();
                    Stopwatch waiting = Stopwatch.StartNew();
                    while (!process.WaitForExit(50))
                    {
                        if (cancellation.IsCancellationRequested || waiting.ElapsedMilliseconds > 4500)
                        {
                            try { process.Kill(); } catch { }
                            cancellation.ThrowIfCancellationRequested();
                            return null;
                        }
                    }
                    if (process.ExitCode != 0 || !output.Wait(100) || !errors.Wait(100)) return null;
                    return NormalizeToken(output.Result);
                }
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
            catch (IOException) { return null; }
        }

        private static string NormalizeToken(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string token = value.Trim();
            if (token.Length > 8192) return null;
            foreach (char character in token) if (Char.IsWhiteSpace(character) || Char.IsControl(character)) return null;
            return token;
        }

        private static void VerifyChecksum(string path, string expected)
        {
            if (!Regex.IsMatch(expected ?? "", "^[a-fA-F0-9]{64}$") || !String.Equals(Sha256(path), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드 파일의 SHA-256 검증에 실패했습니다. 설치하지 않았습니다.");
        }

        private static string Sha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        private static string ValidateStageDirectory(string directory)
        {
            string full = FullPath(directory);
            string name = Path.GetFileName(full);
            Guid id;
            if (!SamePath(Path.GetDirectoryName(full), Path.GetTempPath()) || !name.StartsWith(StagePrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring(StagePrefix.Length), "N", out id) || !Directory.Exists(full) ||
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("업데이트 임시 폴더가 올바르지 않습니다.");
            return full;
        }

        private static void DeleteDownloadStage(string directory)
        {
            try
            {
                string stage = ValidateStageDirectory(directory);
                foreach (string name in new string[] { "download.partial", ExecutableName })
                { string path = Path.Combine(stage, name); if (File.Exists(path)) File.Delete(path); }
                Directory.Delete(stage, false);
            }
            catch { }
        }

        private static string[] ValidateRestartArguments(string[] args)
        {
            if (args == null) return new string[0];
            if (args.Length > 32) throw new ArgumentException("재시작 옵션이 너무 많습니다.");
            string[] result = (string[])args.Clone();
            int total = 0;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = result[i] ?? ""; total += result[i].Length;
                if (result[i] == "--apply-update" || result[i].IndexOf('\0') >= 0 || total > 16000) throw new ArgumentException("지원하지 않는 재시작 옵션입니다.");
            }
            return result;
        }

        private static bool TryReadSmallFile(string path, out string text)
        {
            text = null;
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length <= 0 || file.Length > 65536) return false;
                text = File.ReadAllText(path, new UTF8Encoding(false, true));
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static string QuoteArgument(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value ?? "")
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                result.Append('\\', slashes).Append(character); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static string FullPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidDataException("빈 파일 경로입니다.");
            string result = Path.GetFullPath(path);
            if (result.StartsWith(@"\\?\", StringComparison.Ordinal)) result = result.Substring(4);
            return result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        private static bool SamePath(string first, string second) { return String.Equals(FullPath(first), FullPath(second), StringComparison.OrdinalIgnoreCase); }
        private static bool IsInside(string file, string directory) { return FullPath(file).StartsWith(FullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        private static void RequireSamePath(string first, string second, string message) { if (!SamePath(first, second)) throw new InvalidDataException(message); }
        private static Version InstalledVersion() { return (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version; }
        private static string CurrentExecutablePath() { using (Process process = Process.GetCurrentProcess()) return FullPath(process.MainModule.FileName); }
        private static Version NormalizeVersion(Version version) { return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)); }
        private static string DisplayVersion(Version version) { Version normal = NormalizeVersion(version); return normal.Revision == 0 ? normal.ToString(3) : normal.ToString(4); }
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 }; }
        private static string StringField(Dictionary<string, object> item, string name) { object value; return item.TryGetValue(name, out value) ? value as string ?? "" : ""; }
        private static bool BooleanField(Dictionary<string, object> item, string name) { object value; return item.TryGetValue(name, out value) && value is bool && (bool)value; }
        private static long LongField(Dictionary<string, object> item, string name) { object value; long parsed; return item.TryGetValue(name, out value) && Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : -1; }
        private static void Report(IProgress<UpdateProgress> progress, string phase, long received, long total) { if (progress != null) progress.Report(new UpdateProgress { Phase = phase, BytesReceived = received, TotalBytes = total }); }

        public sealed class InstallManifest
        {
            public int FormatVersion;
            public string Token;
            public long CreatedUtc;
            public int ParentPid;
            public long ParentStartUtc;
            public string TargetPath;
            public string StagedPath;
            public string HelperPath;
            public string ExpectedHash;
            public string OriginalHash;
            public string ReleaseTag;
            public string ReleaseVersion;
            public string DownloadUrl;
            public string[] RestartArguments;
        }
    }
}
