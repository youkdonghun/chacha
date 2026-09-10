using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChachaCapture
{
    public enum CaptureOutcome { Edit, Copy, Pin, Save, Color }

    public sealed class CaptureResult : IDisposable
    {
        public Bitmap Image;
        public Rectangle ScreenBounds;
        public CaptureOutcome Outcome;
        public string ColorHex;

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }

    /// <summary>A frozen, pixel-accurate selection surface covering the complete virtual desktop.</summary>
    public sealed class CaptureOverlay : Form
    {
        private enum DragPart { None, New, Move, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

        private readonly Bitmap _desktop;
        private readonly Rectangle _desktopBounds;
        private readonly Rectangle _lastSelection;
        private readonly List<Rectangle> _windows = new List<Rectangle>();
        private readonly Font _normalFont = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _smallFont = new Font("Malgun Gothic", 8F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _boldFont = new Font("Malgun Gothic", 10F, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Color _accent = Color.FromArgb(65, 224, 190);
        private Rectangle _selection;
        private Rectangle _hoverWindow;
        private Rectangle _dragOriginal;
        private Rectangle _clickCandidate;
        private Point _mousePoint;
        private Point _dragStart;
        private DragPart _dragPart;
        private bool _hasSelection;
        private bool _completing;
        private int _pressedButton = -1;

        public event Action<CaptureResult> Completed;

        public Rectangle SelectedScreenBounds
        {
            get
            {
                if (!_hasSelection || _selection.IsEmpty) return Rectangle.Empty;
                return new Rectangle(_selection.X + _desktopBounds.X, _selection.Y + _desktopBounds.Y,
                    _selection.Width, _selection.Height);
            }
        }

        public CaptureOverlay(Bitmap desktop, Rectangle desktopBounds, Rectangle lastSelection)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            if (desktopBounds.Width != desktop.Width || desktopBounds.Height != desktop.Height)
                throw new ArgumentException("Desktop bounds must match the captured bitmap dimensions.", "desktopBounds");

            _desktop = (Bitmap)desktop.Clone();
            _desktopBounds = desktopBounds;
            _lastSelection = ToLocalClipped(lastSelection);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktopBounds;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Font = _normalFont;
            Text = "Chacha Capture — 영역 선택";
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            Point mouse = System.Windows.Forms.Cursor.Position;
            _mousePoint = ClampPoint(new Point(mouse.X - desktopBounds.X, mouse.Y - desktopBounds.Y));
            SnapshotWindows();
            UpdateWindowHover();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Setting the physical bounds again avoids StartPosition/monitor adjustments during handle creation.
            Bounds = _desktopBounds;
            Activate();
            Focus();
        }

        private Rectangle ToLocalClipped(Rectangle screenRectangle)
        {
            if (screenRectangle.Width < 1 || screenRectangle.Height < 1) return Rectangle.Empty;
            Rectangle clipped = Rectangle.Intersect(screenRectangle, _desktopBounds);
            if (clipped.Width < 1 || clipped.Height < 1) return Rectangle.Empty;
            clipped.Offset(-_desktopBounds.X, -_desktopBounds.Y);
            return clipped;
        }

        private void SnapshotWindows()
        {
            uint ownProcess = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr hwnd, IntPtr parameter)
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId == ownProcess) return true;
                NativeRect rect;
                if (!GetWindowRect(hwnd, out rect)) return true;
                try
                {
                    int cloaked;
                    if (DwmGetWindowAttributeInt(hwnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                        return true;
                    NativeRect frame;
                    if (DwmGetWindowAttributeRect(hwnd, 9, out frame, Marshal.SizeOf(typeof(NativeRect))) == 0 &&
                        frame.Right > frame.Left && frame.Bottom > frame.Top)
                        rect = frame;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                Rectangle local = ToLocalClipped(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                if (local.Width >= 12 && local.Height >= 12) _windows.Add(local);
                return true;
            }, IntPtr.Zero);
        }

        private void UpdateWindowHover()
        {
            _hoverWindow = Rectangle.Empty;
            for (int i = 0; i < _windows.Count; i++)
            {
                if (_windows[i].Contains(_mousePoint))
                {
                    _hoverWindow = _windows[i];
                    break;
                }
            }
        }

        private Point ClampPoint(Point point)
        {
            return new Point(Math.Max(0, Math.Min(_desktop.Width - 1, point.X)),
                Math.Max(0, Math.Min(_desktop.Height - 1, point.Y)));
        }

        private Rectangle MakeSelection(Point start, Point end)
        {
            // Inclusive endpoints let a deliberate click-and-drag select even a single pixel.
            return Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X) + 1, Math.Max(start.Y, end.Y) + 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_completing) return;
            _mousePoint = ClampPoint(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (_hasSelection || _dragPart != DragPart.None)
                    ResetSelection();
                else
                    Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            int button = HitToolbar(e.Location);
            if (button >= 0)
            {
                _pressedButton = button;
                Capture = true;
                Invalidate();
                return;
            }
            if (_hasSelection && GetToolbarBounds().Contains(e.Location)) return;

            _dragStart = _mousePoint;
            _dragOriginal = _selection;
            _dragPart = _hasSelection ? HitSelection(e.Location) : DragPart.None;
            if (_dragPart == DragPart.None)
            {
                UpdateWindowHover();
                _clickCandidate = _hoverWindow;
                _dragPart = DragPart.New;
                _hasSelection = false;
                _selection = MakeSelection(_dragStart, _dragStart);
            }
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mousePoint = ClampPoint(e.Location);
            if (_pressedButton >= 0)
            {
                Invalidate();
                return;
            }
            if (_dragPart == DragPart.New)
            {
                _selection = MakeSelection(_dragStart, _mousePoint);
            }
            else if (_dragPart != DragPart.None)
            {
                ApplyDrag(_mousePoint.X - _dragStart.X, _mousePoint.Y - _dragStart.Y);
            }
            else if (!_hasSelection)
            {
                UpdateWindowHover();
            }
            UpdateCursor();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || _completing) return;
            _mousePoint = ClampPoint(e.Location);
            if (_pressedButton >= 0)
            {
                int pressed = _pressedButton;
                _pressedButton = -1;
                Capture = false;
                if (HitToolbar(e.Location) == pressed) InvokeToolbar(pressed);
                else Invalidate();
                return;
            }
            if (_dragPart == DragPart.None) return;
            if (_dragPart == DragPart.New)
            {
                _selection = MakeSelection(_dragStart, _mousePoint);
                if (Math.Abs(_mousePoint.X - _dragStart.X) < 3 && Math.Abs(_mousePoint.Y - _dragStart.Y) < 3 &&
                    !_clickCandidate.IsEmpty)
                    _selection = _clickCandidate;
            }
            else
            {
                ApplyDrag(_mousePoint.X - _dragStart.X, _mousePoint.Y - _dragStart.Y);
            }
            _hasSelection = _selection.Width > 0 && _selection.Height > 0;
            _dragPart = DragPart.None;
            Capture = false;
            UpdateCursor();
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _dragPart != DragPart.None)
            {
                _hasSelection = _selection.Width > 0 && _selection.Height > 0;
                _dragPart = DragPart.None;
                Invalidate();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && _hasSelection && _selection.Contains(e.Location) &&
                !GetToolbarBounds().Contains(e.Location))
                Finish(CaptureOutcome.Edit);
        }

        private void ResetSelection()
        {
            _dragPart = DragPart.None;
            _pressedButton = -1;
            Capture = false;
            _hasSelection = false;
            _selection = Rectangle.Empty;
            UpdateWindowHover();
            Cursor = Cursors.Cross;
            Invalidate();
        }

        private void ApplyDrag(int dx, int dy)
        {
            Rectangle rect = _dragOriginal;
            if (_dragPart == DragPart.Move)
            {
                rect.X = Math.Max(0, Math.Min(_desktop.Width - rect.Width, rect.X + dx));
                rect.Y = Math.Max(0, Math.Min(_desktop.Height - rect.Height, rect.Y + dy));
                _selection = rect;
                return;
            }
            int left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
            if (_dragPart == DragPart.Left || _dragPart == DragPart.TopLeft || _dragPart == DragPart.BottomLeft)
                left = Math.Max(0, Math.Min(right - 1, left + dx));
            if (_dragPart == DragPart.Right || _dragPart == DragPart.TopRight || _dragPart == DragPart.BottomRight)
                right = Math.Max(left + 1, Math.Min(_desktop.Width, right + dx));
            if (_dragPart == DragPart.Top || _dragPart == DragPart.TopLeft || _dragPart == DragPart.TopRight)
                top = Math.Max(0, Math.Min(bottom - 1, top + dy));
            if (_dragPart == DragPart.Bottom || _dragPart == DragPart.BottomLeft || _dragPart == DragPart.BottomRight)
                bottom = Math.Max(top + 1, Math.Min(_desktop.Height, bottom + dy));
            _selection = Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Point[] GetHandlePoints()
        {
            int left = _selection.Left;
            int right = _selection.Right - 1;
            int top = _selection.Top;
            int bottom = _selection.Bottom - 1;
            int middleX = left + (_selection.Width - 1) / 2;
            int middleY = top + (_selection.Height - 1) / 2;
            return new Point[] { new Point(left, top), new Point(right, top), new Point(right, bottom),
                new Point(left, bottom), new Point(middleX, top), new Point(right, middleY),
                new Point(middleX, bottom), new Point(left, middleY) };
        }

        private DragPart HitSelection(Point point)
        {
            Point[] handles = GetHandlePoints();
            DragPart[] parts = new DragPart[] { DragPart.TopLeft, DragPart.TopRight, DragPart.BottomRight,
                DragPart.BottomLeft, DragPart.Top, DragPart.Right, DragPart.Bottom, DragPart.Left };
            for (int i = 0; i < handles.Length; i++)
                if (Math.Abs(point.X - handles[i].X) <= 5 && Math.Abs(point.Y - handles[i].Y) <= 5)
                    return parts[i];
            return _selection.Contains(point) ? DragPart.Move : DragPart.None;
        }

        private void UpdateCursor()
        {
            if (_hasSelection && GetToolbarBounds().Contains(_mousePoint))
            {
                Cursor = HitToolbar(_mousePoint) >= 0 ? Cursors.Hand : Cursors.Default;
                return;
            }
            DragPart part = _dragPart != DragPart.None ? _dragPart :
                (_hasSelection ? HitSelection(_mousePoint) : DragPart.None);
            switch (part)
            {
                case DragPart.Move: Cursor = Cursors.SizeAll; break;
                case DragPart.TopLeft:
                case DragPart.BottomRight: Cursor = Cursors.SizeNWSE; break;
                case DragPart.TopRight:
                case DragPart.BottomLeft: Cursor = Cursors.SizeNESW; break;
                case DragPart.Left:
                case DragPart.Right: Cursor = Cursors.SizeWE; break;
                case DragPart.Top:
                case DragPart.Bottom: Cursor = Cursors.SizeNS; break;
                default: Cursor = Cursors.Cross; break;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_completing) return true;
            Keys key = keyData & Keys.KeyCode;
            Keys modifiers = keyData & Keys.Modifiers;
            if (key == Keys.Escape) { Close(); return true; }
            if (key == Keys.C && modifiers == Keys.None) { Finish(CaptureOutcome.Color); return true; }
            if (key == Keys.R && modifiers == Keys.None)
            {
                if (!_lastSelection.IsEmpty)
                {
                    _selection = _lastSelection;
                    _hasSelection = true;
                    _dragPart = DragPart.None;
                    Capture = false;
                    Invalidate();
                }
                return true;
            }
            if (_hasSelection)
            {
                if (key == Keys.Enter) { Finish(CaptureOutcome.Edit); return true; }
                if (modifiers == Keys.Control)
                {
                    if (key == Keys.C) { Finish(CaptureOutcome.Copy); return true; }
                    if (key == Keys.P) { Finish(CaptureOutcome.Pin); return true; }
                    if (key == Keys.S) { Finish(CaptureOutcome.Save); return true; }
                }
                if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
                {
                    int dx = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
                    int dy = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
                    bool resize = (modifiers & Keys.Shift) != 0;
                    if ((modifiers & Keys.Control) != 0) { dx *= 10; dy *= 10; }
                    if (resize)
                    {
                        _selection.Width = Math.Max(1, Math.Min(_desktop.Width - _selection.X, _selection.Width + dx));
                        _selection.Height = Math.Max(1, Math.Min(_desktop.Height - _selection.Y, _selection.Height + dy));
                    }
                    else
                    {
                        _selection.X = Math.Max(0, Math.Min(_desktop.Width - _selection.Width, _selection.X + dx));
                        _selection.Y = Math.Max(0, Math.Min(_desktop.Height - _selection.Height, _selection.Y + dy));
                    }
                    Invalidate();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Rectangle GetMonitorBounds(Point localPoint)
        {
            Point screenPoint = new Point(localPoint.X + _desktopBounds.X, localPoint.Y + _desktopBounds.Y);
            Rectangle monitor = Screen.FromPoint(screenPoint).Bounds;
            monitor.Offset(-_desktopBounds.X, -_desktopBounds.Y);
            Rectangle visible = Rectangle.Intersect(monitor, new Rectangle(Point.Empty, _desktop.Size));
            return visible.Width > 0 && visible.Height > 0 ? visible : new Rectangle(Point.Empty, _desktop.Size);
        }

        private static int Fit(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(Math.Max(minimum, maximum), value));
        }

        private Rectangle GetToolbarBounds()
        {
            // Anchor to the monitor containing the selection's bottom-right pixel; it stays still while hovering.
            Rectangle monitor = GetMonitorBounds(new Point(_selection.Right - 1, _selection.Bottom - 1));
            int width = Math.Min(440, Math.Max(1, monitor.Width - 16));
            int height = Math.Min(81, Math.Max(1, monitor.Height - 16));
            int x = Fit(_selection.Right - width, monitor.Left + 8, monitor.Right - width - 8);
            int y = _selection.Bottom + 12;
            if (y + height > monitor.Bottom - 8) y = _selection.Top - height - 12;
            y = Fit(y, monitor.Top + 8, monitor.Bottom - height - 8);
            return new Rectangle(x, y, width, height);
        }

        private Rectangle GetToolbarButton(Rectangle bar, int index)
        {
            int left = bar.X + 7 + (bar.Width - 14) * index / 5;
            int right = bar.X + 7 + (bar.Width - 14) * (index + 1) / 5;
            return new Rectangle(left, bar.Y + 7, Math.Max(1, right - left - 3), 43);
        }

        private int HitToolbar(Point point)
        {
            if (!_hasSelection || _dragPart != DragPart.None) return -1;
            Rectangle bar = GetToolbarBounds();
            for (int i = 0; i < 5; i++)
                if (GetToolbarButton(bar, i).Contains(point)) return i;
            return -1;
        }

        private void InvokeToolbar(int index)
        {
            switch (index)
            {
                case 0: Finish(CaptureOutcome.Edit); break;
                case 1: Finish(CaptureOutcome.Copy); break;
                case 2: Finish(CaptureOutcome.Pin); break;
                case 3: Finish(CaptureOutcome.Save); break;
                default: Close(); break;
            }
        }

        private void Finish(CaptureOutcome outcome)
        {
            if (_completing || (outcome != CaptureOutcome.Color && !_hasSelection)) return;
            _completing = true;
            _dragPart = DragPart.None;
            Capture = false;
            CaptureResult result = new CaptureResult();
            result.Outcome = outcome;
            if (outcome == CaptureOutcome.Color)
            {
                Color color = _desktop.GetPixel(_mousePoint.X, _mousePoint.Y);
                result.ColorHex = String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
                result.ScreenBounds = new Rectangle(_mousePoint.X + _desktopBounds.X,
                    _mousePoint.Y + _desktopBounds.Y, 1, 1);
            }
            else
            {
                result.ScreenBounds = SelectedScreenBounds;
                result.Image = _desktop.Clone(_selection, PixelFormat.Format32bppArgb);
            }
            Hide();
            try
            {
                Action<CaptureResult> handler = Completed;
                if (handler != null) handler(result);
                else result.Dispose();
            }
            finally { Close(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_desktop, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            using (Brush shade = new SolidBrush(Color.FromArgb(132, 9, 13, 20)))
                g.FillRectangle(shade, ClientRectangle);

            Rectangle region = (_hasSelection || _dragPart == DragPart.New) ? _selection : _hoverWindow;
            if (region.Width > 0 && region.Height > 0)
            {
                g.DrawImage(_desktop, region, region, GraphicsUnit.Pixel);
                using (Pen outline = new Pen(_accent, 1F))
                    g.DrawRectangle(outline, region.Left, region.Top, Math.Max(0, region.Width - 1), Math.Max(0, region.Height - 1));
                DrawDimensions(g, region);
            }

            if (_hasSelection)
            {
                DrawHandles(g);
                if (_dragPart == DragPart.None) DrawToolbar(g);
            }
            else
            {
                DrawCrosshair(g);
                DrawMagnifier(g);
                if (_dragPart == DragPart.None) DrawStartHint(g);
            }
        }

        private void DrawDimensions(Graphics g, Rectangle region)
        {
            string text = region.Width + " × " + region.Height;
            Size textSize = TextRenderer.MeasureText(text, _boldFont);
            Rectangle monitor = GetMonitorBounds(new Point(region.Left, region.Top));
            int width = textSize.Width + 17, height = textSize.Height + 10;
            int x = Fit(region.Left, monitor.Left + 3, monitor.Right - width - 3);
            int y = region.Top - height - 7;
            if (y < monitor.Top + 3) y = region.Top + 8;
            y = Fit(y, monitor.Top + 3, monitor.Bottom - height - 3);
            Rectangle badge = new Rectangle(x, y, width, height);
            using (Brush background = new SolidBrush(Color.FromArgb(240, 24, 31, 42))) g.FillRectangle(background, badge);
            TextRenderer.DrawText(g, text, _boldFont, badge, _accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawHandles(Graphics g)
        {
            Point[] points = GetHandlePoints();
            using (Brush fill = new SolidBrush(Color.FromArgb(22, 31, 40)))
            using (Pen outline = new Pen(_accent, 1F))
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Rectangle handle = new Rectangle(points[i].X - 3, points[i].Y - 3, 6, 6);
                    g.FillRectangle(fill, handle);
                    g.DrawRectangle(outline, handle);
                }
            }
        }

        private void DrawToolbar(Graphics g)
        {
            Rectangle bar = GetToolbarBounds();
            using (Brush background = new SolidBrush(Color.FromArgb(250, 24, 31, 42))) g.FillRectangle(background, bar);
            using (Pen border = new Pen(Color.FromArgb(73, 85, 100)))
                g.DrawRectangle(border, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);
            string[] labels = { "편집", "복사", "고정", "저장", "취소" };
            string[] shortcuts = { "Enter", "Ctrl+C", "Ctrl+P", "Ctrl+S", "Esc" };
            int hover = HitToolbar(_mousePoint);
            for (int i = 0; i < labels.Length; i++)
            {
                Rectangle button = GetToolbarButton(bar, i);
                if (i == hover || i == _pressedButton || i == 0)
                {
                    Color fill = i == _pressedButton ? Color.FromArgb(51, 106, 96) :
                        i == hover ? Color.FromArgb(49, 64, 77) : Color.FromArgb(31, 71, 66);
                    using (Brush background = new SolidBrush(fill)) g.FillRectangle(background, button);
                }
                Rectangle label = new Rectangle(button.X, button.Y + 2, button.Width, 21);
                TextRenderer.DrawText(g, labels[i], _normalFont, label, i == 0 ? _accent : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Rectangle shortcut = new Rectangle(button.X, button.Y + 24, button.Width, 16);
                TextRenderer.DrawText(g, shortcuts[i], _smallFont, shortcut, Color.FromArgb(167, 180, 194),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            Rectangle hint = new Rectangle(bar.X + 6, bar.Y + 56, bar.Width - 12, 18);
            TextRenderer.DrawText(g, "방향키 이동 · Shift+방향키 크기 · 우클릭 다시 선택", _smallFont, hint,
                Color.FromArgb(175, 188, 201), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void DrawCrosshair(Graphics g)
        {
            using (Pen dark = new Pen(Color.FromArgb(180, 0, 0, 0)))
            using (Pen light = new Pen(Color.FromArgb(190, 237, 250, 248)))
            {
                g.DrawLine(dark, _mousePoint.X + 1, 0, _mousePoint.X + 1, Height);
                g.DrawLine(dark, 0, _mousePoint.Y + 1, Width, _mousePoint.Y + 1);
                light.DashStyle = DashStyle.Dot;
                g.DrawLine(light, _mousePoint.X, 0, _mousePoint.X, Height);
                g.DrawLine(light, 0, _mousePoint.Y, Width, _mousePoint.Y);
            }
        }

        private void DrawMagnifier(Graphics g)
        {
            const int sampleSide = 17;
            const int zoom = 7;
            const int viewSide = sampleSide * zoom;
            const int width = 197;
            const int height = 197;
            Rectangle monitor = GetMonitorBounds(_mousePoint);
            int x = _mousePoint.X + 25;
            int y = _mousePoint.Y + 25;
            if (x + width > monitor.Right - 8) x = _mousePoint.X - width - 25;
            if (y + height > monitor.Bottom - 8) y = _mousePoint.Y - height - 25;
            x = Fit(x, monitor.Left + 8, monitor.Right - width - 8);
            y = Fit(y, monitor.Top + 8, monitor.Bottom - height - 8);
            Rectangle box = new Rectangle(x, y, width, height);
            using (Brush background = new SolidBrush(Color.FromArgb(247, 23, 30, 40))) g.FillRectangle(background, box);
            using (Pen border = new Pen(Color.FromArgb(106, 126, 142))) g.DrawRectangle(border, box);
            Rectangle viewport = new Rectangle(x + (width - viewSide) / 2, y + 8, viewSide, viewSide);
            g.FillRectangle(Brushes.Black, viewport);
            Rectangle sample = new Rectangle(_mousePoint.X - sampleSide / 2, _mousePoint.Y - sampleSide / 2,
                sampleSide, sampleSide);
            Rectangle clipped = Rectangle.Intersect(sample, new Rectangle(Point.Empty, _desktop.Size));
            Rectangle destination = new Rectangle(viewport.X + (clipped.X - sample.X) * zoom,
                viewport.Y + (clipped.Y - sample.Y) * zoom, clipped.Width * zoom, clipped.Height * zoom);
            InterpolationMode interpolation = g.InterpolationMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_desktop, destination, clipped, GraphicsUnit.Pixel);
            g.InterpolationMode = interpolation;
            g.PixelOffsetMode = offset;
            using (Pen grid = new Pen(Color.FromArgb(38, 20, 25, 30)))
            {
                for (int i = 0; i <= sampleSide; i++)
                {
                    g.DrawLine(grid, viewport.X + i * zoom, viewport.Y, viewport.X + i * zoom, viewport.Bottom);
                    g.DrawLine(grid, viewport.X, viewport.Y + i * zoom, viewport.Right, viewport.Y + i * zoom);
                }
            }
            int centerX = viewport.X + sampleSide / 2 * zoom;
            int centerY = viewport.Y + sampleSide / 2 * zoom;
            using (Pen black = new Pen(Color.Black, 3F)) g.DrawRectangle(black, centerX - 1, centerY - 1, zoom + 1, zoom + 1);
            using (Pen accent = new Pen(_accent, 1F)) g.DrawRectangle(accent, centerX - 1, centerY - 1, zoom + 1, zoom + 1);
            Color color = _desktop.GetPixel(_mousePoint.X, _mousePoint.Y);
            string hex = String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            Rectangle swatch = new Rectangle(x + 16, y + 138, 18, 18);
            using (Brush fill = new SolidBrush(color)) g.FillRectangle(fill, swatch);
            using (Pen border = new Pen(Color.FromArgb(135, 150, 160))) g.DrawRectangle(border, swatch);
            TextRenderer.DrawText(g, hex + "   C 복사", _normalFont, new Rectangle(x + 40, y + 135, width - 48, 24),
                Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            string rgb = String.Format("RGB  {0}, {1}, {2}", color.R, color.G, color.B);
            TextRenderer.DrawText(g, rgb, _smallFont, new Rectangle(x + 9, y + 160, width - 18, 16),
                Color.FromArgb(187, 200, 211), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            string position = String.Format("X {0}   Y {1}", _mousePoint.X + _desktopBounds.X, _mousePoint.Y + _desktopBounds.Y);
            TextRenderer.DrawText(g, position, _smallFont, new Rectangle(x + 9, y + 178, width - 18, 16),
                Color.FromArgb(149, 167, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawStartHint(Graphics g)
        {
            Rectangle monitor = GetMonitorBounds(_mousePoint);
            int width = Math.Min(620, monitor.Width - 24);
            if (width < 1) return;
            Rectangle hint = new Rectangle(monitor.Left + (monitor.Width - width) / 2, monitor.Top + 24, width, 66);
            // Keep the hint away from the pointer and magnifier when selecting near the top edge.
            if (Math.Abs(_mousePoint.Y - hint.Top) < 100)
                hint.Y = Math.Max(monitor.Top, monitor.Bottom - hint.Height - 24);
            using (Brush fill = new SolidBrush(Color.FromArgb(237, 23, 30, 40))) g.FillRectangle(fill, hint);
            Rectangle heading = new Rectangle(hint.X + 8, hint.Y + 9, hint.Width - 16, 24);
            TextRenderer.DrawText(g, "드래그하여 캡처 · 창을 클릭하여 선택", _boldFont, heading, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
            Rectangle detail = new Rectangle(hint.X + 8, hint.Y + 37, hint.Width - 16, 19);
            TextRenderer.DrawText(g, "C 색상 복사    R 이전 영역    Esc 취소", _normalFont, detail, _accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _desktop.Dispose();
                _normalFont.Dispose();
                _smallFont.Dispose();
                _boldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>Call before showing any capture overlay. Returned bitmap belongs to the caller.</summary>
        public static Bitmap CaptureDesktop(bool includeCursor, out Rectangle bounds)
        {
            bounds = SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("No desktop display is available.");
            Bitmap image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    // Graphics.CopyFromScreen validates single enum values and rejects CaptureBlt | SourceCopy.
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                    if (includeCursor) DrawCursor(graphics, bounds);
                }
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private static void DrawCursor(Graphics graphics, Rectangle bounds)
        {
            CursorInfo cursor = new CursorInfo();
            cursor.Size = Marshal.SizeOf(typeof(CursorInfo));
            if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0 || cursor.CursorHandle == IntPtr.Zero) return;
            IconInfo icon;
            if (!GetIconInfo(cursor.CursorHandle, out icon)) return;
            try
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    DrawIconEx(hdc, cursor.Position.X - bounds.X - (int)icon.HotspotX,
                        cursor.Position.Y - bounds.Y - (int)icon.HotspotY,
                        cursor.CursorHandle, 0, 0, 0, IntPtr.Zero, 3);
                }
                finally { graphics.ReleaseHdc(hdc); }
            }
            finally
            {
                if (icon.MaskBitmap != IntPtr.Zero) DeleteObject(icon.MaskBitmap);
                if (icon.ColorBitmap != IntPtr.Zero) DeleteObject(icon.ColorBitmap);
            }
        }

        private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo { public int Size; public int Flags; public IntPtr CursorHandle; public NativePoint Position; }
        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
            public uint HotspotX, HotspotY;
            public IntPtr MaskBitmap, ColorBitmap;
        }

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attribute, out NativeRect value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height,
            uint animationStep, IntPtr flickerFreeBrush, uint flags);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);
    }
}
