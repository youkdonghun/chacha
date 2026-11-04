using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ChachaCapture
{
    internal static class OcrPreprocessing
    {
        internal const long MaximumPixels = 24000000;

        internal static Bitmap Prepare(Bitmap source, bool enlarge)
        {
            if ((long)source.Width * source.Height > MaximumPixels)
                throw new ArgumentException("읽을 영역이 너무 큽니다. 텍스트 부분을 좁혀서 캡처해 주세요.");
            // Bound enlarged output, including padding, instead of silently shrinking large text.
            double scale = enlarge ? Math.Min(2, Math.Sqrt(16000000.0 / ((long)source.Width * source.Height))) : 1;
            scale = Math.Max(1, scale);
            int width = (int)Math.Round(source.Width * scale), height = (int)Math.Round(source.Height * scale);
            Bitmap result = new Bitmap(width + 32, height + 32, PixelFormat.Format24bppRgb);
            try
            {
                // Most screen snippets have a uniform background. Sample composited luminance so
                // transparent black pixels do not make a transparent image look like dark mode.
                long light = 0; int samples = 0, minimum = 255, maximum = 0;
                for (int y = 0; y < source.Height; y += Math.Max(1, source.Height / 40))
                    for (int x = 0; x < source.Width; x += Math.Max(1, source.Width / 60))
                    {
                        Color p = source.GetPixel(x, y);
                        int luminance = ((p.R * 299 + p.G * 587 + p.B * 114) / 1000 * p.A + 255 * (255 - p.A)) / 255;
                        light += luminance; minimum = Math.Min(minimum, luminance); maximum = Math.Max(maximum, luminance);
                        samples++;
                    }
                bool invert = light / Math.Max(1, samples) < 110;
                using (Bitmap flat = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
                using (Graphics flatten = Graphics.FromImage(flat))
                using (Graphics graphics = Graphics.FromImage(result))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    flatten.Clear(Color.White); flatten.DrawImageUnscaled(source, 0, 0);
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    if (maximum - minimum < 64) { minimum = 0; maximum = 255; }
                    float range = maximum - minimum;
                    float sign = (invert ? -255f : 255f) / range, offset = (invert ? maximum : -minimum) / range;
                    attributes.SetColorMatrix(new ColorMatrix(new float[][] {
                        new float[] {.299f*sign,.299f*sign,.299f*sign,0,0},
                        new float[] {.587f*sign,.587f*sign,.587f*sign,0,0},
                        new float[] {.114f*sign,.114f*sign,.114f*sign,0,0},
                        new float[] {0,0,0,1,0}, new float[] {offset,offset,offset,0,1} }));
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(flat, new Rectangle(16, 16, width, height), 0, 0, flat.Width, flat.Height, GraphicsUnit.Pixel, attributes);
                }
                result.SetResolution(300, 300);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
    }
}
