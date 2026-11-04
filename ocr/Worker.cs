using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Tesseract;

namespace ChachaCapture
{
    // One request per process. No screenshots or recognized text are written to disk.
    internal static class OcrWorker
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (args.Length != 2) throw new ArgumentException("Invalid OCR options.");
                PageSegMode mode = args[0] == "line" ? PageSegMode.SingleLine :
                    args[0] == "block" ? PageSegMode.SingleBlock : PageSegMode.Auto;
                using (Stream input = Console.OpenStandardInput())
                using (MemoryStream bytes = new MemoryStream())
                {
                    byte[] buffer = new byte[32768]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (bytes.Length + count > 128L * 1024 * 1024) throw new InvalidDataException("Image is too large.");
                        bytes.Write(buffer, 0, count);
                    }
                    bytes.Position = 0;
                    using (Bitmap image = new Bitmap(bytes))
                    using (Bitmap prepared = OcrPreprocessing.Prepare(image, args[1] == "large"))
                    using (MemoryStream png = new MemoryStream())
                    using (TesseractEngine engine = new TesseractEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"), "kor+eng", EngineMode.LstmOnly))
                    {
                        prepared.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        engine.SetVariable("preserve_interword_spaces", "1");
                        using (Pix pixels = Pix.LoadFromMemory(png.ToArray()))
                        using (Page page = engine.Process(pixels, mode))
                            Console.Write(new JavaScriptSerializer().Serialize(new { text = page.GetText().Trim(), error = (string)null }));
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Write(new JavaScriptSerializer().Serialize(new { text = "", error = error.GetType().Name + ": " + error.Message }));
                return 1;
            }
        }
    }
}
