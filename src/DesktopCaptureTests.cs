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
                Require(order.Length == 1 && order[0] == explicitMode, "An explicit backend must not silently switch API.");
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
    }
}
