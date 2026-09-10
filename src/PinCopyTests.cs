using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ChachaCapture
{
    // Synthetic pixels and an in-memory clipboard writer; no desktop or system clipboard access.
    internal static class PinCopyTests
    {
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        internal static void Run()
        {
            using (Bitmap source = new Bitmap(83, 47))
            {
                using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.DarkSeaGreen);
                source.SetPixel(61, 31, Color.FromArgb(255, 13, 81, 197));
                using (PinForm pin = new PinForm(source))
                using (PinForm unrelated = new PinForm(source))
                {
                    int hidden = 0, unrelatedHidden = 0;
                    bool copied = false;
                    pin.HiddenByUser += delegate
                    {
                        Require(copied, "The floating image closed before the clipboard copy completed.");
                        hidden++;
                    };
                    unrelated.HiddenByUser += delegate { unrelatedHidden++; };
                    pin.ScaleFactor = 0.5;
                    pin.IsSelected = true;
                    unrelated.IsSelected = true;

                    bool failed = false;
                    try
                    {
                        pin.CopyImageAndClose(delegate(Bitmap pixels)
                        {
                            Require(!pin.ClosedByUser && hidden == 0, "Copy failure started with the source already closed.");
                            throw new ExternalException("Simulated busy clipboard.");
                        });
                    }
                    catch (ExternalException) { failed = true; }
                    Require(failed && !pin.ClosedByUser && !pin.IsDisposed && hidden == 0,
                        "A failed clipboard write closed or destroyed the floating image.");
                    using (Bitmap retained = pin.ExportImage())
                        Require(retained.Size == source.Size && retained.GetPixel(61, 31).ToArgb() == source.GetPixel(61, 31).ToArgb(),
                            "Copy failure changed the retained pixels.");

                    using (Bitmap saved = new Bitmap(source.Width, source.Height))
                    {
                        pin.CopyImageAndClose(delegate(Bitmap pixels)
                        {
                            Require(!pin.ClosedByUser && hidden == 0, "The source closed before a successful clipboard write.");
                            Require(pixels.Size == source.Size, "Copy exported the resized display instead of full-resolution pixels.");
                            using (Graphics graphics = Graphics.FromImage(saved)) graphics.DrawImageUnscaled(pixels, 0, 0);
                            copied = true;
                        });
                        Require(saved.GetPixel(61, 31).ToArgb() == source.GetPixel(61, 31).ToArgb(), "Copy changed the image pixels.");
                    }
                    Require(pin.ClosedByUser && !pin.Visible && !pin.IsDisposed && hidden == 1,
                        "Successful image copy did not close the source recoverably exactly once.");
                    Require(!unrelated.ClosedByUser && !unrelated.IsDisposed && unrelatedHidden == 0,
                        "Copying one selected floating image closed an unrelated selected image.");
                    using (Bitmap retained = pin.ExportImage())
                        Require(retained.GetPixel(61, 31).ToArgb() == source.GetPixel(61, 31).ToArgb(), "Recoverable close discarded the copied pixels.");
                    pin.HideByUser();
                    Require(hidden == 1, "An already closed image was added to recovery more than once.");
                }
            }
        }
    }
}
