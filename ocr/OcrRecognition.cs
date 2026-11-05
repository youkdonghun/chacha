using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Tesseract;

namespace ChachaCapture
{
    internal static class OcrRecognition
    {
        internal sealed class Reading { public string text { get; set; } public string[] review { get; set; } }
        private sealed class Word
        {
            internal string Text;
            internal Rectangle Bounds;
            internal float Confidence;
            internal int Offset;
        }

        internal static string Read(Pix pixels, string data, PageSegMode mode, string content)
        { return Recognize(pixels, data, mode, content).text; }

        internal static Reading Recognize(Pix pixels, string data, PageSegMode mode, string content)
        {
            bool mixed = content != "latin" && content != "numbers";
            string text; List<Word> words;
            using (TesseractEngine engine = new TesseractEngine(mixed ? data : Path.Combine(data, "accurate"), mixed ? "kor+eng" : "eng", EngineMode.LstmOnly))
            {
                engine.SetVariable("preserve_interword_spaces", "1");
                if (content == "numbers") engine.SetVariable("tessedit_char_whitelist", "0123456789.,:%+-/() ");
                using (Page page = engine.Process(pixels, mode))
                {
                    text = page.GetText(); words = mixed ? Words(page, text) : null;
                }
            }
            if (!mixed || String.IsNullOrWhiteSpace(text)) return new Reading { text = text.Trim(), review = new string[0] };
            // The Korean recognizer can assign very high confidence to digit-shaped Latin
            // letters (S8S5 -> 5855). Re-read ASCII words with the English recognizer, matching
            // the same image boxes. Korean words and punctuation-only words stay untouched.
            List<Word> latin;
            using (TesseractEngine engine = new TesseractEngine(data, "eng", EngineMode.LstmOnly))
            using (Page page = engine.Process(pixels, mode)) latin = Words(page, page.GetText());
            StringBuilder result = new StringBuilder(); int offset = 0;
            List<string> review = new List<string>();
            foreach (Word word in words)
            {
                if (word.Offset < offset || !AsciiWord(word.Text)) continue;
                Word match = null; double best = .8;
                foreach (Word candidate in latin)
                {
                    if (candidate.Confidence < 50 || !AsciiWord(candidate.Text) || !CompatibleWord(word.Text, candidate.Text)) continue;
                    Rectangle overlap = Rectangle.Intersect(word.Bounds, candidate.Bounds);
                    double area = (double)overlap.Width * overlap.Height;
                    double union = (double)word.Bounds.Width * word.Bounds.Height + (double)candidate.Bounds.Width * candidate.Bounds.Height - area;
                    double score = union > 0 ? area / union : 0;
                    if (score > best) { best = score; match = candidate; }
                }
                if (match == null || match.Text == word.Text) continue;
                if (review.Count < 20) review.Add(match.Text + " / " + word.Text);
                result.Append(text, offset, word.Offset - offset); result.Append(match.Text);
                offset = word.Offset + word.Text.Length;
            }
            result.Append(text, offset, text.Length - offset);
            return new Reading { text = result.ToString().Trim(), review = review.ToArray() };
        }

        private static bool AsciiWord(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            bool alphanumeric = false;
            foreach (char c in text) { if (c > 127 || Char.IsWhiteSpace(c)) return false; if (Char.IsLetterOrDigit(c)) alphanumeric = true; }
            return alphanumeric;
        }

        private static bool CompatibleWord(string original, string candidate)
        {
            StringBuilder before = new StringBuilder(), after = new StringBuilder();
            bool originalLetter = false, candidateDigit = false, candidateLowercase = false;
            foreach (char c in original) { if (Char.IsLetter(c)) originalLetter = true; if (!Char.IsLetterOrDigit(c)) before.Append(c); }
            foreach (char c in candidate) { if (Char.IsDigit(c)) candidateDigit = true; if (Char.IsLower(c)) candidateLowercase = true; if (!Char.IsLetterOrDigit(c)) after.Append(c); }
            // Do not turn a quantity such as 800 into an English word ("see"), or insert
            // punctuation into a code. Explicit Latin mode remains available for such text.
            return before.ToString() == after.ToString() && (originalLetter || candidateDigit && !candidateLowercase);
        }

        private static List<Word> Words(Page page, string text)
        {
            List<Word> words = new List<Word>(); int offset = 0;
            using (ResultIterator iterator = page.GetIterator())
            {
                iterator.Begin();
                do
                {
                    string value = iterator.GetText(PageIteratorLevel.Word);
                    Rect box;
                    if (String.IsNullOrEmpty(value) || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out box)) continue;
                    int start = text.IndexOf(value, offset, StringComparison.Ordinal);
                    if (start < 0) continue;
                    words.Add(new Word { Text = value, Offset = start, Confidence = iterator.GetConfidence(PageIteratorLevel.Word), Bounds = new Rectangle(box.X1, box.Y1, box.Width, box.Height) });
                    offset = start + value.Length;
                } while (iterator.Next(PageIteratorLevel.Word));
            }
            return words;
        }
    }
}
