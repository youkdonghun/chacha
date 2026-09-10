using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
internal static class GenerateIcon
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(System.IntPtr icon);
    private static void Main(string[] args)
    {
        using (Bitmap bitmap = new Bitmap(64, 64))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 21, 30))) g.FillEllipse(b, 1, 1, 62, 62);
            using (Pen p = new Pen(Color.FromArgb(94, 234, 196), 6))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLines(p, new Point[] { new Point(17, 28), new Point(17, 17), new Point(28, 17) });
                g.DrawLines(p, new Point[] { new Point(36, 17), new Point(47, 17), new Point(47, 28) });
                g.DrawLines(p, new Point[] { new Point(47, 36), new Point(47, 47), new Point(36, 47) });
                g.DrawLines(p, new Point[] { new Point(28, 47), new Point(17, 47), new Point(17, 36) });
            }
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, 28, 28, 8, 8);
            System.IntPtr handle = bitmap.GetHicon();
            try { using (Icon icon = Icon.FromHandle(handle)) using (FileStream stream = File.Create(args[0])) icon.Save(stream); }
            finally { DestroyIcon(handle); }
        }
    }
}
