using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ChachaCapture
{
    public sealed class ClipboardPayload : IDisposable
    {
        public Bitmap Image;
        public string SourceText;
        public byte[] Animation;
        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }

    public static class ClipboardContent
    {
        public static List<ClipboardPayload> Read(IDataObject data, bool preferHtml, bool pastePaths)
        {
            List<ClipboardPayload> result = new List<ClipboardPayload>();
            if (data == null) return result;
            try
            {
                string[] files = data.GetData(DataFormats.FileDrop) as string[];
                if (files != null)
                {
                    foreach (string file in files)
                    {
                        if (result.Count == 30) break;
                        try { result.Add(FromFile(file)); }
                        catch (Exception e)
                        {
                            if (!(e is IOException || e is ArgumentException || e is OutOfMemoryException || e is NotSupportedException)) throw;
                            if (pastePaths) result.Add(FromText(file, false));
                        }
                    }
                    return result;
                }
                // Browsers expose PNG separately, preserving alpha better than the legacy DIB representation.
                object png = data.GetData("PNG");
                byte[] pngBytes = png as byte[];
                Stream pngStream = png as Stream;
                if (pngBytes != null || pngStream != null)
                {
                    using (MemoryStream memory = new MemoryStream())
                    {
                        if (pngBytes != null) memory.Write(pngBytes, 0, pngBytes.Length);
                        else { if (pngStream.CanSeek) pngStream.Position = 0; pngStream.CopyTo(memory); }
                        memory.Position = 0;
                        using (Image image = Image.FromStream(memory)) result.Add(new ClipboardPayload { Image = new Bitmap(image) });
                    }
                    return result;
                }
                Image bitmap = data.GetData(DataFormats.Bitmap) as Image;
                if (bitmap != null) { result.Add(new ClipboardPayload { Image = new Bitmap(bitmap) }); return result; }
                string plain = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
                string html = data.GetData(DataFormats.Html) as string;
                string rtf = data.GetData(DataFormats.Rtf) as string;
                if (preferHtml && !String.IsNullOrWhiteSpace(rtf))
                {
                    using (RichTextBox box = NewRichText())
                    {
                        try { box.Rtf = rtf; result.Add(new ClipboardPayload { Image = RenderRichText(box), SourceText = plain ?? box.Text }); return result; }
                        catch (ArgumentException) { }
                    }
                }
                if (preferHtml && !String.IsNullOrWhiteSpace(html)) { ClipboardPayload item = FromText(html, true); if (plain != null) item.SourceText = plain; result.Add(item); }
                else if (!String.IsNullOrWhiteSpace(plain)) result.Add(FromText(plain, false));
                return result;
            }
            catch { foreach (ClipboardPayload item in result) item.Dispose(); throw; }
        }
        public static ClipboardPayload FromFile(string path)
        {
            Bitmap image = Storage.LoadBitmap(path);
            ClipboardPayload item = new ClipboardPayload { Image = image };
            if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                try { FileInfo info = new FileInfo(path); if (info.Length <= 32 * 1024 * 1024) item.Animation = File.ReadAllBytes(path); }
                catch { item.Dispose(); throw; }
            }
            return item;
        }
        public static ClipboardPayload FromText(string text, bool html)
        {
            if (!html) return new ClipboardPayload { Image = ClipboardImages.RenderText(text), SourceText = text };
            using (RichTextBox box = NewRichText())
            {
                FillHtml(box, ExtractFragment(text));
                return new ClipboardPayload { Image = RenderRichText(box), SourceText = box.Text };
            }
        }
        public static string ExtractFragment(string html)
        {
            if (html == null) return "";
            int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase), end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start) return html.Substring(start + 20, end - start - 20);
            Match a = Regex.Match(html, @"StartFragment:(\d+)"), b = Regex.Match(html, @"EndFragment:(\d+)");
            int left, right;
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            if (a.Success && b.Success && Int32.TryParse(a.Groups[1].Value, out left) && Int32.TryParse(b.Groups[1].Value, out right) && left >= 0 && right > left && right <= bytes.Length) return Encoding.UTF8.GetString(bytes, left, right - left);
            int tag = html.IndexOf('<'); return tag >= 0 ? html.Substring(tag) : html;
        }
        private static RichTextBox NewRichText()
        {
            return new RichTextBox { DetectUrls = false, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true, Width = 640, Height = 100, Font = new Font("Malgun Gothic", 12), ForeColor = Color.FromArgb(28, 33, 42), BackColor = Color.White, ScrollBars = RichTextBoxScrollBars.None };
        }
        private sealed class HtmlStyle
        {
            public FontStyle FontStyle;
            public float Size = 12;
            public Color Color = Color.FromArgb(28, 33, 42);
            public Color Background = Color.White;
            public HtmlStyle Clone() { return (HtmlStyle)MemberwiseClone(); }
        }
        private static void FillHtml(RichTextBox box, string html)
        {
            if (html.Length > 200000) html = html.Substring(0, 200000);
            // Parse text formatting only. No browser, scripts, images, URLs or network resources are loaded.
            html = Regex.Replace(html, @"<(script|style|iframe|object)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
            Stack<HtmlStyle> stack = new Stack<HtmlStyle>(); stack.Push(new HtmlStyle());
            foreach (Match token in Regex.Matches(html, @"<[^>]*>|[^<]+"))
            {
                string part = token.Value;
                if (!part.StartsWith("<"))
                {
                    string text = WebUtility.HtmlDecode(part).Replace('\u00a0', ' ');
                    HtmlStyle s = stack.Peek(); box.Select(box.TextLength, 0);
                    using (Font f = new Font("Malgun Gothic", s.Size, s.FontStyle)) box.SelectionFont = f;
                    box.SelectionColor = s.Color; box.SelectionBackColor = s.Background; box.AppendText(text); continue;
                }
                Match tagMatch = Regex.Match(part, @"^<\s*(/?)\s*([\w]+)");
                if (!tagMatch.Success) continue;
                string tag = tagMatch.Groups[2].Value.ToLowerInvariant(); bool close = tagMatch.Groups[1].Value == "/";
                if (tag == "br" || (close && (tag == "p" || tag == "div" || tag == "li" || tag == "tr" || Regex.IsMatch(tag, "^h[1-6]$")))) box.AppendText("\n");
                if (tag == "td" && close) box.AppendText("\t");
                if (tag == "li" && !close) box.AppendText("• ");
                if (!(tag == "span" || tag == "font" || tag == "b" || tag == "strong" || tag == "i" || tag == "em" || tag == "u" || tag == "s" || tag == "a" || Regex.IsMatch(tag, "^h[1-6]$"))) continue;
                if (close) { if (stack.Count > 1) stack.Pop(); continue; }
                HtmlStyle current = stack.Peek().Clone();
                if (tag == "b" || tag == "strong" || tag.StartsWith("h")) current.FontStyle |= FontStyle.Bold;
                if (tag == "i" || tag == "em") current.FontStyle |= FontStyle.Italic;
                if (tag == "u" || tag == "a") current.FontStyle |= FontStyle.Underline;
                if (tag == "s") current.FontStyle |= FontStyle.Strikeout;
                if (tag == "a") current.Color = Color.RoyalBlue;
                if (Regex.IsMatch(tag, "^h[1-6]$")) current.Size = 26 - (tag[1] - '1') * 2;
                Match fg = Regex.Match(part, "(?:^|[;\\s\"'])color\\s*:\\s*([^;\"']+)", RegexOptions.IgnoreCase);
                Match bg = Regex.Match(part, "background(?:-color)?\\s*:\\s*([^;\"']+)", RegexOptions.IgnoreCase);
                Match size = Regex.Match(part, @"font-size\s*:\s*([\d.]+)\s*(px|pt)?", RegexOptions.IgnoreCase);
                Color parsed;
                if (fg.Success && TryCssColor(fg.Groups[1].Value.Trim(), out parsed)) current.Color = parsed;
                if (bg.Success && TryCssColor(bg.Groups[1].Value.Trim(), out parsed)) current.Background = parsed;
                float fontSize;
                if (size.Success && Single.TryParse(size.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize)) current.Size = Math.Max(6, Math.Min(72, fontSize * (size.Groups[2].Value == "px" ? .75f : 1f)));
                if (Regex.IsMatch(part, "font-weight\\s*:\\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)) current.FontStyle |= FontStyle.Bold;
                stack.Push(current);
            }
        }
        private static bool TryCssColor(string value, out Color color)
        {
            if (ClipboardImages.TryColor(value, out color)) return true;
            try { color = ColorTranslator.FromHtml(value); return color.A > 0; } catch { color = Color.Black; return false; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct CharRange { public int Min, Max; }
        [StructLayout(LayoutKind.Sequential)] private struct FormatRange { public IntPtr Hdc, Target; public NativeRect Area, Page; public CharRange Range; }
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref FormatRange range);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private static Bitmap RenderRichText(RichTextBox box)
        {
            IntPtr handle = box.Handle;
            using (Bitmap probe = new Bitmap(1, 1)) using (Graphics g = Graphics.FromImage(probe))
            {
                // EM_FORMATRANGE returns how much text fits. Search height to preserve styled line wrapping.
                IntPtr hdc = g.GetHdc(); int height = 80;
                try
                {
                    while (height < 6000)
                    {
                        FormatRange range = Range(hdc, 680, height, box.TextLength);
                        int last = SendMessage(handle, 0x439, IntPtr.Zero, ref range).ToInt32();
                        SendMessage(handle, 0x439, IntPtr.Zero, IntPtr.Zero);
                        if (last >= box.TextLength) break;
                        height += 40;
                    }
                }
                finally { g.ReleaseHdc(hdc); }
                Bitmap output = new Bitmap(680, height);
                using (Graphics graphics = Graphics.FromImage(output))
                {
                    graphics.Clear(Color.White); IntPtr target = graphics.GetHdc();
                    try { FormatRange range = Range(target, output.Width, output.Height, box.TextLength); SendMessage(handle, 0x439, new IntPtr(1), ref range); }
                    finally { SendMessage(handle, 0x439, IntPtr.Zero, IntPtr.Zero); graphics.ReleaseHdc(target); }
                }
                return output;
            }
        }
        private static FormatRange Range(IntPtr hdc, int width, int height, int length)
        {
            return new FormatRange { Hdc = hdc, Target = hdc, Area = new NativeRect { Left = 240, Top = 240, Right = (width - 16) * 15, Bottom = (height - 16) * 15 }, Page = new NativeRect { Right = width * 15, Bottom = height * 15 }, Range = new CharRange { Min = 0, Max = length } };
        }
    }

    public static class TgaImage
    {
        public static Bitmap Load(string path) { using (FileStream stream = File.OpenRead(path)) return Decode(stream); }
        public static Bitmap Decode(Stream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                int idLength = reader.ReadByte(), mapType = reader.ReadByte(), type = reader.ReadByte();
                int mapStart = reader.ReadUInt16(), mapLength = reader.ReadUInt16(), mapDepth = reader.ReadByte();
                reader.ReadUInt16(); reader.ReadUInt16();
                int width = reader.ReadUInt16(), height = reader.ReadUInt16(), depth = reader.ReadByte(), descriptor = reader.ReadByte();
                int baseType = type & 7; bool rle = (type & 8) != 0;
                if (width == 0 || height == 0 || (long)width * height > 50000000 || !(type == 1 || type == 2 || type == 3 || type == 9 || type == 10 || type == 11) || (descriptor & 192) != 0) throw new InvalidDataException("지원하지 않는 TGA 형식입니다.");
                if (reader.ReadBytes(idLength).Length != idLength) throw new EndOfStreamException();
                Color[] palette = new Color[mapStart + mapLength];
                if (mapType == 1) for (int i = 0; i < mapLength; i++) palette[mapStart + i] = ReadColor(reader, mapDepth, true);
                if (baseType == 1 && (mapType != 1 || (depth != 8 && depth != 16))) throw new InvalidDataException("잘못된 TGA 색상표입니다.");
                if (baseType == 2 && depth != 15 && depth != 16 && depth != 24 && depth != 32) throw new InvalidDataException("지원하지 않는 TGA 픽셀 크기입니다.");
                if (baseType == 3 && depth != 8 && depth != 16) throw new InvalidDataException("지원하지 않는 TGA 명암 형식입니다.");
                int[] pixels = new int[width * height]; int count = 0;
                while (count < pixels.Length)
                {
                    int packet = rle ? reader.ReadByte() : 0, amount = rle ? (packet & 127) + 1 : 1;
                    if (amount > pixels.Length - count) throw new InvalidDataException("TGA 패킷이 이미지 크기를 초과합니다.");
                    Color color = Color.Empty;
                    for (int i = 0; i < amount; i++)
                    {
                        if (i == 0 || (packet & 128) == 0)
                        {
                            if (baseType == 1) { int index = depth == 8 ? reader.ReadByte() : reader.ReadUInt16(); if (index < mapStart || index >= palette.Length) throw new InvalidDataException("잘못된 TGA 색상 번호입니다."); color = palette[index]; }
                            else if (baseType == 3) { int gray = reader.ReadByte(); color = Color.FromArgb(depth == 16 ? reader.ReadByte() : 255, gray, gray, gray); }
                            else color = ReadColor(reader, depth, (descriptor & 15) != 0);
                        }
                        int x = count % width, y = count / width;
                        if ((descriptor & 16) != 0) x = width - 1 - x;
                        if ((descriptor & 32) == 0) y = height - 1 - y;
                        pixels[y * width + x] = color.ToArgb(); count++;
                    }
                }
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width, IntPtr.Add(bits.Scan0, y * bits.Stride), width); }
                finally { bitmap.UnlockBits(bits); }
                return bitmap;
            }
        }
        private static Color ReadColor(BinaryReader r, int bits, bool alpha)
        {
            if (bits == 15 || bits == 16) { int v = r.ReadUInt16(); return Color.FromArgb(alpha && bits == 16 && (v & 32768) == 0 ? 0 : 255, ((v >> 10) & 31) * 255 / 31, ((v >> 5) & 31) * 255 / 31, (v & 31) * 255 / 31); }
            if (bits != 24 && bits != 32) throw new InvalidDataException("잘못된 TGA 색상 깊이입니다.");
            int b = r.ReadByte(), g = r.ReadByte(), red = r.ReadByte(), a = bits == 32 ? r.ReadByte() : 255;
            return Color.FromArgb(bits == 32 && alpha ? a : 255, red, g, b);
        }
    }
}
