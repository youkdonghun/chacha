using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ChachaCapture
{
    /// <summary>Owns encoded GIF bytes and their decoder for the complete animation lifetime.</summary>
    public sealed class AnimatedImageSource : IDisposable
    {
        private byte[] encoded;
        private MemoryStream stream;
        private Image decoder;
        private int[] delays;
        public int FrameCount { get; private set; }
        public int Width { get { return decoder.Width; } }
        public int Height { get { return decoder.Height; } }

        public AnimatedImageSource(byte[] data)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("GIF 데이터가 비어 있습니다.", "data");
            encoded = (byte[])data.Clone();
            stream = new MemoryStream(encoded, false);
            try
            {
                decoder = Image.FromStream(stream, true, true);
                if (decoder.RawFormat.Guid != ImageFormat.Gif.Guid)
                    throw new ArgumentException("GIF 이미지 파일을 선택하세요.", "data");
                FrameCount = decoder.GetFrameCount(FrameDimension.Time);
                delays = new int[FrameCount];
                for (int i = 0; i < delays.Length; i++) delays[i] = 100;
                try
                {
                    byte[] raw = decoder.GetPropertyItem(0x5100).Value;
                    for (int i = 0; i < FrameCount && i * 4 + 3 < raw.Length; i++)
                    {
                        long hundredths = BitConverter.ToUInt32(raw, i * 4);
                        delays[i] = hundredths <= 1 ? 100 : (int)Math.Min(600000, hundredths * 10);
                    }
                }
                catch (ArgumentException) { }
            }
            catch { Dispose(); throw; }
        }

        public int DelayForFrame(int index)
        {
            if (decoder == null) throw new ObjectDisposedException("AnimatedImageSource");
            if (index < 0 || index >= FrameCount) throw new ArgumentOutOfRangeException("index");
            return delays[index];
        }

        public Bitmap GetFrame(int index)
        {
            if (decoder == null) throw new ObjectDisposedException("AnimatedImageSource");
            if (index < 0 || index >= FrameCount) throw new ArgumentOutOfRangeException("index");
            decoder.SelectActiveFrame(FrameDimension.Time, index);
            Bitmap frame = new Bitmap(decoder.Width, decoder.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(decoder, new Rectangle(0, 0, frame.Width, frame.Height), 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel);
            }
            return frame;
        }

        public byte[] ExportBytes()
        {
            if (encoded == null) throw new ObjectDisposedException("AnimatedImageSource");
            return (byte[])encoded.Clone();
        }

        public void Dispose()
        {
            if (decoder != null) { decoder.Dispose(); decoder = null; }
            if (stream != null) { stream.Dispose(); stream = null; }
            encoded = null;
        }
    }
}
