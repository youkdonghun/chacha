using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace ChachaCapture
{
    internal static class OcrService
    {
        internal sealed class Response { public string text { get; set; } public string error { get; set; } }
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        internal static Action<int> WorkerStartedForTest;
        internal static string CacheRootForTest;
        internal static int TimeoutMilliseconds = 90000;

        internal static async Task<string> RecognizeAsync(Bitmap image, string mode, bool enlarge, CancellationToken cancellation)
        {
            await Gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                return await Task.Run(async delegate
                {
                    cancellation.ThrowIfCancellationRequested();
                    if ((long)image.Width * image.Height > OcrPreprocessing.MaximumPixels)
                        throw new ArgumentException("읽을 영역이 너무 큽니다. 텍스트 부분을 좁혀서 캡처해 주세요.");
                    string runtime = EnsureRuntime(cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    using (Process process = new Process())
                    using (WorkerJob job = new WorkerJob())
                    using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        timeout.CancelAfter(TimeoutMilliseconds);
                        process.StartInfo = new ProcessStartInfo(Path.Combine(runtime, "ChachaOcr.exe"), (mode == "line" || mode == "block" ? mode : "auto") + (enlarge ? " large" : " normal")) {
                            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtime,
                            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                        process.StartInfo.EnvironmentVariables["OMP_THREAD_LIMIT"] = "2";
                        process.Start();
                        try
                        {
                            job.Attach(process);
                            using (timeout.Token.Register(delegate { StopOwnedWorker(process); }))
                            {
                                if (WorkerStartedForTest != null) WorkerStartedForTest(process.Id);
                                Task<string> output = process.StandardOutput.ReadToEndAsync();
                                Task<string> errors = process.StandardError.ReadToEndAsync();
                                try
                                {
                                    using (MemoryStream png = new MemoryStream())
                                    {
                                        image.Save(png, System.Drawing.Imaging.ImageFormat.Png); png.Position = 0;
                                        await png.CopyToAsync(process.StandardInput.BaseStream, 32768, timeout.Token).ConfigureAwait(false);
                                    }
                                    process.StandardInput.Close();
                                    process.WaitForExit();
                                    string result = await output.ConfigureAwait(false);
                                    await errors.ConfigureAwait(false);
                                    timeout.Token.ThrowIfCancellationRequested();
                                    Response response = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<Response>(result);
                                    if (process.ExitCode != 0 || response == null || response.error != null)
                                        throw new InvalidOperationException("텍스트를 읽지 못했습니다. 다시 시도해 주세요. 인식 도구를 시작하지 못하면 Microsoft Visual C++ 2015–2022 x64 런타임 설치를 확인해 주세요.");
                                    return (response.text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                                }
                                catch
                                {
                                    StopOwnedWorker(process);
                                    output.GetAwaiter().GetResult(); errors.GetAwaiter().GetResult();
                                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                                    if (timeout.IsCancellationRequested) throw new TimeoutException("텍스트 읽기가 오래 걸려 중단했습니다. 영역을 좁히거나 작은 글자 확대를 끄고 다시 시도해 주세요.");
                                    throw;
                                }
                            }
                        }
                        finally { StopOwnedWorker(process); process.WaitForExit(); }
                    }
                }).ConfigureAwait(false);
            }
            finally { Gate.Release(); }
        }

        private static void StopOwnedWorker(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        internal static string EnsureRuntime(CancellationToken cancellation)
        {
            Assembly assembly = typeof(OcrService).Assembly;
            using (Stream payload = assembly.GetManifestResourceStream("Chacha.Ocr.zip"))
            {
                if (payload == null) throw new InvalidDataException("텍스트 인식 파일이 없습니다. 최신 ChachaCapture.exe를 다시 받아 주세요.");
                string key = Hash(payload).Substring(0, 24); payload.Position = 0;
                string root = Path.Combine(CacheRootForTest ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChachaCapture", "ocr"), key);
                Directory.CreateDirectory(root);
                using (Mutex mutex = new Mutex(false, "Local\\ChachaOcr-" + key))
                {
                    bool held = false;
                    try
                    {
                        try { held = mutex.WaitOne(30000); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("다른 텍스트 인식 작업이 준비 중입니다. 잠시 후 다시 시도해 주세요.");
                        using (ZipArchive zip = new ZipArchive(payload, ZipArchiveMode.Read))
                        {
                            foreach (ZipArchiveEntry entry in zip.Entries)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                if (entry.Name.Length == 0) continue;
                                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid OCR payload path.");
                                string expected; using (Stream data = entry.Open()) expected = Hash(data);
                                bool valid = false;
                                if (File.Exists(target)) using (Stream existing = File.OpenRead(target)) valid = Hash(existing) == expected;
                                if (valid) continue;
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                                try
                                {
                                    entry.ExtractToFile(temporary);
                                    if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                                }
                                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                            }
                        }
                    }
                    finally { if (held) mutex.ReleaseMutex(); }
                }
                return root;
            }
        }

        private static string Hash(Stream stream)
        { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }

        // Closing the app (including a crash) closes this handle and ends only its OCR worker.
        private sealed class WorkerJob : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal WorkerJob() : base(true)
            {
                SetHandle(CreateJobObject(IntPtr.Zero, null));
                if (IsInvalid) throw new System.ComponentModel.Win32Exception();
                ExtendedLimits limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000;
                if (!SetInformationJobObject(this, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits)))) { Dispose(); throw new System.ComponentModel.Win32Exception(); }
            }
            internal void Attach(Process process)
            { if (!AssignProcessToJobObject(this, process.Handle)) throw new System.ComponentModel.Win32Exception(); }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public UIntPtr Minimum, Maximum; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(WorkerJob job, int info, ref ExtendedLimits value, int length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(WorkerJob job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
