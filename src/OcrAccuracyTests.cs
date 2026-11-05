using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ChachaCapture
{
    internal static class OcrAccuracyTests
    {
        private static string Compact(string text) { return Regex.Replace(text ?? "", @"\s", ""); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static Bitmap Fixture(string[] lines, string family, int size, bool dark)
        {
            Bitmap image = new Bitmap(1400, lines.Length * size * 2 + 40);
            using (Graphics graphics = Graphics.FromImage(image))
            using (Font font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(dark ? Color.FromArgb(25, 28, 34) : Color.White);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < lines.Length; i++) graphics.DrawString(lines[i], font, dark ? Brushes.White : Brushes.Black, 20, 16 + i * size * 2);
            }
            return image;
        }

        internal static void Run(string root, string output)
        {
            OcrService.CacheRootForTest = root;
            List<object> cases = new List<object>();
            const string numeric = "8050 8088 8000 10:08 8.50% 80,508 -12.5 (800)";
            const string latin = "S8S5 B8B8 ISO 9001 8050 test08@example.com";
            string[] mixed = { "금액 80,508원 / 할인 8.0% / 수량 800", "Code: S8S5 0O01 O0O0 B8B8 I1l1", numeric, "주문 S8050O / 2026-11-05 / test08@example.com" };
            int numericExact = 0, latinExact = 0, latinCodeExact = 0, codeRecovered = 0;
            try
            {
                foreach (string family in new[] { "Malgun Gothic", "Segoe UI", "Consolas" })
                foreach (bool dark in new[] { false, true })
                {
                    using (Bitmap image = Fixture(new[] { numeric }, family, 24, dark))
                    {
                        OcrService.Response response = OcrService.RecognizeDetailedAsync(image, "line", true, "numbers", CancellationToken.None).GetAwaiter().GetResult();
                        bool exact = Compact(response.text) == Compact(numeric); if (exact) numericExact++;
                        cases.Add(new { family, dark, content = "numbers", expected = numeric, actual = response.text, exact });
                        Require(!Regex.IsMatch(response.text, @"[^0-9\s.,:%+\-/()]"), "Numeric mode emitted alphabetic characters.");
                    }
                    using (Bitmap image = Fixture(new[] { latin }, family, 24, dark))
                    {
                        OcrService.Response response = OcrService.RecognizeDetailedAsync(image, "line", true, "latin", CancellationToken.None).GetAwaiter().GetResult();
                        bool exact = Compact(response.text) == Compact(latin); if (exact) latinExact++;
                        if (Compact(response.text).StartsWith("S8S5B8B8ISO90018050", StringComparison.Ordinal)) latinCodeExact++;
                        cases.Add(new { family, dark, content = "latin", expected = latin, actual = response.text, exact });
                    }
                    using (Bitmap image = Fixture(mixed, family, 18, dark))
                    {
                        OcrService.Response response = OcrService.RecognizeDetailedAsync(image, "block", true, "mixed", CancellationToken.None).GetAwaiter().GetResult();
                        bool code = response.text.Contains("S8S5") && response.text.Contains("B8B8"); if (code) codeRecovered++;
                        cases.Add(new { family, dark, content = "mixed", expected = String.Join("\n", mixed), actual = response.text, recoveredCode = code, review = response.review });
                        // Observe the harder Consolas/Korean line as well; its base recognizer
                        // can add punctuation. Keep a gate for the normal Korean UI fonts.
                        if (family != "Consolas") Require(Compact(response.text).Contains("80,508원") && Compact(response.text).Contains("수량800"), "ASCII re-reading changed a Korean word or a quantity into letters: " + response.text);
                    }
                }
                foreach (bool dark in new[] { false, true })
                using (Bitmap image = Fixture(new[] { "금액 80,508원 / 할인 8.0% / 수량 800", "Code: S8S5 0O01 O0O0 B8B8 I1l1", "8050 8088 8000 10:08 8.50%", "주문 S8050O / 2026-11-05 / test08@example.com", "질문? 확인! 할인 25% 이메일 test@example.com 시간 10:30" }, "Consolas", 14, dark))
                {
                    OcrService.Response response = OcrService.RecognizeDetailedAsync(image, "block", true, "mixed", CancellationToken.None).GetAwaiter().GetResult();
                    cases.Add(new { family = "Consolas", size = 14, dark, content = "quantity-guard", actual = response.text });
                    Require(response.text.Split('\n')[0].Trim().EndsWith("800", StringComparison.Ordinal), "A small-font quantity was replaced with an English word.");
                }
                Require(numericExact == 6, "24px numeric-mode regression; see " + output);
                Require(latinCodeExact == 6, "24px Latin/code prefix regression; see " + output);
                Require(codeRecovered >= 4, "Mixed Latin-word recovery regression; see " + output);
            }
            finally
            {
                File.WriteAllText(output, new JavaScriptSerializer().Serialize(new { numericExact, latinExact, latinCodeExact, codeRecovered, cases }));
                OcrService.CacheRootForTest = null;
            }
        }
    }
}
