using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ChachaCapture
{
    internal static class DxgiFrameTests
    {
        private const int Timeout = unchecked((int)0x887A0027);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        internal static void Run()
        {
            Require(Marshal.SizeOf(typeof(DesktopCapture.FrameInfo)) == 48 &&
                Marshal.OffsetOf(typeof(DesktopCapture.FrameInfo), "PointerVisible").ToInt32() == 36,
                "DXGI frame metadata no longer matches the native x64 layout.");
            // Each monitor starts without a desktop image, including a monitor left of the primary.
            foreach (int x in new[] { 0, -1920 })
            {
                Rectangle bounds = new Rectangle(x, 0, 1920, 1080);
                long elapsed = 0; int calls = 0, releasedFrames = 0;
                List<IntPtr> released = new List<IntPtr>();
                DesktopCaptureReport report = new DesktopCaptureReport();
                IntPtr resource;
                DesktopCapture.FrameInfo result = DesktopCapture.AcquireDesktopFrame(new IntPtr(99),
                    delegate(IntPtr self, uint wait, out DesktopCapture.FrameInfo info, out IntPtr image)
                    {
                        Require(wait > 0 && wait <= 100, "Frame acquisition must use bounded waits.");
                        calls++; elapsed += 100; info = new DesktopCapture.FrameInfo(); image = IntPtr.Zero;
                        if (calls > 1) Require(releasedFrames == 1 && released.Count == 1, "A skipped frame was held across another AcquireNextFrame.");
                        if (calls == 2) return Timeout;
                        image = new IntPtr(calls);
                        if (calls == 1) { info.LastMouseUpdateTime = 12; info.PointerVisible = 1; info.PointerX = 24; info.ProtectedContentMaskedOut = 1; }
                        else { info.LastPresentTime = 45; info.AccumulatedFrames = 1; }
                        return 0;
                    }, delegate { releasedFrames++; return 0; }, released.Add, 1000, bounds, report, out resource, delegate { return elapsed; });
                Require(calls == 3 && resource == new IntPtr(3) && result.LastPresentTime == 45, "Pointer-only data was returned as the initial desktop.");
                Require(result.ProtectedContentMaskedOut == 0, "Rejected frame protection flags contaminated the accepted desktop.");
                Require(result.LastMouseUpdateTime == 12 && result.PointerVisible == 1 && result.PointerX == 24, "Pointer metadata from the same session was lost.");
                Require(releasedFrames == 1 && !released.Contains(resource), "The accepted resource was released before its pixels could be read.");
                Require(String.Join("\n", report.Events.ToArray()).Contains("skipped=1; timeouts=1") && report.Events[0].Contains(bounds.ToString()), "First-frame diagnostics lost the output or skipped updates.");
                // The native caller releases these after copying and unmapping the accepted frame.
                released.Add(resource); releasedFrames++;
                Require(released.Count == 2 && releasedFrames == 2, "Frame ownership did not balance.");
            }
            foreach (bool pointerOnly in new[] { false, true })
            {
                long elapsed = 0; int calls = 0, frames = 0, resources = 0;
                DesktopCaptureReport report = new DesktopCaptureReport(); IntPtr resource = IntPtr.Zero;
                try
                {
                    DesktopCapture.AcquireDesktopFrame(IntPtr.Zero,
                        delegate(IntPtr self, uint wait, out DesktopCapture.FrameInfo info, out IntPtr image)
                        { calls++; elapsed += wait; info = new DesktopCapture.FrameInfo(); image = pointerOnly ? new IntPtr(calls) : IntPtr.Zero; return pointerOnly ? 0 : Timeout; },
                        delegate { frames++; return 0; }, delegate { resources++; }, 450, Rectangle.Empty, report, out resource, delegate { return elapsed; });
                    throw new Exception("A session without a desktop update succeeded.");
                }
                catch (COMException error) { Require(error.HResult == Timeout, "The no-desktop failure lost its timeout code."); }
                Require(calls == 5 && elapsed == 450 && resource == IntPtr.Zero, "Initial frame waiting exceeded its budget or returned a resource.");
                Require(frames == (pointerOnly ? 5 : 0) && resources == frames, "Timed-out or pointer-only resources leaked or were released twice.");
                Require(report.Events[0].Contains("no desktop update"), "An initial-frame timeout has no diagnostic.");
            }
            foreach (int failure in new[] { unchecked((int)0x887A0026), unchecked((int)0x80070005) })
            {
                int calls = 0, releases = 0; IntPtr resource;
                try
                {
                    DesktopCapture.AcquireDesktopFrame(IntPtr.Zero,
                        delegate(IntPtr self, uint wait, out DesktopCapture.FrameInfo info, out IntPtr image)
                        { calls++; info = new DesktopCapture.FrameInfo(); image = IntPtr.Zero; return failure; },
                        delegate { releases++; return 0; }, delegate { releases++; }, 1000, Rectangle.Empty, new DesktopCaptureReport(), out resource, delegate { return 0; });
                    throw new Exception("A native capture failure was ignored.");
                }
                catch (Exception error) { Require(error.HResult == failure, "Native error was replaced during frame acquisition."); }
                Require(calls == 1 && releases == 0, "Access denial or lost duplication was retried or released an unowned frame.");
            }
            {
                int releases = 0; IntPtr resource;
                try
                {
                    DesktopCapture.AcquireDesktopFrame(IntPtr.Zero,
                        delegate(IntPtr self, uint wait, out DesktopCapture.FrameInfo info, out IntPtr image)
                        { info = new DesktopCapture.FrameInfo { LastPresentTime = 1 }; image = IntPtr.Zero; return 0; },
                        delegate { releases++; return 0; }, delegate { throw new Exception("Null resource released."); }, 1000, Rectangle.Empty, new DesktopCaptureReport(), out resource, delegate { return 0; });
                    throw new Exception("A missing surface was accepted.");
                }
                catch (InvalidOperationException) { }
                Require(releases == 1, "The held frame was leaked after a missing surface.");
            }
        }
    }
}
