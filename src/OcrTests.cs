using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal static class OcrTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string Compact(string value) { return Regex.Replace(value, @"\s", ""); }
        private static readonly string[] Lines = {
            "한글 캡처 테스트입니다. English OCR 12345",
            "질문? 확인! 할인 25% 이메일 test@example.com 시간 10:30",
            "Symbols: ? ! % @ :",
            "추가 기호: (A) [B] {C} + - = / _ # & * ; , ." };

        internal static Bitmap Sample(int size, bool dark)
        {
            Bitmap image = new Bitmap(1600, size * 8 + 40);
            using (Graphics g = Graphics.FromImage(image))
            using (Font font = new Font("Malgun Gothic", size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush ink = new SolidBrush(dark ? Color.White : Color.Black))
            {
                g.Clear(dark ? Color.FromArgb(25, 28, 34) : Color.White);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < Lines.Length; i++) g.DrawString(Lines[i], font, ink, 20, 16 + i * size * 2);
            }
            return image;
        }

        internal static void Recognition(string root, string report)
        {
            OcrService.CacheRootForTest = root;
            List<object> results = new List<object>(); List<int> processes = new List<int>();
            int currentPid = 0;
            OcrService.WorkerStartedForTest = delegate(int pid) { processes.Add(pid); Interlocked.Exchange(ref currentPid, pid); };
            try
            {
                int phraseMatches = 0, symbolMatches = 0, fullMatches = 0;
                foreach (int size in new int[] { 14, 20, 28 }) foreach (bool dark in new bool[] { false, true })
                {
                    using (Bitmap source = Sample(size, dark))
                    {
                        Stopwatch watch = Stopwatch.StartNew();
                        Task<string> task = OcrService.RecognizeAsync(source, "block", true, CancellationToken.None);
                        long peakPrivateBytes = 0;
                        while (!task.IsCompleted)
                        {
                            int pid = Volatile.Read(ref currentPid);
                            if (pid != 0) try { using (Process worker = Process.GetProcessById(pid)) { worker.Refresh(); peakPrivateBytes = Math.Max(peakPrivateBytes, worker.PrivateMemorySize64); } } catch (ArgumentException) { } catch (InvalidOperationException) { }
                            Thread.Sleep(10);
                        }
                        string actual = task.GetAwaiter().GetResult();
                        bool phrase = Compact(actual).Contains(Compact(Lines[1]));
                        bool symbols = Compact(actual).Contains(Compact(Lines[2]));
                        bool full = Compact(actual) == Compact(String.Join("\n", Lines));
                        if (phrase) phraseMatches++; if (symbols) symbolMatches++; if (full) fullMatches++;
                        results.Add(new { size, dark, phrase, symbols, full, milliseconds = watch.ElapsedMilliseconds, peakPrivateBytes, workerExited = !IsRunning(currentPid), text = actual });
                    }
                }
                File.WriteAllText(report, new JavaScriptSerializer().Serialize(new { cases = results, phraseMatches, symbolMatches, fullMatches, total = results.Count }));
                Assert(phraseMatches == 6, "Mixed Korean/English/numbers/punctuation regression; see " + report);
                Assert(symbolMatches == 6, "Required punctuation regression; see " + report);
                List<object> isolatedSymbols = new List<object>();
                foreach (string family in new string[] { "Malgun Gothic", "Segoe UI" })
                using (Bitmap symbols = new Bitmap(600, 100))
                {
                    using (Graphics g = Graphics.FromImage(symbols))
                    using (Font font = new Font(family, 24, FontStyle.Regular, GraphicsUnit.Pixel))
                    { g.Clear(Color.White); g.DrawString("? ! % @ :", font, Brushes.Black, 20, 20); }
                    string actual = OcrService.RecognizeAsync(symbols, "line", true, CancellationToken.None).GetAwaiter().GetResult();
                    // Observe difficult inputs without turning imperfect OCR into a promise of
                    // universal accuracy. The supported mixed-text fixtures above remain gates.
                    isolatedSymbols.Add(new { family, expected = "? ! % @ :", actual, exact = Compact(actual) == "?!%@:" });
                }
                File.WriteAllText(report, new JavaScriptSerializer().Serialize(new { cases = results, phraseMatches, symbolMatches, fullMatches, total = results.Count, isolatedSymbols }));
                using (Bitmap blank = new Bitmap(240, 90))
                {
                    using (Graphics g = Graphics.FromImage(blank)) g.Clear(Color.White);
                    Assert(String.IsNullOrWhiteSpace(OcrService.RecognizeAsync(blank, "auto", true, CancellationToken.None).GetAwaiter().GetResult()), "Blank image invented text.");
                }
                string runtime = OcrService.EnsureRuntime(CancellationToken.None);
                string managed = Path.Combine(runtime, "Tesseract.dll");
                byte[] original = File.ReadAllBytes(managed); File.WriteAllText(managed, "damaged");
                OcrService.EnsureRuntime(CancellationToken.None);
                Assert(original.SequenceEqual(File.ReadAllBytes(managed)), "Damaged OCR cache was not repaired.");
            }
            finally
            {
                OcrService.WorkerStartedForTest = null; OcrService.CacheRootForTest = null;
                foreach (int pid in processes) Assert(!IsRunning(pid), "OCR worker remains after completion: " + pid);
            }
        }

        internal static void Cancellation(string root)
        {
            OcrService.CacheRootForTest = root;
            int pid = 0;
            try
            {
                using (Bitmap image = Sample(20, false))
                using (CancellationTokenSource cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    try { OcrService.RecognizeAsync(image, "auto", true, cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Pre-cancelled request ran."); }
                    catch (OperationCanceledException) { }
                    using (CancellationTokenSource active = new CancellationTokenSource())
                    {
                        OcrService.WorkerStartedForTest = delegate(int id) { pid = id; active.Cancel(); };
                        try { OcrService.RecognizeAsync(image, "auto", true, active.Token).GetAwaiter().GetResult(); throw new Exception("Active cancel ignored."); }
                        catch (OperationCanceledException) { }
                        Assert(pid != 0 && !IsRunning(pid), "Cancel left an OCR worker alive.");
                    }
                    OcrService.WorkerStartedForTest = delegate(int id) { pid = id; };
                    OcrService.TimeoutMilliseconds = 1;
                    try { OcrService.RecognizeAsync(image, "auto", true, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Timeout ignored."); }
                    catch (TimeoutException) { }
                    Assert(!IsRunning(pid), "Timeout left an OCR worker alive.");
                }
            }
            finally { OcrService.TimeoutMilliseconds = 90000; OcrService.WorkerStartedForTest = null; OcrService.CacheRootForTest = null; }
        }

        internal static void Interface(string previewPath)
        {
            using (Bitmap source = Sample(20, false))
            using (OcrForm form = new OcrForm(source))
            {
                form.Recognizer = delegate { return Task.FromResult(String.Join(Environment.NewLine, Lines)); };
                CreateHandles(form);
                form.ReadAsync().GetAwaiter().GetResult();
                TextBox text = Field<TextBox>(form, "result"); Button copy = Field<Button>(form, "copy");
                Assert(!text.ReadOnly && copy.Enabled && text.Text.Contains("test@example.com"), "OCR results cannot be edited/copied.");
                Assert(Field<ComboBox>(form, "mode").Text == "한 문단", "The default OCR reading mode has no label.");
                text.Text = "수정한 결과!";
                form.Recognizer = delegate { TaskCompletionSource<string> error = new TaskCompletionSource<string>(); error.SetException(new IOException("fixture failure")); return error.Task; };
                form.ReadAsync().GetAwaiter().GetResult();
                Assert(text.Text == "수정한 결과!" && copy.Enabled, "Retry failure discarded the edited result.");
                form.Recognizer = delegate { return Task.FromResult(String.Join(Environment.NewLine, Lines)); };
                form.ReadAsync().GetAwaiter().GetResult();
                if (previewPath != null)
                {
                    using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, form.Size)); preview.Save(previewPath); }
                }
            }
            using (Bitmap source = Sample(20, false))
            using (CaptureOverlay overlay = new CaptureOverlay(source, new Rectangle(0, 0, source.Width, source.Height), Rectangle.Empty, null, false, false))
            {
                overlay.SetSelection(new Rectangle(10, 15, 400, 150));
                int count = 0; overlay.Completed += delegate(CaptureResult capture) { using (capture) { count++; Assert(capture.Outcome == CaptureOutcome.Ocr && capture.Image.Size == new Size(400, 150), "OCR did not use exactly the selected pixels."); } };
                MethodInfo key = typeof(CaptureOverlay).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
                key.Invoke(overlay, new object[] { new Message(), Keys.Control | Keys.Shift | Keys.O });
                Assert(count == 1, "OCR shortcut did not complete capture.");
            }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
        private static void CreateHandles(Control control) { IntPtr handle = control.Handle; foreach (Control child in control.Controls) CreateHandles(child); control.PerformLayout(); }
        private static bool IsRunning(int pid) { try { using (Process process = Process.GetProcessById(pid)) return !process.HasExited; } catch (ArgumentException) { return false; } }
    }
}
