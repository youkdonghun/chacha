using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChachaCapture
{
    /// <summary>Exercises the update dialog with isolated callbacks: no network, processes, or application controller.</summary>
    internal static class UpdateUiRegressionTests
    {
        internal static void Run()
        {
            LatestDoesNotDownload();
            CheckFailureCanRetry();
            CancelCheckAndPreventDuplicate();
            CancelDownloadAndPreventDuplicate();
            FailedInstallerKeepsApplicationOpen();
            DisposeCancelsPendingOperation();
            RepeatedDisposePreservesSharedIcon();
            DownloadStageOwnership();
            LateDownloadCompletionIsDiscarded(false);
            LateDownloadCompletionIsDiscarded(true);
        }

        private static UpdateInfo Available()
        {
            return new UpdateInfo { CurrentVersion = "1.2.0", Version = "1.3.0", Tag = "v1.3.0",
                Name = "Chacha Capture 1.3.0", IsUpdateAvailable = true, AssetSize = 4194304,
                ReleaseNotes = "새 버전\n캡처와 플로팅 동작 개선", ReleaseUrl = "https://github.com/youkdonghun/chacha/releases/tag/v1.3.0" };
        }

        private static void LatestDoesNotDownload()
        {
            int downloads = 0, installs = 0, shutdowns = 0;
            using (UpdateForm form = new UpdateForm(delegate { shutdowns++; }, delegate
            {
                UpdateInfo info = Available(); info.Version = "1.1.0"; info.IsUpdateAvailable = false;
                return Task.FromResult(info);
            }, delegate { downloads++; return Task.FromResult(new StagedUpdate()); }, delegate { installs++; }))
            {
                Await(form.CheckForUpdatesAsync());
                Require(form.StatusName == "Latest", "A newer local version was offered a downgrade.");
                Await(form.DownloadUpdateAsync()); form.InstallUpdate();
                Require(downloads == 0 && installs == 0 && shutdowns == 0, "A current/newer build triggered download, installation, or shutdown.");
                Require(Field<Button>(form, "actionButton").Text == "확인", "Latest-version state does not offer the close action.");
            }
        }

        private static void CheckFailureCanRetry()
        {
            int requests = 0;
            using (UpdateForm form = new UpdateForm(delegate { throw new InvalidOperationException("Unexpected shutdown."); }, delegate
            {
                requests++;
                if (requests > 1) return Task.FromResult(Available());
                TaskCompletionSource<UpdateInfo> failed = new TaskCompletionSource<UpdateInfo>();
                failed.SetException(new InvalidOperationException("네트워크 연결 실패")); return failed.Task;
            }, delegate { throw new InvalidOperationException("Unexpected download."); }, delegate { throw new InvalidOperationException("Unexpected installation."); }))
            {
                Await(form.CheckForUpdatesAsync());
                Require(form.StatusName == "Error" && Field<Button>(form, "checkButton").Enabled, "Check failure did not enable retry.");
                Require(Field<TextBox>(form, "notes").Text.Contains("네트워크 연결 실패"), "Check failure hid the error detail.");
                Await(form.CheckForUpdatesAsync());
                Require(requests == 2 && form.StatusName == "Available", "Check retry did not restore the available update.");
                Require(Field<TextBox>(form, "notes").Text.Contains("새 버전") && !Field<TextBox>(form, "notes").Text.Contains("네트워크"), "Successful retry retained the old error.");
            }
        }

        private static void CancelCheckAndPreventDuplicate()
        {
            int requests = 0; bool canceled = false;
            TaskCompletionSource<UpdateInfo> pending = new TaskCompletionSource<UpdateInfo>();
            using (UpdateForm form = new UpdateForm(delegate { throw new InvalidOperationException("Unexpected shutdown."); }, delegate(CancellationToken token)
            {
                requests++; token.Register(delegate { canceled = true; pending.TrySetCanceled(); }); return pending.Task;
            }, delegate { throw new InvalidOperationException("Unexpected download."); }, delegate { throw new InvalidOperationException("Unexpected installation."); }))
            {
                Task first = form.CheckForUpdatesAsync(); Await(form.CheckForUpdatesAsync());
                Require(requests == 1 && form.StatusName == "Checking", "Repeated check started a second request.");
                Require(!Field<Button>(form, "actionButton").Enabled, "Busy update action was enabled.");
                Invoke(form, "CancelOrClose"); Await(first);
                Require(canceled && form.StatusName == "Idle" && !form.IsDisposed, "Canceling the check closed the dialog or did not abort the request.");
                Require(!Field<Label>(form, "description").Text.Contains("중지하고"), "Finished cancellation still showed an in-progress message.");
            }
        }

        private static void CancelDownloadAndPreventDuplicate()
        {
            int downloads = 0; bool canceled = false;
            TaskCompletionSource<StagedUpdate> pending = new TaskCompletionSource<StagedUpdate>();
            using (UpdateForm form = new UpdateForm(delegate { throw new InvalidOperationException("Unexpected shutdown."); }, delegate { return Task.FromResult(Available()); },
                delegate(UpdateInfo info, IProgress<UpdateProgress> progress, CancellationToken token)
                {
                    downloads++; token.Register(delegate { canceled = true; pending.TrySetCanceled(); }); return pending.Task;
                }, delegate { throw new InvalidOperationException("Unexpected installation."); }))
            {
                Await(form.CheckForUpdatesAsync());
                Task first = form.DownloadUpdateAsync(); Await(form.DownloadUpdateAsync());
                Require(downloads == 1 && form.StatusName == "Downloading", "Repeated download started a second request.");
                Invoke(form, "CancelOrClose"); Await(first);
                Require(canceled && form.StatusName == "Available", "Canceled download did not preserve the release for retry.");
                Require(Field<Button>(form, "actionButton").Enabled && !form.IsDisposed, "Canceled download left the dialog unusable.");
                Require(!Field<Label>(form, "description").Text.Contains("중지하고"), "Finished download cancellation still showed an in-progress message.");
            }
        }

        private static void FailedInstallerKeepsApplicationOpen()
        {
            int attempts = 0, shutdowns = 0;
            using (UpdateForm form = new UpdateForm(delegate { shutdowns++; }, delegate { return Task.FromResult(Available()); },
                delegate { return Task.FromResult(new StagedUpdate { Version = "1.3.0" }); }, delegate
                {
                    attempts++;
                    Require(shutdowns == 0, "Shutdown ran before the installer started.");
                    if (attempts == 1) throw new InvalidOperationException("설치 도우미 실행 실패");
                }))
            {
                Await(form.CheckForUpdatesAsync()); Await(form.DownloadUpdateAsync());
                Require(form.StatusName == "Ready" && attempts == 0 && shutdowns == 0, "Download implicitly installed or exited the application.");
                form.InstallUpdate();
                Require(attempts == 1 && shutdowns == 0 && form.StatusName == "Error" && !form.IsDisposed, "Failed installer exited the application.");
                Require(Field<Button>(form, "actionButton").Text == "설치하고 다시 시작", "Failed installer did not preserve the staged retry.");
                form.InstallUpdate(); form.InstallUpdate();
                Require(attempts == 2 && shutdowns == 1, "Successful install or shutdown ran more than once.");
            }
        }

        private static void DisposeCancelsPendingOperation()
        {
            bool canceled = false;
            TaskCompletionSource<UpdateInfo> pending = new TaskCompletionSource<UpdateInfo>();
            UpdateForm form = new UpdateForm(delegate { throw new InvalidOperationException("Unexpected shutdown."); }, delegate(CancellationToken token)
            {
                token.Register(delegate { canceled = true; pending.TrySetCanceled(); }); return pending.Task;
            }, delegate { throw new InvalidOperationException("Unexpected download."); }, delegate { throw new InvalidOperationException("Unexpected installation."); });
            Task check = form.CheckForUpdatesAsync(); form.Dispose(); Await(check);
            Require(canceled, "Disposing the update dialog left its request running.");
        }

        private static void RepeatedDisposePreservesSharedIcon()
        {
            UpdateForm form = new UpdateForm(delegate { throw new InvalidOperationException("Unexpected shutdown."); },
                delegate { return Task.FromResult(Available()); },
                delegate { throw new InvalidOperationException("Unexpected download."); },
                delegate { throw new InvalidOperationException("Unexpected installation."); });
            IntPtr updateHandle = form.Handle;
            Require(updateHandle != IntPtr.Zero, "Update dialog native handle could not be created.");
            form.Dispose(); form.Dispose();
            using (Form probe = new Form())
            {
                Require(probe.Icon.Handle != IntPtr.Zero && probe.Handle != IntPtr.Zero,
                    "Disposing the update dialog corrupted the shared WinForms icon or prevented a later window from opening.");
            }
        }

        private static void DownloadStageOwnership()
        {
            StagedUpdate package = new StagedUpdate { Version = "1.3.0" };
            int discards = 0, installs = 0, shutdowns = 0;
            Action<StagedUpdate> discard = delegate(StagedUpdate abandoned)
            {
                Require(Object.ReferenceEquals(package, abandoned), "Cleanup received another download's stage."); discards++;
            };
            UpdateForm closed = new UpdateForm(delegate { shutdowns++; }, delegate { return Task.FromResult(Available()); },
                delegate { return Task.FromResult(package); }, delegate { installs++; }, discard);
            IntPtr closeHandle = closed.Handle;
            Await(closed.CheckForUpdatesAsync()); Await(closed.DownloadUpdateAsync());
            closed.Close(); closed.Dispose();
            Require(closeHandle != IntPtr.Zero && discards == 1 && installs == 0 && shutdowns == 0,
                "Closing and disposing an abandoned ready update did not discard its stage exactly once.");

            discards = 0;
            using (UpdateForm rechecked = new UpdateForm(delegate { shutdowns++; }, delegate { return Task.FromResult(Available()); },
                delegate { return Task.FromResult(package); }, delegate { installs++; }, discard))
            {
                Await(rechecked.CheckForUpdatesAsync()); Await(rechecked.DownloadUpdateAsync());
                Await(rechecked.CheckForUpdatesAsync());
                Require(discards == 1, "Rechecking forgot to discard the previous completed download.");
            }
            Require(discards == 1, "Disposal discarded a stage already released by rechecking.");

            discards = 0;
            using (UpdateForm transferred = new UpdateForm(delegate { shutdowns++; }, delegate { return Task.FromResult(Available()); },
                delegate { return Task.FromResult(package); }, delegate { installs++; }, discard))
            {
                Await(transferred.CheckForUpdatesAsync()); Await(transferred.DownloadUpdateAsync());
                transferred.InstallUpdate(); transferred.Dispose();
            }
            Require(installs == 1 && shutdowns == 1 && discards == 0, "An update transferred to the installer was discarded by the dialog.");
        }

        private static void LateDownloadCompletionIsDiscarded(bool disposeBeforeCompletion)
        {
            StagedUpdate package = new StagedUpdate { Version = "1.3.0" };
            TaskCompletionSource<StagedUpdate> pending = new TaskCompletionSource<StagedUpdate>();
            int discards = 0;
            using (UpdateForm form = new UpdateForm(delegate { throw new InvalidOperationException("Unexpected shutdown."); },
                delegate { return Task.FromResult(Available()); },
                delegate { return pending.Task; },
                delegate { throw new InvalidOperationException("Unexpected installation."); },
                delegate(StagedUpdate abandoned)
                {
                    Require(Object.ReferenceEquals(package, abandoned), "Late cleanup received another download's stage."); discards++;
                }))
            {
                Await(form.CheckForUpdatesAsync()); Task download = form.DownloadUpdateAsync();
                if (disposeBeforeCompletion) form.Dispose(); else Invoke(form, "CancelOrClose");
                Require(discards == 0, "An unfinished download was discarded before its stage was returned.");
                pending.SetResult(package); Await(download);
                Require(discards == 1, "A download finishing after cancellation/disposal was not discarded exactly once.");
                if (!disposeBeforeCompletion) Require(form.StatusName == "Available", "Canceled late completion became an installable update.");
            }
            Require(discards == 1, "Late completion cleanup ran again during disposal.");
        }

        private static T Field<T>(object value, string name)
        {
            return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }

        private static void Invoke(object value, string name)
        {
            value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
        }

        private static void Await(Task task)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            Require(task.IsCompleted, "Update callback did not finish within the test deadline.");
            task.GetAwaiter().GetResult();
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
