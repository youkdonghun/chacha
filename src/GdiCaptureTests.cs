using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ChachaCapture
{
    internal static class GdiCaptureTests
    {
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        internal static void Run()
        {
            // A memory DC with an offset logical origin exercises negative desktop coordinates
            // without reading a user's screen or changing the user's display arrangement.
            using (Bitmap pixels = new Bitmap(3840, 1080, PixelFormat.Format32bppArgb))
            {
                using (Graphics paint = Graphics.FromImage(pixels))
                {
                    paint.Clear(Color.CornflowerBlue);
                    paint.FillRectangle(Brushes.OrangeRed, 0, 0, 1920, 1080);
                    paint.FillRectangle(Brushes.LimeGreen, 0, 0, 9, 9);
                    paint.FillRectangle(Brushes.Yellow, 3831, 1071, 9, 9);
                }
                IntPtr sourceBitmap = pixels.GetHbitmap(Color.Black);
                IntPtr source = CreateCompatibleDC(IntPtr.Zero);
                IntPtr originalBitmap = SelectObject(source, sourceBitmap);
                try
                {
                    NativePoint oldOrigin;
                    Require(SetWindowOrgEx(source, -1920, 0, out oldOrigin), "Could not prepare a negative-origin GDI fixture.");
                    try
                    {
                        Rectangle left = new Rectangle(-1920, 0, 1920, 1080), right = new Rectangle(0, 0, 1920, 1080);
                        foreach (Rectangle region in new Rectangle[] { left, right, new Rectangle(-1920, 0, 3840, 1080) })
                        {
                            bool basic;
                            using (Bitmap capture = DesktopCapture.CaptureGdiSource(source, region, new DesktopCaptureReport(), out basic, null))
                            {
                                Require(capture.Size == region.Size, "GDI capture changed the requested dimensions.");
                                Require(capture.GetPixel(20, 20).ToArgb() == (region.X < 0 ? Color.OrangeRed : Color.CornflowerBlue).ToArgb(), "Negative desktop origin selected the wrong pixels: " + region + " => " + capture.GetPixel(20, 20));
                                Require(capture.GetPixel(20, 20).A == 255, "GDI left captured pixels transparent.");
                                if (region.Width == 3840)
                                {
                                    Require(capture.GetPixel(2, 2).ToArgb() == Color.LimeGreen.ToArgb(), "GDI flipped or shifted the top-left corner.");
                                    Require(capture.GetPixel(3838, 1077).ToArgb() == Color.Yellow.ToArgb(), "GDI flipped or shifted the bottom-right corner.");
                                }
                            }
                        }
                        foreach (int error in new int[] { 0, 50, 87, 288 })
                        {
                            int calls = 0; bool basic; DesktopCaptureReport report = new DesktopCaptureReport();
                            using (Bitmap capture = DesktopCapture.CaptureGdiSource(source, left, report, out basic, delegate(uint operation)
                            {
                                calls++;
                                Require(operation == (calls == 1 ? 0x40CC0020u : 0x00CC0020u), "GDI fallback changed the wrong raster flags.");
                                return calls == 1 ? (int?)error : null;
                            }))
                            {
                                Require(calls == 2 && basic && capture.GetPixel(20, 20).ToArgb() == Color.OrangeRed.ToArgb(), "Bounded basic GDI retry lost the selected pixels.");
                                Require(String.Join("\n", report.Events.ToArray()).Contains("Win32=" + error), "GDI retry lost the original driver error.");
                            }
                        }
                        int before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                        for (int i = 0; i < 20; i++)
                        {
                            bool basic; int calls = 0;
                            try
                            {
                                using (Bitmap unexpected = DesktopCapture.CaptureGdiSource(source, left, new DesktopCaptureReport(), out basic, delegate { calls++; return (int?)5; })) { }
                                throw new InvalidOperationException("Access-denied capture unexpectedly succeeded.");
                            }
                            catch (DesktopCapture.GdiCaptureException error)
                            {
                                Require(calls == 1 && DesktopCapture.IsAccessDenied(error), "An explicit access denial triggered a GDI retry.");
                                Require(error.Operation == "BitBlt(CAPTUREBLT)" && error.Area == left, "Native failure lost its exact stage or negative bounds.");
                            }
                        }
                        int after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                        Require(after <= before + 1, "Failed captures leaked GDI handles.");
                        try
                        {
                            bool basic;
                            using (Bitmap unexpected = DesktopCapture.CaptureGdiSource(source, right, new DesktopCaptureReport(), out basic, delegate { return (int?)288; })) { }
                            throw new InvalidOperationException("Repeated GDI failure was accepted.");
                        }
                        catch (DesktopCapture.GdiCaptureException error)
                        {
                            string description = DesktopCapture.DescribeFailure(error);
                            Require(error.Operation == "BitBlt(SRCCOPY)" && description.Contains("288") && description.Contains("단계="), "Failed retry hid the final stage/code.");
                        }
                    }
                    finally { NativePoint ignored; SetWindowOrgEx(source, oldOrigin.X, oldOrigin.Y, out ignored); }
                }
                finally { SelectObject(source, originalBitmap); DeleteObject(sourceBitmap); DeleteDC(source); }
            }
            Require(!DesktopCapture.CanRetryBasicGdi(5) && !DesktopCapture.CanRetryBasicGdi(8), "Retry should not cover access denial or insufficient memory.");
            string missing = DesktopCapture.DescribeFailure(new DesktopCapture.GdiCaptureException(0, "GetDC", new Rectangle(-1920, 0, 1920, 1080)));
            Require(missing.Contains("미제공") && missing.Contains("GetDC") && missing.Contains("-1920"), "A missing Windows error was misreported as success or an unrelated stale error.");
            Require(DesktopCapture.DescribeNotice(new DesktopCaptureReport { BasicGdiCopy = true }).Contains("반투명"), "Basic GDI copy must disclose layered-window limitations.");
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool SetWindowOrgEx(IntPtr dc, int x, int y, out NativePoint previous);
        [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
