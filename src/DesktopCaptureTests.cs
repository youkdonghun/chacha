using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ChachaCapture
{
    internal static class DesktopCaptureTests
    {
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        internal static void BackendSelectionAndDiagnostics()
        {
            DesktopCaptureMode[] automatic = DesktopCapture.BackendOrder(DesktopCaptureMode.Automatic);
            Require(automatic.Length == 2 && automatic[0] == DesktopCaptureMode.Compatibility && automatic[1] == DesktopCaptureMode.Graphics,
                "Automatic capture must use the standard still-image API first and retain an API-error fallback.");
            foreach (DesktopCaptureMode explicitMode in new[] { DesktopCaptureMode.Compatibility, DesktopCaptureMode.Graphics })
            {
                DesktopCaptureMode[] order = DesktopCapture.BackendOrder(explicitMode);
                Require(order.Length == 2 && order[0] == explicitMode && order[1] != explicitMode, "A saved preference must retain a bounded error recovery path.");
            }
            Require(DesktopCapture.IsAccessDenied(new Win32Exception(5)), "Native access denial must prevent an automatic backend fallback.");
            Require(DesktopCapture.IsAccessDenied(new COMException("denied", unchecked((int)0x80070005))), "HRESULT access denial must be recognized.");
            Require(!DesktopCapture.IsAccessDenied(new COMException("busy", unchecked((int)0x887A0022))), "A busy duplication slot is an API resource error, not an access denial.");
            string busy = DesktopCapture.DescribeFailure(new COMException("busy", unchecked((int)0x887A0022)));
            Require(busy.Contains("다른 캡처 앱") && busy.Contains("887A0022"), "Concurrent capture failure must retain its actionable reason and error code.");
            using (Bitmap image = new Bitmap(257, 139))
            {
                Rectangle region = new Rectangle(Point.Empty, image.Size);
                using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
                Require(DesktopCapture.IsUniformRegion(image, region), "A solid white driver frame must produce a diagnostic.");
                image.SetPixel(256, 138, Color.Black);
                Require(!DesktopCapture.IsUniformRegion(image, region), "A single real pixel of content must stop a uniform-frame warning.");
                Require(DesktopCapture.IsUniformRegion(image, new Rectangle(0, 0, 200, 100)), "Region checks must ignore other display pixels.");
                using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.FromArgb(40, 60, 80));
                Require(DesktopCapture.IsUniformRegion(image, region), "Legitimate solid-color desktops may be diagnosed but must remain capturable.");
            }
            DesktopCaptureReport report = new DesktopCaptureReport { UniformFrame = true, ProtectedContentMasked = true };
            Require(DesktopCapture.DescribeNotice(report).Contains("보호된 콘텐츠"), "Protected-content notice must take priority over a uniform image heuristic.");
            report.ProtectedContentMasked = false;
            Require(DesktopCapture.DescribeNotice(report).Contains("미리보기"), "A solid image must request visual confirmation, never claim it is blocked.");
            report.UniformFrame = false; report.EmbeddedCursor = true;
            Require(DesktopCapture.DescribeNotice(report).Contains("마우스 포인터"), "A driver-baked cursor must be explained without changing backends.");
        }

        internal static void Recovery()
        {
            Rectangle bounds = new Rectangle(-1920, 0, 3840, 1080);
            foreach (DesktopCaptureMode mode in new[] { DesktopCaptureMode.Automatic, DesktopCaptureMode.Compatibility, DesktopCaptureMode.Graphics })
            {
                int calls = 0;
                DesktopCaptureReport report = new DesktopCaptureReport();
                using (Bitmap result = DesktopCapture.CaptureBackends(mode, true, report, delegate(DesktopCaptureMode backend, DesktopCaptureReport attempt)
                {
                    calls++;
                    if (calls == 1)
                    {
                        attempt.EmbeddedCursor = true; attempt.ProtectedContentMasked = true;
                        attempt.Events.Add("Partial failed frame metadata");
                        throw new DesktopCapture.GdiCaptureException(288, "BitBlt(CAPTUREBLT)", bounds);
                    }
                    Bitmap frame = new Bitmap(2, 2); frame.SetPixel(1, 1, Color.CornflowerBlue); return frame;
                }))
                {
                    Require(calls == 2 && report.Recovered, "Saved capture preference prevented recovery after error 288.");
                    Require(result.GetPixel(1, 1).ToArgb() == Color.CornflowerBlue.ToArgb(), "Recovery replaced the successful frame.");
                    Require(!report.EmbeddedCursor && !report.ProtectedContentMasked, "Metadata from a failed partial frame contaminated recovery.");
                    Require(report.Backend.StartsWith(mode == DesktopCaptureMode.Graphics ? "GDI" : "DXGI"), "Recovered pixels were attributed to the wrong backend.");
                    Require(String.Join("\n", report.Events.ToArray()).Contains("288") && DesktopCapture.DescribeNotice(report).Contains("오류가 있어"), "Recovery hid the original failure or changed backend.");
                }
            }
            // A legitimate solid image is still a successful image, including a gray desktop.
            foreach (Color color in new[] { Color.Gray, Color.White, Color.Black })
            {
                int calls = 0; DesktopCaptureReport report = new DesktopCaptureReport();
                using (Bitmap result = DesktopCapture.CaptureBackends(DesktopCaptureMode.Compatibility, true, report, delegate
                {
                    calls++; Bitmap frame = new Bitmap(3, 3);
                    using (Graphics paint = Graphics.FromImage(frame)) paint.Clear(color);
                    return frame;
                }))
                    Require(calls == 1 && !report.Recovered && result.GetPixel(1, 1).ToArgb() == color.ToArgb(), "A valid solid frame triggered recovery.");
            }
            foreach (bool accessDenied in new[] { false, true })
            {
                int calls = 0; DesktopCaptureReport report = new DesktopCaptureReport();
                try
                {
                    using (Bitmap unexpected = DesktopCapture.CaptureBackends(DesktopCaptureMode.Compatibility, true, report, delegate
                    {
                        calls++;
                        if (calls == 1) throw new DesktopCapture.GdiCaptureException(accessDenied ? 5 : 288, "BitBlt(CAPTUREBLT)", bounds);
                        throw new COMException("GPU busy", unchecked((int)0x887A0022));
                    })) { }
                    throw new Exception("Failed backends produced a successful capture.");
                }
                catch (InvalidOperationException failure)
                {
                    Require(calls == (accessDenied ? 1 : 2) && report.Backend == null, "Failed capture retried too often or reported success.");
                    Require(accessDenied ? failure.Message.Contains("접근") : failure.Message.Contains("288") && failure.Message.Contains("887A0022"), "Combined failure lost a backend error.");
                }
            }
            int strictCalls = 0;
            try
            {
                using (Bitmap unexpected = DesktopCapture.CaptureBackends(DesktopCaptureMode.Graphics, false, new DesktopCaptureReport(), delegate
                { strictCalls++; throw new COMException("GPU busy", unchecked((int)0x887A0022)); })) { }
            }
            catch (InvalidOperationException) { }
            Require(strictCalls == 1, "Explicit diagnostic API must not conceal an unavailable GPU backend.");
        }
    }
}
