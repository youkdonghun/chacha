using System;
using System.Drawing;

namespace ChachaCapture
{
    internal static class DesktopCursorTests
    {
        internal static void Detection()
        {
            Rectangle leftMonitor = new Rectangle(-1920, -120, 1920, 1080);
            Point pointer = new Point(-400, 200);
            Require(!DesktopCapture.HasEmbeddedCursorMetadata(0, false, true, pointer, leftMonitor),
                "A fresh frame with no pointer timestamp must retain its DXGI pixels.");
            Require(!DesktopCapture.HasEmbeddedCursorMetadata(123, true, true, pointer, leftMonitor),
                "A separate hardware cursor must not be classified as embedded.");
            Require(!DesktopCapture.HasEmbeddedCursorMetadata(123, false, false, pointer, leftMonitor),
                "An invisible system cursor must not be classified as embedded.");
            Require(DesktopCapture.HasEmbeddedCursorMetadata(123, false, true, pointer, leftMonitor),
                "An embedded visible cursor on a negative-coordinate monitor must be reported without changing capture backends.");
            Require(!DesktopCapture.HasEmbeddedCursorMetadata(123, false, true, new Point(400, 200), leftMonitor),
                "A cursor on another monitor must not be classified as embedded on this output.");
            Require(DesktopCapture.HasEmbeddedCursorMetadata(123, false, true, leftMonitor.Location, leftMonitor),
                "The monitor's top-left pixel belongs to that monitor.");
            Require(!DesktopCapture.HasEmbeddedCursorMetadata(123, false, true, new Point(leftMonitor.Right, 200), leftMonitor),
                "The right monitor boundary belongs to the adjacent monitor.");
            Require(!DesktopCapture.HasEmbeddedCursorMetadata(123, false, true, new Point(-400, leftMonitor.Bottom), leftMonitor),
                "The bottom monitor boundary belongs to the adjacent monitor.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
