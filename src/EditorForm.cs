using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChachaCapture
{
    /// <summary>A non-destructive, full-resolution screenshot editor.</summary>
    public sealed class EditorForm : Form
    {
        public event Action<Bitmap> PinRequested;
        public event Action<Bitmap> ImageCommitted;
        public string SaveDirectory { get; set; }

        private enum Tool { Move, Rectangle, Ellipse, Arrow, Line, Pen, Highlight, Text, Number, Mosaic, Blur, Crop, Eraser }

        private sealed class Mark
        {
            internal Tool Tool;
            internal PointF Start;
            internal PointF End;
            internal Color Color;
            internal float Width;
            internal float TextSize;
            internal string Text;
            internal int Number;
            internal List<PointF> Points = new List<PointF>();
        }

        private sealed class DocumentState
        {
            internal Bitmap Image;
            internal List<Mark> Marks;
            internal int NextNumber;
            internal DocumentState(Bitmap image) { Image = image; Marks = new List<Mark>(); NextNumber = 1; }
            internal DocumentState Snapshot()
            {
                DocumentState copy = new DocumentState(Image);
                copy.Marks = new List<Mark>(Marks);
                copy.NextNumber = NextNumber;
                return copy;
            }
        }

        private sealed class CanvasControl : Control
        {
            internal CanvasControl()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
            }
            protected override bool IsInputKey(Keys keyData)
            {
                if ((keyData & Keys.KeyCode) == Keys.Space) return true;
                return base.IsInputKey(keyData);
            }
        }

        private sealed class DarkButton : Button
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (!Enabled)
                {
                    using (SolidBrush fill = new SolidBrush(BackColor)) e.Graphics.FillRectangle(fill, 1, 1, Math.Max(0, Width - 2), Math.Max(0, Height - 2));
                    TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.FromArgb(121, 132, 151), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
        }

        private readonly Color _background = Color.FromArgb(22, 25, 32);
        private readonly Color _surface = Color.FromArgb(31, 35, 45);
        private readonly Color _text = Color.FromArgb(228, 232, 241);
        private readonly Color _muted = Color.FromArgb(157, 168, 187);
        private readonly Color _accent = Color.FromArgb(94, 234, 196);
        private readonly CanvasControl _canvas = new CanvasControl();
        private readonly Dictionary<Tool, Button> _toolButtons = new Dictionary<Tool, Button>();
        private readonly List<DocumentState> _undo = new List<DocumentState>();
        private readonly List<DocumentState> _redo = new List<DocumentState>();
        private readonly HashSet<Bitmap> _ownedImages = new HashSet<Bitmap>();
        private readonly List<Font> _ownedFonts = new List<Font>();
        private readonly ToolTip _tips = new ToolTip();
        private DocumentState _state;
        private Bitmap _rendered;
        private Mark _draft;
        private Tool _tool = Tool.Arrow;
        private Color _color = Color.FromArgb(255, 83, 100);
        private float _lineWidth = 4;
        private float _textSize = 28;
        private float _zoom = 1;
        private PointF _pan;
        private bool _fit = true;
        private bool _panning;
        private bool _spaceHeld;
        private bool _drawing;
        private Point _panStart;
        private PointF _panBefore;
        private Label _status;
        private Label _sizeLabel;
        private Button _zoomLabel;
        private Button _undoButton;
        private Button _redoButton;
        private Button _colorButton;

        public EditorForm(Bitmap image)
        {
            if (image == null) throw new ArgumentNullException("image");
            SuspendLayout();
            _state = new DocumentState(CopyBitmap(image));
            _ownedImages.Add(_state.Image);
            Text = "Chacha · 캡처 편집";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _background;
            ForeColor = _text;
            Font = OwnFont("Malgun Gothic", 9F, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(860, work.Width), Math.Min(640, work.Height));
            Size = new Size(Math.Min(1280, work.Width), Math.Min(860, work.Height));
            KeyPreview = true;
            BuildInterface();
            UpdateRender();
            SetTool(Tool.Arrow);
            Shown += delegate { FitImage(); _canvas.Focus(); };
            Deactivate += delegate { _spaceHeld = false; EndPan(); };
            FormClosing += delegate { CancelGesture(); };
            ResumeLayout(true);
            PerformAutoScale();
            MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
            Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        }

        private void BuildInterface()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = _surface, Padding = new Padding(16, 8, 12, 8) };
            Label title = new Label { Text = "CHACHA", AutoSize = true, Location = new Point(18, 10), ForeColor = _accent, Font = OwnFont("Segoe UI", 15F, FontStyle.Bold) };
            _sizeLabel = new Label { AutoSize = true, Location = new Point(19, 39), ForeColor = _muted, Font = OwnFont("Segoe UI", 8.5F, FontStyle.Regular) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 445, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = _surface, Padding = new Padding(0, 6, 0, 0) };
            Button copy = MakeButton("복사  Ctrl+C", 135, 36);
            copy.Click += delegate { CopyImage(); };
            Button save = MakeButton("저장  Ctrl+S", 135, 36);
            save.Click += delegate { SaveImage(); };
            Button pin = MakeButton("고정  Ctrl+P", 135, 36);
            pin.BackColor = _accent;
            pin.ForeColor = Color.FromArgb(15, 20, 34);
            pin.Click += delegate { PinImage(); };
            actions.Controls.AddRange(new Control[] { copy, save, pin });
            header.Controls.Add(actions);
            header.Controls.Add(title);
            header.Controls.Add(_sizeLabel);

            Panel tools = new Panel { Dock = DockStyle.Top, Height = 91, BackColor = _surface };
            FlowLayoutPanel toolRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 2, 5, 0), BackColor = _surface };
            AddTool(toolRow, Tool.Move, "이동", "V · 캔버스 이동 (Space 또는 마우스 가운데 버튼도 사용 가능)");
            AddTool(toolRow, Tool.Rectangle, "사각", "R · 사각형 (Shift: 정사각형)");
            AddTool(toolRow, Tool.Ellipse, "원", "E · 타원 (Shift: 원)");
            AddTool(toolRow, Tool.Arrow, "화살표", "A · 화살표 (Shift: 45도 단위)");
            AddTool(toolRow, Tool.Line, "선", "L · 직선 (Shift: 45도 단위)");
            AddTool(toolRow, Tool.Pen, "펜", "B · 자유롭게 그리기");
            AddTool(toolRow, Tool.Highlight, "형광펜", "H · 반투명 형광펜");
            AddTool(toolRow, Tool.Text, "문자", "T · 클릭해서 글자 입력");
            AddTool(toolRow, Tool.Number, "번호", "N · 순서 번호 표시");
            AddTool(toolRow, Tool.Mosaic, "모자이크", "M · 영역을 드래그해 모자이크 처리");
            AddTool(toolRow, Tool.Blur, "흐림", "U · 영역을 드래그해 흐림 처리");
            AddTool(toolRow, Tool.Crop, "자르기", "C · 드래그한 영역으로 이미지 자르기");
            AddTool(toolRow, Tool.Eraser, "지우기", "X · 클릭한 주석 삭제 (이미지 픽셀은 그대로 유지)");
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 6, 5, 0), BackColor = _surface };
            Color[] palette = { Color.FromArgb(255, 83, 100), Color.FromArgb(255, 212, 72), Color.FromArgb(75, 215, 150), Color.FromArgb(68, 208, 230), Color.FromArgb(92, 142, 255), Color.FromArgb(205, 119, 246), Color.White, Color.FromArgb(25, 28, 34) };
            foreach (Color color in palette)
            {
                Color selected = color;
                Button swatch = MakeButton("", 21, 26);
                swatch.Margin = new Padding(2, 2, 2, 2);
                swatch.BackColor = selected;
                swatch.FlatAppearance.BorderColor = Color.FromArgb(94, 104, 125);
                swatch.FlatAppearance.MouseOverBackColor = selected;
                swatch.Click += delegate { SetColor(selected); };
                _tips.SetToolTip(swatch, String.Format("색상 #{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B));
                options.Controls.Add(swatch);
            }
            _colorButton = MakeButton("색상", 44, 28);
            _colorButton.BackColor = _color;
            _colorButton.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog())
                {
                    dialog.Color = _color;
                    dialog.FullOpen = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(dialog.Color);
                }
            };
            options.Controls.Add(_colorButton);
            Label widthLabel = new Label { Text = "굵기", AutoSize = true, Margin = new Padding(8, 6, 2, 0), ForeColor = _muted };
            NumericUpDown width = new NumericUpDown { Minimum = 1, Maximum = 40, Value = 4, Width = 47, Height = 28, Margin = new Padding(2, 2, 5, 0), BackColor = _background, ForeColor = _text, BorderStyle = BorderStyle.FixedSingle };
            width.ValueChanged += delegate { _lineWidth = (float)width.Value; _canvas.Focus(); };
            Label fontLabel = new Label { Text = "글자", AutoSize = true, Margin = new Padding(3, 6, 2, 0), ForeColor = _muted };
            NumericUpDown fontSize = new NumericUpDown { Minimum = 8, Maximum = 160, Value = 28, Increment = 2, Width = 51, Height = 28, Margin = new Padding(2, 2, 8, 0), BackColor = _background, ForeColor = _text, BorderStyle = BorderStyle.FixedSingle };
            fontSize.ValueChanged += delegate { _textSize = (float)fontSize.Value; _canvas.Focus(); };
            options.Controls.AddRange(new Control[] { widthLabel, width, fontLabel, fontSize });
            _undoButton = MakeButton("실행 취소", 77, 28);
            _redoButton = MakeButton("다시 실행", 77, 28);
            _tips.SetToolTip(_undoButton, "Ctrl+Z");
            _tips.SetToolTip(_redoButton, "Ctrl+Y / Ctrl+Shift+Z");
            _undoButton.Click += delegate { Undo(); };
            _redoButton.Click += delegate { Redo(); };
            Button minus = MakeButton("−", 30, 28);
            minus.Click += delegate { ZoomAt(_zoom / 1.25F, new Point(_canvas.Width / 2, _canvas.Height / 2)); };
            _zoomLabel = MakeButton("100%", 59, 28);
            _zoomLabel.Click += delegate { ZoomAt(1F, new Point(_canvas.Width / 2, _canvas.Height / 2)); };
            _tips.SetToolTip(_zoomLabel, "클릭: 원본 크기 · 휠: 커서 중심 확대/축소");
            Button plus = MakeButton("+", 30, 28);
            plus.Click += delegate { ZoomAt(_zoom * 1.25F, new Point(_canvas.Width / 2, _canvas.Height / 2)); };
            Button fit = MakeButton("화면 맞춤", 77, 28);
            fit.Click += delegate { FitImage(); };
            _tips.SetToolTip(fit, "Ctrl+0 · 화면 크기에 맞추기");
            options.Controls.AddRange(new Control[] { _undoButton, _redoButton, minus, _zoomLabel, plus, fit });
            tools.Controls.Add(options);
            tools.Controls.Add(toolRow);

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = _surface, Padding = new Padding(14, 0, 8, 0) };
            _status = new Label { Dock = DockStyle.Fill, ForeColor = _muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            footer.Controls.Add(_status);
            _canvas.Dock = DockStyle.Fill;
            _canvas.BackColor = _background;
            _canvas.Paint += PaintCanvas;
            _canvas.MouseDown += CanvasMouseDown;
            _canvas.MouseMove += CanvasMouseMove;
            _canvas.MouseUp += CanvasMouseUp;
            _canvas.MouseWheel += CanvasMouseWheel;
            _canvas.MouseCaptureChanged += delegate { if (!_canvas.Capture && (_drawing || _panning)) CancelGesture(); };
            _canvas.Resize += delegate { if (_fit) FitImage(); else _canvas.Invalidate(); };
            Controls.Add(_canvas);
            Controls.Add(footer);
            Controls.Add(tools);
            Controls.Add(header);
        }

        private Button MakeButton(string label, int width, int height)
        {
            Button button = new DarkButton { Text = label, Width = width, Height = height, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(43, 49, 63), ForeColor = _text, Margin = new Padding(3, 1, 3, 1), Cursor = Cursors.Hand, TabStop = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(57, 64, 81);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 69, 91);
            return button;
        }

        private Font OwnFont(string family, float size, FontStyle style)
        {
            Font font = new Font(family, size, style, GraphicsUnit.Point);
            _ownedFonts.Add(font);
            return font;
        }

        private void AddTool(FlowLayoutPanel row, Tool tool, string name, string hint)
        {
            Tool selected = tool;
            Button button = MakeButton(name, tool == Tool.Mosaic ? 65 : 55, 32);
            button.Margin = new Padding(2, 1, 2, 1);
            button.Font = OwnFont("Malgun Gothic", tool == Tool.Mosaic ? 8F : 8.5F, FontStyle.Regular);
            button.Click += delegate { SetTool(selected); };
            _tips.SetToolTip(button, hint);
            _toolButtons[tool] = button;
            row.Controls.Add(button);
        }

        private void SetTool(Tool tool)
        {
            CancelGesture();
            _tool = tool;
            foreach (KeyValuePair<Tool, Button> pair in _toolButtons)
            {
                bool active = pair.Key == tool;
                pair.Value.BackColor = active ? Color.FromArgb(34, 72, 63) : Color.FromArgb(43, 49, 63);
                pair.Value.FlatAppearance.BorderColor = active ? _accent : Color.FromArgb(57, 64, 81);
                pair.Value.ForeColor = active ? Color.White : _text;
            }
            if (tool == Tool.Highlight && _color.ToArgb() == Color.FromArgb(255, 83, 100).ToArgb()) SetColor(Color.FromArgb(255, 212, 72));
            UpdateCursor();
            string hint;
            switch (tool)
            {
                case Tool.Move: hint = "드래그하여 이미지 이동 · 휠로 확대/축소 · Ctrl+0 화면 맞춤"; break;
                case Tool.Text: hint = "이미지 위를 클릭하여 글자를 넣으세요 · 글자 크기는 원본 이미지의 픽셀 단위입니다"; break;
                case Tool.Number: hint = "클릭할 때마다 순서 번호를 표시합니다 · Ctrl+Z 실행 취소"; break;
                case Tool.Mosaic: hint = "가릴 영역을 드래그하세요 · 굵기가 클수록 모자이크 블록이 커집니다"; break;
                case Tool.Blur: hint = "흐리게 할 영역을 드래그하세요 · 굵기로 효과 강도를 조절합니다"; break;
                case Tool.Crop: hint = "드래그한 영역으로 자릅니다 · Ctrl+Z로 되돌리기 · Esc로 드래그 취소"; break;
                case Tool.Eraser: hint = "삭제할 주석을 클릭하세요 · 가장 위에 있는 주석부터 지워집니다"; break;
                default: hint = "드래그하여 그리기 · Shift로 각도/비율 고정 · Space+드래그로 이동 · 휠로 확대/축소"; break;
            }
            SetStatus(hint);
            _canvas.Focus();
        }

        private void SetColor(Color color)
        {
            _color = color;
            _colorButton.BackColor = color;
            _colorButton.ForeColor = color.GetBrightness() > 0.6F ? Color.FromArgb(25, 28, 35) : Color.White;
            _canvas.Focus();
        }

        private PointF ImageOrigin
        {
            get { return new PointF((_canvas.Width - _state.Image.Width * _zoom) / 2F + _pan.X, (_canvas.Height - _state.Image.Height * _zoom) / 2F + _pan.Y); }
        }

        private PointF ToImage(Point point, bool clamp)
        {
            PointF origin = ImageOrigin;
            float x = (point.X - origin.X) / _zoom;
            float y = (point.Y - origin.Y) / _zoom;
            if (clamp) { x = Math.Max(0, Math.Min(_state.Image.Width, x)); y = Math.Max(0, Math.Min(_state.Image.Height, y)); }
            return new PointF(x, y);
        }

        private void FitImage()
        {
            if (_state == null || _canvas.Width < 2 || _canvas.Height < 2) return;
            _fit = true;
            _pan = PointF.Empty;
            _zoom = Math.Max(0.02F, Math.Min(1F, Math.Min((_canvas.Width - 48F) / _state.Image.Width, (_canvas.Height - 48F) / _state.Image.Height)));
            UpdateZoomLabel();
            _canvas.Invalidate();
        }

        private void ZoomAt(float requested, Point anchor)
        {
            if (_drawing) return;
            PointF imagePoint = ToImage(anchor, false);
            _zoom = Math.Max(0.02F, Math.Min(16F, requested));
            _fit = false;
            _pan = new PointF(anchor.X - imagePoint.X * _zoom - (_canvas.Width - _state.Image.Width * _zoom) / 2F,
                              anchor.Y - imagePoint.Y * _zoom - (_canvas.Height - _state.Image.Height * _zoom) / 2F);
            UpdateZoomLabel();
            _canvas.Invalidate();
        }

        private void UpdateZoomLabel() { if (_zoomLabel != null) _zoomLabel.Text = Math.Round(_zoom * 100).ToString() + "%"; }

        private void CanvasMouseWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) == 0)
                ZoomAt(_zoom * (float)Math.Pow(1.15, e.Delta / 120.0), e.Location);
            else
            {
                _fit = false;
                _pan.X += e.Delta / 2F;
                _canvas.Invalidate();
            }
        }

        private void PaintCanvas(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            PointF origin = ImageOrigin;
            RectangleF imageBounds = new RectangleF(origin.X, origin.Y, _state.Image.Width * _zoom, _state.Image.Height * _zoom);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(10, 12, 17)))
                g.FillRectangle(shadow, imageBounds.X + 5, imageBounds.Y + 7, imageBounds.Width, imageBounds.Height);
            GraphicsState state = g.Save();
            g.SetClip(RectangleF.Intersect(imageBounds, _canvas.ClientRectangle));
            using (SolidBrush a = new SolidBrush(Color.FromArgb(62, 65, 72)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(74, 77, 85)))
            {
                Rectangle clip = Rectangle.Ceiling(RectangleF.Intersect(imageBounds, _canvas.ClientRectangle));
                for (int y = clip.Top / 12 * 12; y < clip.Bottom; y += 12)
                    for (int x = clip.Left / 12 * 12; x < clip.Right; x += 12)
                        g.FillRectangle(((x / 12 + y / 12) & 1) == 0 ? a : b, x, y, 12, 12);
            }
            g.TranslateTransform(origin.X, origin.Y);
            g.ScaleTransform(_zoom, _zoom);
            g.InterpolationMode = _zoom >= 2F ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (_rendered != null) g.DrawImage(_rendered, new Rectangle(0, 0, _rendered.Width, _rendered.Height), 0, 0, _rendered.Width, _rendered.Height, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_draft != null && _draft.Tool != Tool.Crop && _draft.Tool != Tool.Mosaic && _draft.Tool != Tool.Blur) DrawMark(g, _draft);
            g.Restore(state);

            if (_draft != null && (_draft.Tool == Tool.Crop || _draft.Tool == Tool.Mosaic || _draft.Tool == Tool.Blur))
            {
                RectangleF rect = Normalized(_draft.Start, _draft.End);
                RectangleF screenRect = new RectangleF(origin.X + rect.X * _zoom, origin.Y + rect.Y * _zoom, rect.Width * _zoom, rect.Height * _zoom);
                if (_draft.Tool == Tool.Crop)
                {
                    using (Region outside = new Region(imageBounds))
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(155, 0, 0, 0)))
                    {
                        outside.Exclude(screenRect);
                        g.FillRegion(shade, outside);
                    }
                }
                else
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(55, _accent))) g.FillRectangle(fill, screenRect);
                }
                using (Pen border = new Pen(_accent, 1.5F)) { border.DashStyle = DashStyle.Dash; g.DrawRectangle(border, screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height); }
                string dimensions = Math.Round(rect.Width).ToString() + " × " + Math.Round(rect.Height).ToString();
                Size textSize = TextRenderer.MeasureText(dimensions, Font);
                Rectangle label = new Rectangle((int)screenRect.Left, Math.Max(0, (int)screenRect.Top - 28), textSize.Width + 14, 24);
                using (SolidBrush fill = new SolidBrush(_accent)) g.FillRectangle(fill, label);
                TextRenderer.DrawText(g, dimensions, Font, label, Color.FromArgb(15, 20, 34), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void CanvasMouseDown(object sender, MouseEventArgs e)
        {
            _canvas.Focus();
            if (e.Button == MouseButtons.Right) { CancelGesture(); return; }
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (_spaceHeld || _tool == Tool.Move)))
            {
                _panning = true;
                _panStart = e.Location;
                _panBefore = _pan;
                _canvas.Capture = true;
                _canvas.Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            PointF point = ToImage(e.Location, false);
            if (point.X < 0 || point.Y < 0 || point.X >= _state.Image.Width || point.Y >= _state.Image.Height) return;
            if (_tool == Tool.Eraser) { EraseAt(point); return; }
            if (_tool == Tool.Text)
            {
                string text = PromptText();
                if (String.IsNullOrWhiteSpace(text)) return;
                Mark mark = CreateMark(point);
                mark.Text = text;
                PushUndo();
                _state.Marks.Add(mark);
                UpdateRender();
                return;
            }
            if (_tool == Tool.Number)
            {
                Mark mark = CreateMark(point);
                mark.Number = _state.NextNumber;
                PushUndo();
                _state.Marks.Add(mark);
                _state.NextNumber++;
                UpdateRender();
                return;
            }
            _draft = CreateMark(point);
            _draft.Points.Add(point);
            _drawing = true;
            _canvas.Capture = true;
            _canvas.Invalidate();
        }

        private Mark CreateMark(PointF point)
        {
            return new Mark { Tool = _tool, Start = point, End = point, Color = _color, Width = _lineWidth, TextSize = _textSize };
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_panning)
            {
                _fit = false;
                _pan = new PointF(_panBefore.X + e.X - _panStart.X, _panBefore.Y + e.Y - _panStart.Y);
                _canvas.Invalidate();
                return;
            }
            if (!_drawing || _draft == null) return;
            PointF point = ToImage(e.Location, true);
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                float dx = point.X - _draft.Start.X;
                float dy = point.Y - _draft.Start.Y;
                if (_tool == Tool.Rectangle || _tool == Tool.Ellipse || _tool == Tool.Crop)
                {
                    float length = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    float sx = dx < 0 ? -1F : 1F;
                    float sy = dy < 0 ? -1F : 1F;
                    length = Math.Min(length, sx > 0 ? _state.Image.Width - _draft.Start.X : _draft.Start.X);
                    length = Math.Min(length, sy > 0 ? _state.Image.Height - _draft.Start.Y : _draft.Start.Y);
                    point = new PointF(_draft.Start.X + sx * length, _draft.Start.Y + sy * length);
                }
                else if (_tool == Tool.Line || _tool == Tool.Arrow)
                {
                    double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    point = new PointF(_draft.Start.X + (float)(Math.Cos(angle) * length), _draft.Start.Y + (float)(Math.Sin(angle) * length));
                }
            }
            _draft.End = point;
            if (_tool == Tool.Pen || _tool == Tool.Highlight)
            {
                PointF last = _draft.Points[_draft.Points.Count - 1];
                if (DistanceSquared(last, point) >= Math.Max(0.25F, 1F / (_zoom * _zoom))) _draft.Points.Add(point);
            }
            _canvas.Invalidate();
        }

        private void CanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (_panning) { EndPan(); return; }
            if (!_drawing || _draft == null || e.Button != MouseButtons.Left) return;
            CanvasMouseMove(sender, e);
            Mark mark = _draft;
            _draft = null;
            _drawing = false;
            _canvas.Capture = false;
            if (mark.Tool == Tool.Crop)
            {
                Rectangle crop = PixelRegion(Normalized(mark.Start, mark.End), _state.Image.Size);
                if (crop.Width > 1 && crop.Height > 1) CropTo(crop);
            }
            else if (mark.Tool == Tool.Pen || mark.Tool == Tool.Highlight || DistanceSquared(mark.Start, mark.End) >= 2)
            {
                PushUndo();
                _state.Marks.Add(mark);
                UpdateRender();
            }
            _canvas.Invalidate();
        }

        private void EndPan()
        {
            _panning = false;
            if (!_drawing) _canvas.Capture = false;
            UpdateCursor();
        }

        private void UpdateCursor()
        {
            _canvas.Cursor = _spaceHeld || _tool == Tool.Move ? Cursors.Hand : _tool == Tool.Text ? Cursors.IBeam : _tool == Tool.Eraser ? Cursors.No : Cursors.Cross;
        }

        private void CancelGesture()
        {
            _drawing = false;
            _draft = null;
            _panning = false;
            _canvas.Capture = false;
            UpdateCursor();
            _canvas.Invalidate();
        }

        private void PushUndo()
        {
            _undo.Add(_state.Snapshot());
            _redo.Clear();
            TrimHistory();
        }

        private void Undo()
        {
            CancelGesture();
            if (_undo.Count == 0) return;
            _redo.Add(_state);
            bool sizeChanged = _state.Image.Size != _undo[_undo.Count - 1].Image.Size;
            _state = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            UpdateRender();
            if (sizeChanged) FitImage();
            SetStatus("실행을 취소했습니다 · Ctrl+Y로 다시 실행");
        }

        private void Redo()
        {
            CancelGesture();
            if (_redo.Count == 0) return;
            _undo.Add(_state);
            bool sizeChanged = _state.Image.Size != _redo[_redo.Count - 1].Image.Size;
            _state = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            UpdateRender();
            if (sizeChanged) FitImage();
            SetStatus("다시 실행했습니다");
        }

        private void TrimHistory()
        {
            while (_undo.Count > 40) _undo.RemoveAt(0);
            while (_undo.Count > 1 && ReferencedPixels() > 48000000L) _undo.RemoveAt(0);
            HashSet<Bitmap> kept = ReferencedImages();
            List<Bitmap> removed = new List<Bitmap>();
            foreach (Bitmap bitmap in _ownedImages) if (!kept.Contains(bitmap)) removed.Add(bitmap);
            foreach (Bitmap bitmap in removed) { _ownedImages.Remove(bitmap); bitmap.Dispose(); }
        }

        private HashSet<Bitmap> ReferencedImages()
        {
            HashSet<Bitmap> result = new HashSet<Bitmap>();
            result.Add(_state.Image);
            foreach (DocumentState state in _undo) result.Add(state.Image);
            foreach (DocumentState state in _redo) result.Add(state.Image);
            return result;
        }

        private long ReferencedPixels()
        {
            long pixels = 0;
            foreach (Bitmap image in ReferencedImages()) pixels += (long)image.Width * image.Height;
            return pixels;
        }

        private void CropTo(Rectangle bounds)
        {
            Bitmap cropped = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(cropped))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_rendered, new Rectangle(Point.Empty, bounds.Size), bounds, GraphicsUnit.Pixel);
            }
            PushUndo();
            _ownedImages.Add(cropped);
            _state = new DocumentState(cropped);
            TrimHistory();
            UpdateRender();
            FitImage();
            SetStatus("이미지를 " + bounds.Width + " × " + bounds.Height + " px로 잘랐습니다 · Ctrl+Z 실행 취소");
        }

        private void EraseAt(PointF point)
        {
            for (int i = _state.Marks.Count - 1; i >= 0; i--)
            {
                if (!HitTest(_state.Marks[i], point, Math.Max(5F, 7F / _zoom))) continue;
                PushUndo();
                _state.Marks.RemoveAt(i);
                UpdateRender();
                SetStatus("주석을 지웠습니다 · Ctrl+Z로 복원");
                return;
            }
            SetStatus("주석이 있는 곳을 클릭하세요 · 캡처 원본은 지워지지 않습니다");
        }

        private static bool HitTest(Mark mark, PointF point, float tolerance)
        {
            float radius = tolerance + mark.Width;
            if (mark.Tool == Tool.Line || mark.Tool == Tool.Arrow) return SegmentDistance(point, mark.Start, mark.End) <= radius * (mark.Tool == Tool.Arrow ? 2 : 1);
            if (mark.Tool == Tool.Pen || mark.Tool == Tool.Highlight)
            {
                if (mark.Tool == Tool.Highlight) radius += Math.Max(12, mark.Width * 4) / 2;
                if (mark.Points.Count == 1) return DistanceSquared(point, mark.Points[0]) <= radius * radius;
                for (int i = 1; i < mark.Points.Count; i++) if (SegmentDistance(point, mark.Points[i - 1], mark.Points[i]) <= radius) return true;
                return false;
            }
            RectangleF bounds = Normalized(mark.Start, mark.End);
            if (mark.Tool == Tool.Number)
            {
                float r = Math.Max(16, mark.TextSize * 0.7F);
                return DistanceSquared(point, mark.Start) <= (r + tolerance) * (r + tolerance);
            }
            if (mark.Tool == Tool.Text)
            {
                using (Bitmap measure = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(measure))
                using (Font font = new Font("Malgun Gothic", mark.TextSize, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    SizeF size = g.MeasureString(mark.Text, font);
                    bounds = new RectangleF(mark.Start, size);
                }
            }
            bounds.Inflate(tolerance, tolerance);
            return bounds.Contains(point);
        }

        private static float SegmentDistance(PointF point, PointF a, PointF b)
        {
            float length = DistanceSquared(a, b);
            if (length < 0.0001F) return (float)Math.Sqrt(DistanceSquared(point, a));
            float t = ((point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y)) / length;
            t = Math.Max(0, Math.Min(1, t));
            return (float)Math.Sqrt(DistanceSquared(point, new PointF(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y))));
        }

        private void UpdateRender()
        {
            Bitmap next = RenderDocument();
            if (_rendered != null) _rendered.Dispose();
            _rendered = next;
            _sizeLabel.Text = _state.Image.Width + " × " + _state.Image.Height + " px  ·  원본 해상도";
            _undoButton.Enabled = _undo.Count > 0;
            _redoButton.Enabled = _redo.Count > 0;
            _canvas.Invalidate();
        }

        private Bitmap RenderDocument()
        {
            Bitmap result = CopyBitmap(_state.Image);
            try
            {
                foreach (Mark mark in _state.Marks)
                {
                    if (mark.Tool == Tool.Mosaic) ApplyMosaic(result, PixelRegion(Normalized(mark.Start, mark.End), result.Size), Math.Max(5, (int)mark.Width * 3));
                    else if (mark.Tool == Tool.Blur) ApplyBlur(result, PixelRegion(Normalized(mark.Start, mark.End), result.Size), Math.Max(3, (int)mark.Width * 2));
                    else
                    {
                        using (Graphics g = Graphics.FromImage(result))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            DrawMark(g, mark);
                        }
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static void DrawMark(Graphics g, Mark mark)
        {
            RectangleF rect = Normalized(mark.Start, mark.End);
            Color color = mark.Tool == Tool.Highlight ? Color.FromArgb(95, mark.Color) : mark.Color;
            float width = mark.Tool == Tool.Highlight ? Math.Max(12, mark.Width * 4) : mark.Width;
            using (Pen pen = new Pen(color, width))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (mark.Tool)
                {
                    case Tool.Rectangle:
                        if (rect.Width > 0 && rect.Height > 0) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                        break;
                    case Tool.Ellipse:
                        if (rect.Width > 0 && rect.Height > 0) g.DrawEllipse(pen, rect);
                        break;
                    case Tool.Line:
                        g.DrawLine(pen, mark.Start, mark.End);
                        break;
                    case Tool.Arrow:
                        double length = Math.Sqrt(DistanceSquared(mark.Start, mark.End));
                        if (length < 1) break;
                        float head = Math.Min((float)length * 0.55F, Math.Max(12, mark.Width * 4));
                        float dx = (mark.End.X - mark.Start.X) / (float)length;
                        float dy = (mark.End.Y - mark.Start.Y) / (float)length;
                        PointF neck = new PointF(mark.End.X - dx * head * 0.72F, mark.End.Y - dy * head * 0.72F);
                        g.DrawLine(pen, mark.Start, neck);
                        PointF a = new PointF(mark.End.X - dx * head - dy * head * 0.48F, mark.End.Y - dy * head + dx * head * 0.48F);
                        PointF b = new PointF(mark.End.X - dx * head + dy * head * 0.48F, mark.End.Y - dy * head - dx * head * 0.48F);
                        g.FillPolygon(brush, new PointF[] { mark.End, a, neck, b });
                        break;
                    case Tool.Pen:
                    case Tool.Highlight:
                        if (mark.Points.Count > 1)
                        {
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                path.AddLines(mark.Points.ToArray());
                                g.DrawPath(pen, path);
                            }
                        }
                        else if (mark.Points.Count == 1) g.FillEllipse(brush, mark.Start.X - width / 2, mark.Start.Y - width / 2, width, width);
                        break;
                    case Tool.Text:
                        using (Font font = new Font("Malgun Gothic", mark.TextSize, FontStyle.Bold, GraphicsUnit.Pixel))
                            g.DrawString(mark.Text, font, brush, mark.Start);
                        break;
                    case Tool.Number:
                        float radius = Math.Max(16, mark.TextSize * 0.7F);
                        RectangleF circle = new RectangleF(mark.Start.X - radius, mark.Start.Y - radius, radius * 2, radius * 2);
                        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(65, 0, 0, 0))) g.FillEllipse(shadow, circle.X + 1, circle.Y + 2, circle.Width, circle.Height);
                        g.FillEllipse(brush, circle);
                        using (Pen border = new Pen(Color.FromArgb(215, Color.White), 2)) g.DrawEllipse(border, circle);
                        using (Font font = new Font("Segoe UI", radius * (mark.Number >= 100 ? 0.83F : 1.05F), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush foreground = new SolidBrush(mark.Color.GetBrightness() > 0.66F ? Color.FromArgb(23, 28, 38) : Color.White))
                        using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(mark.Number.ToString(), font, foreground, circle, format);
                        break;
                }
            }
        }

        private static RectangleF Normalized(PointF start, PointF end)
        {
            return new RectangleF(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        }

        private static float DistanceSquared(PointF a, PointF b) { float x = a.X - b.X; float y = a.Y - b.Y; return x * x + y * y; }

        private static Rectangle PixelRegion(RectangleF rect, Size bounds)
        {
            int left = Math.Max(0, (int)Math.Floor(rect.Left));
            int top = Math.Max(0, (int)Math.Floor(rect.Top));
            int right = Math.Min(bounds.Width, (int)Math.Ceiling(rect.Right));
            int bottom = Math.Min(bounds.Height, (int)Math.Ceiling(rect.Bottom));
            return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        internal static Bitmap CopyBitmap(Bitmap image)
        {
            Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(copy))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            }
            return copy;
        }

        /// <summary>Average-color pixelation, applied only inside the clipped region.</summary>
        internal static void ApplyMosaic(Bitmap image, Rectangle region, int blockSize)
        {
            if (image == null) throw new ArgumentNullException("image");
            region = Rectangle.Intersect(new Rectangle(Point.Empty, image.Size), region);
            if (region.Width <= 0 || region.Height <= 0) return;
            blockSize = Math.Max(2, Math.Min(256, blockSize));
            BitmapData data = image.LockBits(region, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = region.Width * 4;
                byte[] pixels = new byte[rowBytes * region.Height];
                for (int y = 0; y < region.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * rowBytes, rowBytes);
                for (int by = 0; by < region.Height; by += blockSize)
                {
                    for (int bx = 0; bx < region.Width; bx += blockSize)
                    {
                        int right = Math.Min(region.Width, bx + blockSize);
                        int bottom = Math.Min(region.Height, by + blockSize);
                        long alpha = 0, red = 0, green = 0, blue = 0;
                        int count = (right - bx) * (bottom - by);
                        for (int y = by; y < bottom; y++)
                            for (int x = bx; x < right; x++)
                            {
                                int p = y * rowBytes + x * 4;
                                blue += pixels[p]; green += pixels[p + 1]; red += pixels[p + 2]; alpha += pixels[p + 3];
                            }
                        byte b = (byte)(blue / count), g = (byte)(green / count), r = (byte)(red / count), a = (byte)(alpha / count);
                        for (int y = by; y < bottom; y++)
                            for (int x = bx; x < right; x++)
                            {
                                int p = y * rowBytes + x * 4;
                                pixels[p] = b; pixels[p + 1] = g; pixels[p + 2] = r; pixels[p + 3] = a;
                            }
                    }
                }
                for (int y = 0; y < region.Height; y++) Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
            }
            finally { image.UnlockBits(data); }
        }

        /// <summary>Three separable box passes approximate a Gaussian in O(width*height), with region-local edges.</summary>
        internal static void ApplyBlur(Bitmap image, Rectangle region, int radius)
        {
            if (image == null) throw new ArgumentNullException("image");
            region = Rectangle.Intersect(new Rectangle(Point.Empty, image.Size), region);
            if (region.Width <= 0 || region.Height <= 0) return;
            radius = Math.Max(1, Math.Min(80, radius));
            BitmapData data = image.LockBits(region, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int width = region.Width, height = region.Height, stride = width * 4;
                byte[] pixels = new byte[stride * height];
                byte[] temp = new byte[pixels.Length];
                for (int y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * stride, stride);
                for (int pass = 0; pass < 3; pass++)
                {
                    BoxPass(pixels, temp, width, height, radius, true);
                    BoxPass(temp, pixels, width, height, radius, false);
                }
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, IntPtr.Add(data.Scan0, y * data.Stride), stride);
            }
            finally { image.UnlockBits(data); }
        }

        private static void BoxPass(byte[] source, byte[] destination, int width, int height, int radius, bool horizontal)
        {
            int lineCount = horizontal ? height : width;
            int lineLength = horizontal ? width : height;
            int step = horizontal ? 4 : width * 4;
            int diameter = radius * 2 + 1;
            for (int line = 0; line < lineCount; line++)
            {
                int origin = horizontal ? line * width * 4 : line * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += source[origin + Math.Max(0, Math.Min(lineLength - 1, k)) * step + channel];
                    for (int i = 0; i < lineLength; i++)
                    {
                        destination[origin + i * step + channel] = (byte)(sum / diameter);
                        int remove = Math.Max(0, i - radius);
                        int add = Math.Min(lineLength - 1, i + radius + 1);
                        sum += source[origin + add * step + channel] - source[origin + remove * step + channel];
                    }
                }
            }
        }

        private string PromptText()
        {
            using (Form dialog = new Form())
            using (TextBox input = new TextBox())
            using (Font inputFont = new Font("Malgun Gothic", 12F))
            {
                dialog.Text = "글자 넣기";
                dialog.Font = Font;
                dialog.BackColor = _surface;
                dialog.ForeColor = _text;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(430, 239);
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                Label description = new Label { Text = "이미지에 표시할 내용을 입력하세요", AutoSize = true, Location = new Point(17, 15), ForeColor = _muted };
                input.Multiline = true;
                input.AcceptsReturn = true;
                input.ScrollBars = ScrollBars.Vertical;
                input.SetBounds(18, 44, 393, 129);
                input.BackColor = _background;
                input.ForeColor = _text;
                input.BorderStyle = BorderStyle.FixedSingle;
                input.Font = inputFont;
                input.MaxLength = 10000;
                Button ok = MakeButton("넣기", 92, 34);
                ok.Location = new Point(319, 188);
                ok.DialogResult = DialogResult.OK;
                ok.BackColor = Color.FromArgb(34, 72, 63);
                Button cancel = MakeButton("취소", 92, 34);
                cancel.Location = new Point(218, 188);
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.AddRange(new Control[] { description, input, ok, cancel });
                dialog.CancelButton = cancel;
                dialog.KeyPreview = true;
                dialog.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Enter) { dialog.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; } };
                dialog.Shown += delegate { input.Focus(); };
                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.TrimEnd() : null;
            }
        }

        private void CopyImage()
        {
            CancelGesture();
            try
            {
                Clipboard.SetImage(_rendered);
                NotifyCommitted();
                SetStatus("클립보드에 복사했습니다 · " + _rendered.Width + " × " + _rendered.Height + " px");
            }
            catch (Exception ex) { ShowActionError("이미지 복사", ex); }
        }

        private void SaveImage()
        {
            CancelGesture();
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "캡처 저장";
                dialog.Filter = "PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg)|*.jpg|비트맵 이미지 (*.bmp)|*.bmp";
                dialog.DefaultExt = "png";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = "Chacha_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (!String.IsNullOrWhiteSpace(SaveDirectory))
                {
                    try
                    {
                        string directory = Path.GetFullPath(SaveDirectory);
                        Directory.CreateDirectory(directory);
                        dialog.InitialDirectory = directory;
                    }
                    catch (ArgumentException) { SetStatus("저장 폴더를 열 수 없어 사진 폴더를 표시합니다"); }
                    catch (NotSupportedException) { SetStatus("저장 폴더를 열 수 없어 사진 폴더를 표시합니다"); }
                    catch (IOException) { SetStatus("저장 폴더를 열 수 없어 사진 폴더를 표시합니다"); }
                    catch (UnauthorizedAccessException) { SetStatus("저장 폴더에 접근할 수 없어 사진 폴더를 표시합니다"); }
                    catch (System.Security.SecurityException) { SetStatus("저장 폴더에 접근할 수 없어 사진 폴더를 표시합니다"); }
                }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    ImageFormat format = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png;
                    using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        if (format.Guid == ImageFormat.Jpeg.Guid)
                        {
                            using (Bitmap opaque = new Bitmap(_rendered.Width, _rendered.Height, PixelFormat.Format24bppRgb))
                            {
                                using (Graphics g = Graphics.FromImage(opaque)) { g.Clear(Color.White); g.DrawImageUnscaled(_rendered, 0, 0); }
                                ImageCodecInfo encoder = null;
                                foreach (ImageCodecInfo candidate in ImageCodecInfo.GetImageEncoders()) if (candidate.FormatID == ImageFormat.Jpeg.Guid) encoder = candidate;
                                if (encoder != null)
                                {
                                    using (EncoderParameters parameters = new EncoderParameters(1))
                                    {
                                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                                        opaque.Save(stream, encoder, parameters);
                                    }
                                }
                                else opaque.Save(stream, format);
                            }
                        }
                        else _rendered.Save(stream, format);
                    }
                    NotifyCommitted();
                    SetStatus("저장 완료 · " + dialog.FileName);
                }
                catch (Exception ex) { ShowActionError("이미지 저장", ex); }
            }
        }

        private void PinImage()
        {
            CancelGesture();
            try
            {
                Action<Bitmap> handler = PinRequested;
                if (handler == null) { SetStatus("이미지 고정 기능이 연결되지 않았습니다"); return; }
                Bitmap image = CopyBitmap(_rendered);
                try { handler(image); } catch { image.Dispose(); throw; }
                NotifyCommitted();
                SetStatus("이미지를 화면 위에 고정했습니다 · 고정 창은 드래그로 이동할 수 있습니다");
            }
            catch (Exception ex) { ShowActionError("이미지 고정", ex); }
        }

        private void NotifyCommitted()
        {
            Action<Bitmap> handler = ImageCommitted;
            if (handler == null) return;
            Bitmap image = CopyBitmap(_rendered);
            try { handler(image); } catch { image.Dispose(); throw; }
        }

        private void ShowActionError(string action, Exception exception)
        {
            SetStatus(action + "에 실패했습니다");
            MessageBox.Show(this, action + " 중 문제가 발생했습니다.\r\n\r\n" + exception.Message, "Chacha", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SetStatus(string text) { if (_status != null) _status.Text = text; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C)) { CopyImage(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveImage(); return true; }
            if (keyData == (Keys.Control | Keys.P) || keyData == Keys.F3) { PinImage(); return true; }
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { FitImage(); return true; }
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1)) { ZoomAt(1F, new Point(_canvas.Width / 2, _canvas.Height / 2)); return true; }
            if (keyData == Keys.Escape)
            {
                if (_drawing || _panning) CancelGesture();
                else SetTool(Tool.Move);
                return true;
            }
            if (keyData == Keys.Space)
            {
                _spaceHeld = true;
                UpdateCursor();
                return true;
            }
            if (ActiveControl is NumericUpDown) return base.ProcessCmdKey(ref msg, keyData);
            switch (keyData)
            {
                case Keys.V: SetTool(Tool.Move); return true;
                case Keys.R: SetTool(Tool.Rectangle); return true;
                case Keys.E: SetTool(Tool.Ellipse); return true;
                case Keys.A: SetTool(Tool.Arrow); return true;
                case Keys.L: SetTool(Tool.Line); return true;
                case Keys.B: SetTool(Tool.Pen); return true;
                case Keys.H: SetTool(Tool.Highlight); return true;
                case Keys.T: SetTool(Tool.Text); return true;
                case Keys.N: SetTool(Tool.Number); return true;
                case Keys.M: SetTool(Tool.Mosaic); return true;
                case Keys.U: SetTool(Tool.Blur); return true;
                case Keys.C: SetTool(Tool.Crop); return true;
                case Keys.X: SetTool(Tool.Eraser); return true;
                case Keys.Add:
                case Keys.Oemplus: ZoomAt(_zoom * 1.25F, new Point(_canvas.Width / 2, _canvas.Height / 2)); return true;
                case Keys.Subtract:
                case Keys.OemMinus: ZoomAt(_zoom / 1.25F, new Point(_canvas.Width / 2, _canvas.Height / 2)); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { _spaceHeld = true; UpdateCursor(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { _spaceHeld = false; UpdateCursor(); e.Handled = true; }
            base.OnKeyUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_rendered != null) { _rendered.Dispose(); _rendered = null; }
                foreach (Bitmap image in _ownedImages) image.Dispose();
                _ownedImages.Clear();
                _undo.Clear();
                _redo.Clear();
                _tips.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font font in _ownedFonts) font.Dispose();
                _ownedFonts.Clear();
            }
        }
    }
}
