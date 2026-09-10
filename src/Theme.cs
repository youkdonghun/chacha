using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal static class Theme
    {
        public static GraphicsPath Round(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Mix(Color first, Color second, float weight)
        {
            return Color.FromArgb((int)(first.R + (second.R - first.R) * weight),
                (int)(first.G + (second.G - first.G) * weight), (int)(first.B + (second.B - first.B) * weight));
        }
    }

    public class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color OutlineColor { get; set; }
        public RoundedPanel()
        {
            DoubleBuffered = true;
            CornerRadius = 18;
            OutlineColor = Ui.Border;
            BackColor = Ui.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Ui.Background : Parent.BackColor);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), CornerRadius * e.Graphics.DpiX / 96f))
            {
                using (Brush fill = new SolidBrush(BackColor)) e.Graphics.FillPath(fill, path);
                if (OutlineColor.A > 0) using (Pen border = new Pen(OutlineColor)) e.Graphics.DrawPath(border, path);
            }
        }
    }

    // Inherit Button so keyboard navigation, IButtonControl, accessibility and click semantics stay native.
    public sealed class RoundedButton : Button
    {
        private bool hovered, pressed;
        private bool isDefault;
        public int CornerRadius { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            CornerRadius = 12;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
        }

        public override void NotifyDefault(bool value) { isDefault = value; base.NotifyDefault(value); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) pressed = false; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color surroundings = Parent == null ? Ui.Background : Parent.BackColor;
            e.Graphics.Clear(surroundings);
            if (Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Theme.Mix(BackColor, surroundings, .6f) : pressed ? Theme.Mix(BackColor, Color.Black, .16f) : hovered ? Theme.Mix(BackColor, Color.White, .09f) : BackColor;
            Color outline = Focused && ShowFocusCues ? Ui.Accent : FlatAppearance.BorderColor;
            if (isDefault && !Focused) outline = Theme.Mix(outline, Ui.Accent, .38f);
            using (GraphicsPath path = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), CornerRadius * e.Graphics.DpiX / 96f))
            {
                using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                using (Pen border = new Pen(Enabled ? outline : Theme.Mix(outline, surroundings, .6f), Focused && ShowFocusCues ? 2 : 1)) e.Graphics.DrawPath(border, path);
            }
            Rectangle textBounds = Rectangle.Inflate(ClientRectangle, -5, -3);
            if (pressed) textBounds.Offset(0, 1);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Theme.Mix(ForeColor, surroundings, .5f), flags);
        }
    }
}
