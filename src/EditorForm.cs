using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
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
        public event Action CloseRequested;
        public string SaveDirectory { get; set; }
        public string QuickSaveDirectory { get; set; }
        public bool IsInline { get; private set; }
        public bool IsPinEditing { get; private set; }
        public bool HasChanges
        {
            get
            {
                return _state != null && _initialState != null &&
                    (_state.Marks.Count != 0 || !Object.ReferenceEquals(_state.Image, _initialState.Image) || _state.Origin != _initialState.Origin);
            }
        }
        public Rectangle CurrentScreenBounds
        {
            get
            {
                if (IsInline)
                    return new Rectangle(_desktopBounds.X + (int)Math.Round(_state.Origin.X), _desktopBounds.Y + (int)Math.Round(_state.Origin.Y), _state.Image.Width, _state.Image.Height);
                Point origin = IsPinEditing ? Point.Round(_state.Origin) : _canvas.PointToScreen(Point.Round(ImageOrigin));
                return new Rectangle(origin, new Size(Math.Max(1, (int)Math.Round(_state.Image.Width * _zoom)), Math.Max(1, (int)Math.Round(_state.Image.Height * ZoomY))));
            }
        }
        public bool WhiteboardMode
        {
            get { return _whiteboardMode; }
            set
            {
                _whiteboardMode = value;
                _barsVisible = !value;
                if (_inlineBar != null) _inlineBar.Visible = _barsVisible;
                else if (_toolsPanel != null) _toolsPanel.Visible = _barsVisible;
            }
        }

        private enum Tool { Move, Rectangle, Ellipse, Arrow, Line, Pen, Highlight, Text, Number, Mosaic, Blur, Crop, Eraser, Polyline }

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
            internal bool Filled;
            internal string FontFamily = "Malgun Gothic";
            internal FontStyle FontStyle = FontStyle.Regular;
            internal float Rotation;
            internal List<PointF> Points = new List<PointF>();
            internal Mark Clone()
            {
                Mark copy = (Mark)MemberwiseClone();
                copy.Points = new List<PointF>(Points);
                return copy;
            }
        }

        private sealed class DocumentState
        {
            internal Bitmap Image;
            internal List<Mark> Marks;
            internal int NextNumber;
            internal PointF Origin;
            internal DocumentState(Bitmap image) { Image = image; Marks = new List<Mark>(); NextNumber = 1; }
            internal DocumentState Snapshot()
            {
                DocumentState copy = new DocumentState(Image);
                copy.Marks = new List<Mark>(Marks);
                copy.NextNumber = NextNumber;
                copy.Origin = Origin;
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
        private DocumentState _initialState;
        private Bitmap _rendered;
        private Mark _draft;
        private Tool _tool = Tool.Arrow;
        private Color _color = Color.FromArgb(255, 83, 100);
        private float _lineWidth = 4;
        private float _textSize = 28;
        private string _fontFamily = "Malgun Gothic";
        private FontStyle _fontStyle = FontStyle.Regular;
        private bool _filled;
        private int _opacity = 255;
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
        private Panel _header;
        private Panel _toolsPanel;
        private Panel _footer;
        private FlowLayoutPanel _toolRow;
        private FlowLayoutPanel _optionsRow;
        private FlowLayoutPanel _styleRow;
        private Panel _inlineBar;
        private Bitmap _desktop;
        private Rectangle _desktopBounds;
        private bool _barsVisible = true;
        private int _selectedIndex = -1;
        private DocumentState _transformBefore;
        private Mark _transformOriginal;
        private PointF _transformStart;
        private RectangleF _transformBounds;
        private int _transformHandle = -1;
        private bool _transformChanged;
        private NumericUpDown _widthControl;
        private NumericUpDown _fontSizeControl;
        private NumericUpDown _opacityControl;
        private Button _fillButton;
        private Button _boldButton;
        private Button _italicButton;
        private bool _syncingOptions;
        private float _inlineScale = 1F;
        private float _pinZoomY = 1F;
        private bool _positioningPin;
        private bool _whiteboardMode;
        private bool HasFloatingBars { get { return IsInline || IsPinEditing; } }
        private float ZoomY { get { return IsPinEditing ? _pinZoomY : _zoom; } }

        public EditorForm(Bitmap image)
        {
            if (image == null) throw new ArgumentNullException("image");
            SuspendLayout();
            _state = new DocumentState(CopyBitmap(image));
            _initialState = _state.Snapshot();
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

        public EditorForm(Bitmap image, Bitmap desktop, Rectangle desktopBounds, Rectangle selectionBounds) : this(image)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            if (desktopBounds.Width <= 0 || desktopBounds.Height <= 0) throw new ArgumentException("Desktop bounds must be positive.", "desktopBounds");
            SuspendLayout();
            IsInline = true;
            _desktop = CopyBitmap(desktop);
            _desktopBounds = desktopBounds;
            _state.Origin = new PointF(selectionBounds.X - desktopBounds.X, selectionBounds.Y - desktopBounds.Y);
            _initialState.Origin = _state.Origin;
            _zoom = 1F;
            _fit = false;
            _pan = PointF.Empty;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = Size.Empty;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = desktopBounds;
            ConfigureInlineBars();
            ResumeLayout(true);
            PositionInlineBars();
            SetTool(Tool.Move);
            Shown += delegate { Bounds = _desktopBounds; PositionInlineBars(); Activate(); _canvas.Focus(); };
        }

        /// <summary>Edit a pin at its exact displayed image rectangle; transparent window regions leave the desktop accessible.</summary>
        public EditorForm(Bitmap image, Rectangle imageScreenBounds) : this(image)
        {
            if (imageScreenBounds.Width <= 0 || imageScreenBounds.Height <= 0) throw new ArgumentException("Image bounds must be positive.", "imageScreenBounds");
            SuspendLayout();
            IsPinEditing = true;
            _zoom = (float)imageScreenBounds.Width / image.Width;
            _pinZoomY = (float)imageScreenBounds.Height / image.Height;
            _fit = false; _pan = PointF.Empty;
            // Pin history stores absolute origins so the floating window can change its bounds without moving content.
            _state.Origin = imageScreenBounds.Location; _initialState.Origin = _state.Origin;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = Size.Empty;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true;
            Bounds = imageScreenBounds;
            ConfigureInlineBars();
            ResumeLayout(true);
            PositionPinnedBars();
            SetTool(Tool.Move);
            Shown += delegate { PositionPinnedBars(); Activate(); _canvas.Focus(); };
        }

        public void SelectTool(string name)
        {
            Tool tool;
            if (!String.IsNullOrEmpty(name) && Enum.TryParse<Tool>(name, true, out tool)) SetTool(tool);
        }

        public Bitmap ExportImage() { return CopyBitmap(_rendered); }

        public void CommitChanges()
        {
            FinishPolyline(); CancelGesture();
            try { NotifyCommitted(); CloseInline(); }
            catch (Exception ex) { ShowActionError("편집 적용", ex); }
        }

        private void BuildInterface()
        {
            Panel header = _header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = _surface, Padding = new Padding(16, 8, 12, 8) };
            Label title = new Label { Text = "CHACHA", AutoSize = true, Location = new Point(18, 10), ForeColor = _accent, Font = OwnFont("Segoe UI", 15F, FontStyle.Bold) };
            _sizeLabel = new Label { AutoSize = true, Location = new Point(19, 39), ForeColor = _muted, Font = OwnFont("Segoe UI", 8.5F, FontStyle.Regular) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 445, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = _surface, Padding = new Padding(0, 6, 0, 0) };
            Button copy = MakeButton("복사  Ctrl+C", 135, 36);
            copy.Click += delegate { CopyImage(); };
            Button save = MakeButton("저장  Ctrl+S", 135, 36);
            save.Click += delegate { SaveImage(); };
            Button pin = MakeButton("고정  Ctrl+T", 135, 36);
            pin.BackColor = _accent;
            pin.ForeColor = Color.FromArgb(15, 20, 34);
            pin.Click += delegate { PinImage(); };
            actions.Controls.AddRange(new Control[] { copy, save, pin });
            header.Controls.Add(actions);
            header.Controls.Add(title);
            header.Controls.Add(_sizeLabel);

            Panel tools = _toolsPanel = new Panel { Dock = DockStyle.Top, Height = 128, BackColor = _surface };
            FlowLayoutPanel toolRow = _toolRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, WrapContents = false, AutoScroll = true, Padding = new Padding(4, 2, 0, 0), BackColor = _surface };
            AddTool(toolRow, Tool.Move, "선택", "V · 주석 선택/이동/크기 변경 · 가운데 버튼: 캔버스 이동");
            AddTool(toolRow, Tool.Rectangle, "사각", "R · 사각형 (Shift: 정사각형)");
            AddTool(toolRow, Tool.Ellipse, "원", "E · 타원 (Shift: 원)");
            AddTool(toolRow, Tool.Arrow, "화살표", "A · 화살표 (Shift: 45도 단위)");
            AddTool(toolRow, Tool.Line, "선", "L · 직선 (Shift: 45도 단위)");
            AddTool(toolRow, Tool.Polyline, "꺾은선", "P · 클릭해서 연결 · 우클릭/Enter로 완료");
            AddTool(toolRow, Tool.Pen, "펜", "B · 자유롭게 그리기");
            AddTool(toolRow, Tool.Highlight, "형광펜", "H · 반투명 형광펜");
            AddTool(toolRow, Tool.Text, "문자", "T · 클릭해서 글자 입력");
            AddTool(toolRow, Tool.Number, "번호", "N · 순서 번호 표시");
            AddTool(toolRow, Tool.Mosaic, "모자이크", "M · 영역을 드래그해 모자이크 처리");
            AddTool(toolRow, Tool.Blur, "흐림", "U · 영역을 드래그해 흐림 처리");
            AddTool(toolRow, Tool.Crop, "자르기", "C · 드래그한 영역으로 이미지 자르기");
            AddTool(toolRow, Tool.Eraser, "지우기", "X · 클릭한 주석 삭제 (이미지 픽셀은 그대로 유지)");
            FlowLayoutPanel options = _optionsRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 3, 5, 0), BackColor = _surface };
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
            NumericUpDown width = _widthControl = new NumericUpDown { Minimum = 1, Maximum = 40, Value = 4, Width = 47, Height = 28, Margin = new Padding(2, 2, 5, 0), BackColor = _background, ForeColor = _text, BorderStyle = BorderStyle.FixedSingle };
            width.ValueChanged += delegate { _lineWidth = (float)width.Value; ApplySelectedStyle(); _canvas.Focus(); };
            Label fontLabel = new Label { Text = "글자", AutoSize = true, Margin = new Padding(3, 6, 2, 0), ForeColor = _muted };
            NumericUpDown fontSize = _fontSizeControl = new NumericUpDown { Minimum = 8, Maximum = 300, Value = 28, Increment = 2, Width = 51, Height = 28, Margin = new Padding(2, 2, 8, 0), BackColor = _background, ForeColor = _text, BorderStyle = BorderStyle.FixedSingle };
            fontSize.ValueChanged += delegate { _textSize = (float)fontSize.Value; ApplySelectedStyle(); _canvas.Focus(); };
            options.Controls.AddRange(new Control[] { widthLabel, width, fontLabel, fontSize });
            _undoButton = MakeButton("실행 취소", 77, 28);
            _redoButton = MakeButton("다시 실행", 77, 28);
            _tips.SetToolTip(_undoButton, "Ctrl+Z");
            _tips.SetToolTip(_redoButton, "Ctrl+Y");
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
            FlowLayoutPanel styleRow = _styleRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 2, 5, 0), BackColor = _surface };
            _fillButton = MakeButton("채움 꺼짐", 78, 28);
            _fillButton.Click += delegate { _filled = !_filled; UpdateStyleButtons(); ApplySelectedStyle(); };
            _tips.SetToolTip(_fillButton, "사각형/타원을 단색으로 채웁니다");
            Button font = MakeButton("서체", 52, 28);
            font.Click += delegate { ChooseFont(); };
            _boldButton = MakeButton("B", 30, 28);
            _boldButton.Click += delegate { _fontStyle ^= FontStyle.Bold; UpdateStyleButtons(); ApplySelectedStyle(); };
            _tips.SetToolTip(_boldButton, "글자 굵게");
            _italicButton = MakeButton("I", 30, 28);
            _italicButton.Click += delegate { _fontStyle ^= FontStyle.Italic; UpdateStyleButtons(); ApplySelectedStyle(); };
            _tips.SetToolTip(_italicButton, "글자 기울임");
            Label opacity = new Label { Text = "불투명도", AutoSize = true, Margin = new Padding(8, 6, 2, 0), ForeColor = _muted };
            _opacityControl = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 100, Width = 48, BackColor = _background, ForeColor = _text, Margin = new Padding(2, 2, 3, 0) };
            _opacityControl.ValueChanged += delegate { _opacity = (int)Math.Round((double)_opacityControl.Value * 255 / 100); _color = Color.FromArgb(_opacity, _color); ApplySelectedStyle(); _canvas.Focus(); };
            Button print = MakeButton("인쇄", 52, 28); print.Click += delegate { PrintImage(); }; _tips.SetToolTip(print, "Ctrl+P");
            Button quick = MakeButton("빠른 저장", 76, 28); quick.Click += delegate { QuickSaveImage(); }; _tips.SetToolTip(quick, "Ctrl+Shift+S");
            Button clear = MakeButton("편집 지우기", 86, 28); clear.Click += delegate { ClearEdits(); }; _tips.SetToolTip(clear, "Ctrl+Shift+Z · 모든 주석 지우기 (되돌릴 수 없음)");
            styleRow.Controls.AddRange(new Control[] { _fillButton, font, _boldButton, _italicButton, opacity, _opacityControl, print, quick, clear });
            tools.Controls.Add(styleRow);
            tools.Controls.Add(options);
            tools.Controls.Add(toolRow);

            Panel footer = _footer = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = _surface, Padding = new Padding(14, 0, 8, 0) };
            _status = new Label { Dock = DockStyle.Fill, ForeColor = _muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            footer.Controls.Add(_status);
            _canvas.Dock = DockStyle.Fill;
            _canvas.BackColor = _background;
            _canvas.Paint += PaintCanvas;
            _canvas.MouseDown += CanvasMouseDown;
            _canvas.MouseMove += CanvasMouseMove;
            _canvas.MouseUp += CanvasMouseUp;
            _canvas.MouseDoubleClick += CanvasDoubleClick;
            _canvas.MouseWheel += CanvasMouseWheel;
            _canvas.MouseCaptureChanged += delegate { if (!_canvas.Capture && (_drawing || _panning || _transformBefore != null)) CancelGesture(); };
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

        private void ConfigureInlineBars()
        {
            using (Graphics graphics = CreateGraphics()) _inlineScale = Math.Max(1F, graphics.DpiX / 96F);
            _header.Visible = false;
            _toolsPanel.Visible = false;
            _footer.Visible = false;
            _inlineBar = new Panel { BackColor = _surface, Size = new Size(InlinePixels(548), InlinePixels(149)), Padding = new Padding(InlinePixels(2)), BorderStyle = BorderStyle.FixedSingle, AutoScroll = true };
            string[] glyphs = { "↖", "□", "○", "➜", "╱", "✎", "▰", "T", "①", "▦", "◉", "⌗", "⌫", "⌁" };
            foreach (KeyValuePair<Tool, Button> pair in _toolButtons)
            {
                pair.Value.Text = glyphs[(int)pair.Key];
                pair.Value.Width = InlinePixels(33);
                pair.Value.Height = InlinePixels(29);
                pair.Value.Margin = new Padding(InlinePixels(2), InlinePixels(1), InlinePixels(2), InlinePixels(1));
                pair.Value.Font = OwnFont("Segoe UI Symbol", 12F, FontStyle.Regular);
            }
            _toolRow.Parent = _inlineBar; _toolRow.Dock = DockStyle.None; _toolRow.SetBounds(InlinePixels(3), InlinePixels(3), InlinePixels(538), InlinePixels(34)); _toolRow.Padding = new Padding(InlinePixels(4), InlinePixels(1), 0, 0);
            _optionsRow.Parent = _inlineBar; _optionsRow.Dock = DockStyle.None; _optionsRow.SetBounds(InlinePixels(3), InlinePixels(38), InlinePixels(538), InlinePixels(33)); _optionsRow.Padding = new Padding(InlinePixels(4), InlinePixels(1), 0, 0);
            bool afterUndo = false;
            foreach (Control control in _optionsRow.Controls)
            {
                if (control == _undoButton) afterUndo = true;
                if (afterUndo) control.Visible = false;
            }
            _styleRow.Parent = _inlineBar; _styleRow.Dock = DockStyle.None; _styleRow.SetBounds(InlinePixels(3), InlinePixels(73), InlinePixels(538), InlinePixels(33)); _styleRow.Padding = new Padding(InlinePixels(4), InlinePixels(1), 0, 0);
            foreach (Control control in _styleRow.Controls)
                if (control.Text == "인쇄" || control.Text == "빠른 저장" || control.Text == "편집 지우기") control.Visible = false;
            FlowLayoutPanel actions = new FlowLayoutPanel { Location = new Point(InlinePixels(4), InlinePixels(110)), Size = new Size(InlinePixels(536), InlinePixels(33)), WrapContents = false, BackColor = _surface };
            _undoButton = MakeButton("↶", 32, 28); _undoButton.Enabled = _undo.Count > 0; _undoButton.Click += delegate { Undo(); }; _tips.SetToolTip(_undoButton, "Ctrl+Z · 실행 취소");
            _redoButton = MakeButton("↷", 32, 28); _redoButton.Enabled = _redo.Count > 0; _redoButton.Click += delegate { Redo(); }; _tips.SetToolTip(_redoButton, "Ctrl+Y · 다시 실행");
            actions.Controls.AddRange(new Control[] { _undoButton, _redoButton });
            Button clear = MakeButton("지우기", 54, 28); clear.Click += delegate { ClearEdits(); }; _tips.SetToolTip(clear, "Ctrl+Shift+Z · 모든 편집 지우기");
            Button print = MakeButton("인쇄", 47, 28); print.Click += delegate { PrintImage(); }; _tips.SetToolTip(print, "Ctrl+P");
            Button quick = MakeButton("빠른 저장", 72, 28); quick.Click += delegate { QuickSaveImage(); }; _tips.SetToolTip(quick, "Ctrl+Shift+S");
            Button save = MakeButton("저장", 50, 28); save.Click += delegate { SaveImage(); }; _tips.SetToolTip(save, "Ctrl+S");
            Button pin = MakeButton(IsPinEditing ? "복사" : "고정", 50, 28); pin.Click += delegate { if (IsPinEditing) CopyImage(); else PinImage(); }; _tips.SetToolTip(pin, IsPinEditing ? "Ctrl+C · 이미지 복사" : "Ctrl+T · 화면에 고정하고 완료");
            Button copy = MakeButton(IsPinEditing ? "완료 ✓" : "복사 ✓", 66, 28); copy.BackColor = _accent; copy.ForeColor = _background; copy.Click += delegate { if (IsPinEditing) CommitChanges(); else CopyImage(); }; _tips.SetToolTip(copy, IsPinEditing ? "Enter / Space / Esc · 편집 적용" : "Enter / Ctrl+C · 복사하고 완료");
            Button close = MakeButton("×", 30, 28); close.Click += delegate { if (IsPinEditing) CommitChanges(); else CloseInline(); }; _tips.SetToolTip(close, IsPinEditing ? "편집을 적용하고 도구막대 닫기" : "Esc · 취소");
            actions.Controls.AddRange(new Control[] { clear, print, quick, save, pin, copy, close });
            if (_inlineScale != 1F) foreach (Control control in actions.Controls) control.Scale(new SizeF(_inlineScale, _inlineScale));
            _inlineBar.Controls.Add(actions);
            _canvas.Controls.Add(_inlineBar);
            _inlineBar.BringToFront();
            _canvas.Dock = DockStyle.Fill;
        }

        private int InlinePixels(int value) { return Math.Max(1, (int)Math.Round(value * _inlineScale)); }

        private void PositionInlineBars()
        {
            if (IsPinEditing) { PositionPinnedBars(); return; }
            if (!IsInline || _inlineBar == null) return;
            Rectangle selection = new Rectangle((int)_state.Origin.X, (int)_state.Origin.Y, _state.Image.Width, _state.Image.Height);
            Rectangle monitor = Screen.FromRectangle(new Rectangle(selection.X + _desktopBounds.X, selection.Y + _desktopBounds.Y, selection.Width, selection.Height)).Bounds;
            if (!monitor.IntersectsWith(_desktopBounds)) monitor = _desktopBounds;
            monitor.Offset(-_desktopBounds.X, -_desktopBounds.Y);
            int width = Math.Min(InlinePixels(548), monitor.Width - InlinePixels(8));
            _inlineBar.Width = Math.Max(InlinePixels(320), width);
            int x = Math.Max(monitor.Left + 4, Math.Min(selection.Right - _inlineBar.Width, monitor.Right - _inlineBar.Width - 4));
            int y = selection.Bottom + 8;
            if (y + _inlineBar.Height > monitor.Bottom - 4) y = selection.Top - _inlineBar.Height - 8;
            if (y < monitor.Top + 4) y = Math.Max(monitor.Top + 4, monitor.Bottom - _inlineBar.Height - 8);
            _inlineBar.Location = new Point(x, y);
            _inlineBar.Visible = _barsVisible;
            _inlineBar.BringToFront();
        }

        private void PositionPinnedBars()
        {
            if (!IsPinEditing || _inlineBar == null || _positioningPin) return;
            _positioningPin = true;
            try
            {
                Rectangle image = new Rectangle((int)Math.Round(_state.Origin.X), (int)Math.Round(_state.Origin.Y), Math.Max(1, (int)Math.Round(_state.Image.Width * _zoom)), Math.Max(1, (int)Math.Round(_state.Image.Height * _pinZoomY)));
                Rectangle monitor = Screen.FromRectangle(image).WorkingArea;
                int barWidth = Math.Min(InlinePixels(548), monitor.Width - 8);
                _inlineBar.Width = Math.Max(320, barWidth);
                int x = Math.Max(monitor.Left + 4, Math.Min(image.Right - _inlineBar.Width, monitor.Right - _inlineBar.Width - 4));
                int y = image.Bottom + 8;
                if (y + _inlineBar.Height > monitor.Bottom - 4) y = image.Top - _inlineBar.Height - 8;
                if (y < monitor.Top + 4) y = Math.Max(monitor.Top + 4, monitor.Bottom - _inlineBar.Height - 8);
                Rectangle bar = new Rectangle(new Point(x, y), _inlineBar.Size);
                Rectangle frame = image; frame.Inflate(2, 2);
                Rectangle window = _barsVisible ? Rectangle.Union(frame, bar) : frame;
                if (Bounds != window) Bounds = window;
                _inlineBar.Location = new Point(bar.X - window.X, bar.Y - window.Y);
                _inlineBar.Visible = _barsVisible;
                _inlineBar.BringToFront();
                Rectangle imageRegion = frame; imageRegion.Offset(-window.X, -window.Y);
                Region region = new Region(imageRegion);
                if (_barsVisible) region.Union(new Rectangle(_inlineBar.Location, _inlineBar.Size));
                Region previous = Region;
                Region = region;
                if (previous != null) previous.Dispose();
            }
            finally { _positioningPin = false; }
        }

        private void ToggleBars()
        {
            _barsVisible = !_barsVisible;
            if (HasFloatingBars) { _inlineBar.Visible = _barsVisible; if (IsPinEditing) PositionPinnedBars(); }
            else _toolsPanel.Visible = _barsVisible;
            _canvas.Focus();
        }

        private void CloseInline()
        {
            Action handler = CloseRequested;
            if (handler != null) handler();
            if (!IsDisposed) Close();
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
            if (_draft != null && _draft.Tool == Tool.Polyline) FinishPolyline();
            CancelGesture();
            _selectedIndex = -1;
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
                case Tool.Move: hint = "주석 클릭: 선택/이동 · 핸들: 크기 조절 · 글자 더블클릭: 편집 · 가운데 버튼: 캔버스 이동"; break;
                case Tool.Text: hint = "이미지 위를 클릭하여 글자를 넣으세요 · 글자 크기는 원본 이미지의 픽셀 단위입니다"; break;
                case Tool.Number: hint = "클릭할 때마다 순서 번호를 표시합니다 · Ctrl+Z 실행 취소"; break;
                case Tool.Mosaic: hint = "가릴 영역을 드래그하세요 · 굵기가 클수록 모자이크 블록이 커집니다"; break;
                case Tool.Blur: hint = "흐리게 할 영역을 드래그하세요 · 굵기로 효과 강도를 조절합니다"; break;
                case Tool.Crop: hint = "드래그한 영역으로 자릅니다 · Ctrl+Z로 되돌리기 · Esc로 드래그 취소"; break;
                case Tool.Eraser: hint = "삭제할 주석을 클릭하세요 · 가장 위에 있는 주석부터 지워집니다"; break;
                case Tool.Polyline: hint = "클릭으로 점을 연결하세요 · 우클릭/Enter로 완료 · Esc로 취소"; break;
                default: hint = "드래그하여 그리기 · Shift로 각도/비율 고정 · Space: 도구막대 표시 · 가운데 버튼: 이동"; break;
            }
            SetStatus(hint);
            _canvas.Focus();
        }

        private void SetColor(Color color)
        {
            _color = Color.FromArgb(_opacity, color);
            _colorButton.BackColor = color;
            _colorButton.ForeColor = color.GetBrightness() > 0.6F ? Color.FromArgb(25, 28, 35) : Color.White;
            ApplySelectedStyle();
            _canvas.Focus();
        }

        private void UpdateStyleButtons()
        {
            _fillButton.Text = _filled ? "채움 켜짐" : "채움 꺼짐";
            _fillButton.BackColor = _filled ? Color.FromArgb(34, 72, 63) : Color.FromArgb(43, 49, 63);
            _boldButton.BackColor = (_fontStyle & FontStyle.Bold) != 0 ? Color.FromArgb(34, 72, 63) : Color.FromArgb(43, 49, 63);
            _italicButton.BackColor = (_fontStyle & FontStyle.Italic) != 0 ? Color.FromArgb(34, 72, 63) : Color.FromArgb(43, 49, 63);
        }

        private void ChooseFont()
        {
            using (FontDialog dialog = new FontDialog())
            using (Font initial = new Font(_fontFamily, _textSize, _fontStyle, GraphicsUnit.Point))
            {
                dialog.Font = initial;
                dialog.ShowEffects = false;
                dialog.MinSize = 8; dialog.MaxSize = 300;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _fontFamily = dialog.Font.FontFamily.Name;
                _fontStyle = dialog.Font.Style;
                _textSize = Math.Max(8, Math.Min(300, dialog.Font.Size));
                _syncingOptions = true;
                _fontSizeControl.Value = (decimal)_textSize;
                _syncingOptions = false;
                UpdateStyleButtons();
                ApplySelectedStyle();
            }
        }

        private void ApplySelectedStyle()
        {
            if (_syncingOptions || _selectedIndex < 0 || _selectedIndex >= _state.Marks.Count) return;
            Mark changed = _state.Marks[_selectedIndex].Clone();
            changed.Color = _color; changed.Width = _lineWidth; changed.TextSize = _textSize;
            changed.Filled = _filled; changed.FontFamily = _fontFamily; changed.FontStyle = _fontStyle;
            PushUndo();
            _state.Marks[_selectedIndex] = changed;
            UpdateRender();
        }

        private void SyncSelectedOptions()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _state.Marks.Count) return;
            Mark mark = _state.Marks[_selectedIndex];
            _syncingOptions = true;
            _color = mark.Color; _opacity = mark.Color.A; _lineWidth = mark.Width; _textSize = mark.TextSize;
            _filled = mark.Filled; _fontFamily = mark.FontFamily; _fontStyle = mark.FontStyle;
            _widthControl.Value = Math.Max(_widthControl.Minimum, Math.Min(_widthControl.Maximum, (decimal)mark.Width));
            _fontSizeControl.Value = Math.Max(_fontSizeControl.Minimum, Math.Min(_fontSizeControl.Maximum, (decimal)mark.TextSize));
            _opacityControl.Value = Math.Max(1, Math.Round(mark.Color.A * 100M / 255));
            _colorButton.BackColor = Color.FromArgb(255, mark.Color);
            UpdateStyleButtons();
            _syncingOptions = false;
        }

        private PointF ImageOrigin
        {
            get { if (IsPinEditing) return new PointF(_state.Origin.X - Left, _state.Origin.Y - Top); if (IsInline) return _state.Origin; return new PointF((_canvas.Width - _state.Image.Width * _zoom) / 2F + _pan.X, (_canvas.Height - _state.Image.Height * _zoom) / 2F + _pan.Y); }
        }

        private PointF ToImage(Point point, bool clamp)
        {
            PointF origin = ImageOrigin;
            float x = (point.X - origin.X) / _zoom;
            float y = (point.Y - origin.Y) / ZoomY;
            if (clamp) { x = Math.Max(0, Math.Min(_state.Image.Width, x)); y = Math.Max(0, Math.Min(_state.Image.Height, y)); }
            return new PointF(x, y);
        }

        private void FitImage()
        {
            if (HasFloatingBars) { if (IsInline) _zoom = 1; PositionInlineBars(); _canvas.Invalidate(); return; }
            if (_state == null || _canvas.Width < 2 || _canvas.Height < 2) return;
            _fit = true;
            _pan = PointF.Empty;
            _zoom = Math.Max(0.02F, Math.Min(1F, Math.Min((_canvas.Width - 48F) / _state.Image.Width, (_canvas.Height - 48F) / _state.Image.Height)));
            UpdateZoomLabel();
            _canvas.Invalidate();
        }

        private void ZoomAt(float requested, Point anchor)
        {
            if (HasFloatingBars) return;
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
            if (_tool != Tool.Move && (ModifierKeys & Keys.Control) == 0)
            {
                _widthControl.Value = Math.Max(_widthControl.Minimum, Math.Min(_widthControl.Maximum, _widthControl.Value + (e.Delta > 0 ? 1 : -1)));
                return;
            }
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
            if (IsInline && _desktop != null)
            {
                g.DrawImage(_desktop, new Rectangle(Point.Empty, _desktopBounds.Size), 0, 0, _desktop.Width, _desktop.Height, GraphicsUnit.Pixel);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0))) g.FillRectangle(dim, _canvas.ClientRectangle);
            }
            PointF origin = ImageOrigin;
            RectangleF imageBounds = new RectangleF(origin.X, origin.Y, _state.Image.Width * _zoom, _state.Image.Height * ZoomY);
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
            g.ScaleTransform(_zoom, ZoomY);
            g.InterpolationMode = _zoom >= 2F ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (_rendered != null) g.DrawImage(_rendered, new Rectangle(0, 0, _rendered.Width, _rendered.Height), 0, 0, _rendered.Width, _rendered.Height, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_draft != null && _draft.Tool != Tool.Crop && _draft.Tool != Tool.Mosaic && _draft.Tool != Tool.Blur) DrawMark(g, _draft);
            g.Restore(state);

            if (IsInline)
            {
                using (Pen outline = new Pen(_accent, 1F)) g.DrawRectangle(outline, imageBounds.X - 1, imageBounds.Y - 1, imageBounds.Width + 1, imageBounds.Height + 1);
                string info = _state.Image.Width + " × " + _state.Image.Height + "   ·   Enter 복사   Ctrl+T 고정   Space 도구";
                Size infoSize = TextRenderer.MeasureText(info, Font);
                Rectangle label = new Rectangle((int)imageBounds.Left, Math.Max(0, (int)imageBounds.Top - 26), infoSize.Width + 12, 23);
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(230, _surface))) g.FillRectangle(fill, label);
                TextRenderer.DrawText(g, info, Font, label, _text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            PaintSelection(g);

            if (_draft != null && (_draft.Tool == Tool.Crop || _draft.Tool == Tool.Mosaic || _draft.Tool == Tool.Blur))
            {
                RectangleF rect = Normalized(_draft.Start, _draft.End);
                RectangleF screenRect = new RectangleF(origin.X + rect.X * _zoom, origin.Y + rect.Y * ZoomY, rect.Width * _zoom, rect.Height * ZoomY);
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
            if (e.Button == MouseButtons.Right)
            {
                if (_draft != null && _draft.Tool == Tool.Polyline) FinishPolyline();
                else if (_drawing) CancelGesture();
                else { _selectedIndex = -1; SetTool(Tool.Move); }
                return;
            }
            if (e.Button == MouseButtons.Middle && !HasFloatingBars)
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
            if (_selectedIndex >= 0 && _selectedIndex < _state.Marks.Count)
            {
                int handle = HitSelectionHandle(point);
                if (handle >= 0) { BeginTransform(point, handle); return; }
            }
            if (point.X < 0 || point.Y < 0 || point.X >= _state.Image.Width || point.Y >= _state.Image.Height) return;
            if (_tool == Tool.Move)
            {
                _selectedIndex = HitAnnotation(point);
                if (_selectedIndex >= 0) { SyncSelectedOptions(); BeginTransform(point, -1); }
                _canvas.Invalidate();
                return;
            }
            if (_tool == Tool.Eraser) { EraseAt(point); return; }
            if (_tool == Tool.Text)
            {
                int existing = HitAnnotation(point);
                if (existing >= 0 && _state.Marks[existing].Tool == Tool.Text)
                {
                    _selectedIndex = existing; SyncSelectedOptions(); BeginTransform(point, -1); _canvas.Invalidate(); return;
                }
                string text = PromptText(null);
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
            if (_tool == Tool.Polyline)
            {
                if (_draft == null) { _draft = CreateMark(point); _draft.Points.Add(point); }
                else if (DistanceSquared(_draft.Points[_draft.Points.Count - 1], point) > 1) _draft.Points.Add(point);
                _draft.End = point;
                if (e.Clicks > 1) FinishPolyline();
                _canvas.Invalidate();
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
            return new Mark { Tool = _tool, Start = point, End = point, Color = _color, Width = _lineWidth, TextSize = _textSize, Filled = _filled, FontFamily = _fontFamily, FontStyle = _fontStyle };
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_transformBefore != null) { UpdateTransform(ToImage(e.Location, false)); return; }
            if (_panning)
            {
                _fit = false;
                _pan = new PointF(_panBefore.X + e.X - _panStart.X, _panBefore.Y + e.Y - _panStart.Y);
                _canvas.Invalidate();
                return;
            }
            if (_draft != null && _draft.Tool == Tool.Polyline)
            {
                PointF next = ToImage(e.Location, true);
                if ((ModifierKeys & Keys.Shift) != 0 && _draft.Points.Count > 0) next = SnapLine(_draft.Points[_draft.Points.Count - 1], next);
                _draft.End = next; _canvas.Invalidate(); return;
            }
            if (!_drawing || _draft == null)
            {
                if (_selectedIndex >= 0)
                {
                    int handle = HitSelectionHandle(ToImage(e.Location, false));
                    _canvas.Cursor = handle == 8 ? Cursors.Cross : handle >= 0 ? Cursors.SizeAll : _tool == Tool.Move ? Cursors.Default : Cursors.Cross;
                }
                return;
            }
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
            if (_transformBefore != null)
            {
                if (_transformChanged)
                {
                    _undo.Add(_transformBefore); _redo.Clear(); TrimHistory();
                }
                _transformBefore = null; _transformOriginal = null; _transformChanged = false;
                _canvas.Capture = false;
                UpdateRender(); SyncSelectedOptions();
                return;
            }
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
            _canvas.Cursor = _tool == Tool.Move ? Cursors.Default : _tool == Tool.Text ? Cursors.IBeam : _tool == Tool.Eraser ? Cursors.No : Cursors.Cross;
        }

        private void CancelGesture()
        {
            if (_transformBefore != null)
            {
                _state = _transformBefore; _transformBefore = null; _transformOriginal = null; _transformChanged = false;
                UpdateRender();
            }
            _drawing = false;
            _draft = null;
            _panning = false;
            _canvas.Capture = false;
            UpdateCursor();
            _canvas.Invalidate();
        }

        private static PointF SnapLine(PointF start, PointF end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * Math.PI / 4;
            return new PointF(start.X + (float)(Math.Cos(angle) * length), start.Y + (float)(Math.Sin(angle) * length));
        }

        private void FinishPolyline()
        {
            if (_draft == null || _draft.Tool != Tool.Polyline) return;
            Mark line = _draft;
            _draft = null; _drawing = false; _canvas.Capture = false;
            if (line.Points.Count >= 2)
            {
                line.End = line.Points[line.Points.Count - 1];
                PushUndo(); _state.Marks.Add(line); UpdateRender();
            }
            _canvas.Invalidate();
        }

        private void CanvasDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_tool == Tool.Polyline) { FinishPolyline(); return; }
            PointF point = ToImage(e.Location, false);
            int index = HitAnnotation(point);
            if (index >= 0 && _state.Marks[index].Tool == Tool.Text)
            {
                CancelGesture();
                string text = PromptText(_state.Marks[index].Text);
                if (text == null) return;
                PushUndo();
                Mark mark = _state.Marks[index].Clone(); mark.Text = text;
                _state.Marks[index] = mark; _selectedIndex = index; UpdateRender();
                return;
            }
            if (IsInline && _tool == Tool.Move && index < 0 && new RectangleF(0, 0, _state.Image.Width, _state.Image.Height).Contains(point)) CopyImage();
        }

        private int HitAnnotation(PointF point)
        {
            for (int i = _state.Marks.Count - 1; i >= 0; i--)
                if (HitTest(_state.Marks[i], point, Math.Max(4F, 6F / _zoom))) return i;
            return -1;
        }

        private void BeginTransform(PointF point, int handle)
        {
            _transformBefore = _state.Snapshot();
            _transformOriginal = _state.Marks[_selectedIndex].Clone();
            _transformBounds = MarkBounds(_transformOriginal);
            _transformStart = point; _transformHandle = handle; _transformChanged = false;
            _canvas.Capture = true;
        }

        private void UpdateTransform(PointF point)
        {
            float dx = point.X - _transformStart.X, dy = point.Y - _transformStart.Y;
            if (!_transformChanged && dx * dx + dy * dy < 0.25F) return;
            Mark changed = _transformOriginal.Clone();
            if (_transformHandle == 8)
            {
                PointF center = new PointF(_transformBounds.X + _transformBounds.Width / 2, _transformBounds.Y + _transformBounds.Height / 2);
                double before = Math.Atan2(_transformStart.Y - center.Y, _transformStart.X - center.X);
                double after = Math.Atan2(point.Y - center.Y, point.X - center.X);
                changed.Rotation += (float)((after - before) * 180 / Math.PI);
                if ((ModifierKeys & Keys.Shift) != 0) changed.Rotation = (float)Math.Round(changed.Rotation / 15) * 15;
            }
            else if (_transformHandle < 0) TransformMark(changed, _transformBounds, new RectangleF(_transformBounds.X + dx, _transformBounds.Y + dy, _transformBounds.Width, _transformBounds.Height), false);
            else
            {
                float left = _transformBounds.Left, right = _transformBounds.Right, top = _transformBounds.Top, bottom = _transformBounds.Bottom;
                int h = _transformHandle;
                if (h == 0 || h == 6 || h == 7) left = Math.Min(right - 4, left + dx);
                if (h == 2 || h == 3 || h == 4) right = Math.Max(left + 4, right + dx);
                if (h == 0 || h == 1 || h == 2) top = Math.Min(bottom - 4, top + dy);
                if (h == 4 || h == 5 || h == 6) bottom = Math.Max(top + 4, bottom + dy);
                RectangleF target = RectangleF.FromLTRB(left, top, right, bottom);
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    float ratio = Math.Max(target.Width / _transformBounds.Width, target.Height / _transformBounds.Height);
                    target.Width = _transformBounds.Width * ratio; target.Height = _transformBounds.Height * ratio;
                }
                TransformMark(changed, _transformBounds, target, true);
            }
            _state.Marks[_selectedIndex] = changed;
            _transformChanged = true; UpdateRender();
        }

        private static PointF TransformPoint(PointF point, RectangleF source, RectangleF target)
        {
            return new PointF(target.X + (point.X - source.X) * target.Width / Math.Max(0.001F, source.Width), target.Y + (point.Y - source.Y) * target.Height / Math.Max(0.001F, source.Height));
        }

        private static void TransformMark(Mark mark, RectangleF source, RectangleF target, bool resize)
        {
            mark.Start = TransformPoint(mark.Start, source, target); mark.End = TransformPoint(mark.End, source, target);
            for (int i = 0; i < mark.Points.Count; i++) mark.Points[i] = TransformPoint(mark.Points[i], source, target);
            if (resize && (mark.Tool == Tool.Text || mark.Tool == Tool.Number))
                mark.TextSize = Math.Max(8, Math.Min(300, mark.TextSize * Math.Min(target.Width / source.Width, target.Height / source.Height)));
        }

        private static SizeF MeasureMarkText(Mark mark)
        {
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Font font = new Font(mark.FontFamily, mark.TextSize, mark.FontStyle, GraphicsUnit.Pixel)) return g.MeasureString(mark.Text ?? "", font);
        }

        private static RectangleF MarkBounds(Mark mark)
        {
            RectangleF bounds = Normalized(mark.Start, mark.End);
            if (mark.Tool == Tool.Pen || mark.Tool == Tool.Highlight || mark.Tool == Tool.Polyline)
            {
                foreach (PointF point in mark.Points) bounds = RectangleF.Union(bounds, new RectangleF(point.X, point.Y, 0.01F, 0.01F));
            }
            else if (mark.Tool == Tool.Text)
            {
                SizeF size = MeasureMarkText(mark);
                bounds = new RectangleF(mark.Start, size);
                if (Math.Abs(mark.Rotation) > 0.01F)
                {
                    PointF center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                    PointF[] corners = { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top), new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom) };
                    using (Matrix matrix = new Matrix()) { matrix.RotateAt(mark.Rotation, center); matrix.TransformPoints(corners); }
                    bounds = new RectangleF(corners[0], SizeF.Empty);
                    foreach (PointF corner in corners) bounds = RectangleF.Union(bounds, new RectangleF(corner, new SizeF(0.01F, 0.01F)));
                }
            }
            else if (mark.Tool == Tool.Number)
            {
                float radius = Math.Max(16, mark.TextSize * 0.7F);
                bounds = new RectangleF(mark.Start.X - radius, mark.Start.Y - radius, radius * 2, radius * 2);
            }
            if (bounds.Width < 4) { bounds.X -= (4 - bounds.Width) / 2; bounds.Width = 4; }
            if (bounds.Height < 4) { bounds.Y -= (4 - bounds.Height) / 2; bounds.Height = 4; }
            return bounds;
        }

        private PointF[] SelectionHandles()
        {
            RectangleF bounds = MarkBounds(_state.Marks[_selectedIndex]);
            float cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
            return new PointF[] { new PointF(bounds.Left, bounds.Top), new PointF(cx, bounds.Top), new PointF(bounds.Right, bounds.Top), new PointF(bounds.Right, cy), new PointF(bounds.Right, bounds.Bottom), new PointF(cx, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Left, cy), new PointF(cx, bounds.Top - 24 / _zoom) };
        }

        private int HitSelectionHandle(PointF point)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _state.Marks.Count) return -1;
            PointF[] handles = SelectionHandles();
            int count = _state.Marks[_selectedIndex].Tool == Tool.Text ? 9 : 8;
            for (int i = 0; i < count; i++) if (DistanceSquared(point, handles[i]) <= 64 / (_zoom * _zoom)) return i;
            return -1;
        }

        private void PaintSelection(Graphics graphics)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _state.Marks.Count) return;
            RectangleF bounds = MarkBounds(_state.Marks[_selectedIndex]);
            PointF origin = ImageOrigin;
            RectangleF rectangle = new RectangleF(origin.X + bounds.X * _zoom, origin.Y + bounds.Y * ZoomY, bounds.Width * _zoom, bounds.Height * ZoomY);
            using (Pen border = new Pen(_accent, 1))
            using (SolidBrush fill = new SolidBrush(_surface))
            {
                border.DashStyle = DashStyle.Dash;
                graphics.DrawRectangle(border, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                border.DashStyle = DashStyle.Solid;
                PointF[] handles = SelectionHandles();
                int count = _state.Marks[_selectedIndex].Tool == Tool.Text ? 9 : 8;
                if (count == 9) graphics.DrawLine(border, rectangle.X + rectangle.Width / 2, rectangle.Top, rectangle.X + rectangle.Width / 2, rectangle.Top - 24);
                for (int i = 0; i < count; i++)
                {
                    RectangleF handle = new RectangleF(origin.X + handles[i].X * _zoom - 3, origin.Y + handles[i].Y * ZoomY - 3, 6, 6);
                    graphics.FillRectangle(fill, handle); graphics.DrawRectangle(border, handle.X, handle.Y, handle.Width, handle.Height);
                }
            }
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
            _selectedIndex = -1;
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
            _selectedIndex = -1;
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
            if (_initialState != null) result.Add(_initialState.Image);
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
            PointF origin = new PointF(_state.Origin.X + bounds.X * (IsPinEditing ? _zoom : 1F), _state.Origin.Y + bounds.Y * (IsPinEditing ? ZoomY : 1F));
            _state = new DocumentState(cropped);
            _state.Origin = origin;
            _selectedIndex = -1;
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
                _selectedIndex = -1;
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
            if (mark.Tool == Tool.Pen || mark.Tool == Tool.Highlight || mark.Tool == Tool.Polyline)
            {
                if (mark.Tool == Tool.Highlight) radius += Math.Max(12, mark.Width * 4) / 2;
                if (mark.Points.Count == 1) return DistanceSquared(point, mark.Points[0]) <= radius * radius;
                for (int i = 1; i < mark.Points.Count; i++) if (SegmentDistance(point, mark.Points[i - 1], mark.Points[i]) <= radius) return true;
                return false;
            }
            RectangleF bounds = MarkBounds(mark);
            if (mark.Tool == Tool.Number)
            {
                float r = Math.Max(16, mark.TextSize * 0.7F);
                return DistanceSquared(point, mark.Start) <= (r + tolerance) * (r + tolerance);
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
            if (HasFloatingBars) PositionInlineBars();
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
            Color color = mark.Tool == Tool.Highlight ? Color.FromArgb(Math.Max(1, 95 * mark.Color.A / 255), mark.Color) : mark.Color;
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
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            if (mark.Filled) g.FillRectangle(brush, rect);
                            else g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                        }
                        break;
                    case Tool.Ellipse:
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            if (mark.Filled) g.FillEllipse(brush, rect);
                            else g.DrawEllipse(pen, rect);
                        }
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
                    case Tool.Polyline:
                        if (mark.Points.Count > 1)
                        {
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                List<PointF> points = new List<PointF>(mark.Points);
                                if (mark.Tool == Tool.Polyline && DistanceSquared(points[points.Count - 1], mark.End) > 0.01F) points.Add(mark.End);
                                path.AddLines(points.ToArray());
                                g.DrawPath(pen, path);
                            }
                        }
                        else if (mark.Points.Count == 1)
                        {
                            if (mark.Tool == Tool.Polyline && DistanceSquared(mark.Start, mark.End) > 0.01F) g.DrawLine(pen, mark.Start, mark.End);
                            else g.FillEllipse(brush, mark.Start.X - width / 2, mark.Start.Y - width / 2, width, width);
                        }
                        break;
                    case Tool.Text:
                        GraphicsState textState = g.Save();
                        if (Math.Abs(mark.Rotation) > 0.01F)
                        {
                            SizeF size = MeasureMarkText(mark);
                            PointF center = new PointF(mark.Start.X + size.Width / 2, mark.Start.Y + size.Height / 2);
                            g.TranslateTransform(center.X, center.Y); g.RotateTransform(mark.Rotation); g.TranslateTransform(-center.X, -center.Y);
                        }
                        using (Font font = new Font(mark.FontFamily, mark.TextSize, mark.FontStyle, GraphicsUnit.Pixel)) g.DrawString(mark.Text, font, brush, mark.Start);
                        g.Restore(textState);
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

        private string PromptText(string existing)
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
                input.Text = existing ?? "";
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
            FinishPolyline();
            CancelGesture();
            try
            {
                ClipboardImages.Copy(_rendered);
                NotifyCommitted();
                SetStatus("클립보드에 복사했습니다 · " + _rendered.Width + " × " + _rendered.Height + " px");
                if (HasFloatingBars) CloseInline();
            }
            catch (Exception ex) { ShowActionError("이미지 복사", ex); }
        }

        private void SaveImage()
        {
            FinishPolyline();
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
                    if (IsInline) CloseInline();
                }
                catch (Exception ex) { ShowActionError("이미지 저장", ex); }
            }
        }

        private void PinImage()
        {
            FinishPolyline();
            CancelGesture();
            try
            {
                Action<Bitmap> handler = PinRequested;
                if (handler == null) { SetStatus("이미지 고정 기능이 연결되지 않았습니다"); return; }
                Bitmap image = CopyBitmap(_rendered);
                try { handler(image); } catch { image.Dispose(); throw; }
                NotifyCommitted();
                SetStatus("이미지를 화면 위에 고정했습니다 · 고정 창은 드래그로 이동할 수 있습니다");
                if (HasFloatingBars) CloseInline();
            }
            catch (Exception ex) { ShowActionError("이미지 고정", ex); }
        }

        private void QuickSaveImage()
        {
            FinishPolyline(); CancelGesture();
            try
            {
                string folder = !String.IsNullOrWhiteSpace(QuickSaveDirectory) ? QuickSaveDirectory : SaveDirectory;
                if (String.IsNullOrWhiteSpace(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Chacha");
                folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder);
                string name = "Chacha_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string path = Path.Combine(folder, name + ".png");
                int suffix = 1;
                while (File.Exists(path)) path = Path.Combine(folder, name + "_" + suffix++ + ".png");
                using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) _rendered.Save(file, ImageFormat.Png);
                NotifyCommitted(); SetStatus("빠른 저장 완료 · " + path);
                if (HasFloatingBars) CloseInline();
            }
            catch (Exception ex) { ShowActionError("빠른 저장", ex); }
        }

        private void PrintImage()
        {
            FinishPolyline(); CancelGesture();
            try
            {
                using (Bitmap image = ExportImage())
                using (PrintDocument document = new PrintDocument())
                using (PrintDialog dialog = new PrintDialog())
                {
                    document.DocumentName = "Chacha Capture";
                    document.DefaultPageSettings.Landscape = image.Width > image.Height;
                    document.PrintPage += delegate(object sender, PrintPageEventArgs args)
                    {
                        Rectangle bounds = args.MarginBounds;
                        float scale = Math.Min((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
                        RectangleF target = new RectangleF(bounds.X + (bounds.Width - image.Width * scale) / 2, bounds.Y + (bounds.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
                        args.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        args.Graphics.DrawImage(image, target); args.HasMorePages = false;
                    };
                    dialog.Document = document; dialog.UseEXDialog = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK) { document.Print(); SetStatus("이미지를 프린터로 보냈습니다"); }
                }
            }
            catch (Exception ex) { ShowActionError("인쇄", ex); }
        }

        private void ClearEdits()
        {
            CancelGesture(); _selectedIndex = -1;
            _state = _initialState.Snapshot(); _undo.Clear(); _redo.Clear(); TrimHistory(); UpdateRender(); FitImage();
            SetStatus("모든 편집을 지웠습니다");
        }

        private void DeleteSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _state.Marks.Count) return;
            PushUndo(); _state.Marks.RemoveAt(_selectedIndex); _selectedIndex = -1; UpdateRender();
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
            if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { QuickSaveImage(); return true; }
            if (keyData == (Keys.Control | Keys.T) || keyData == Keys.F3) { PinImage(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { PrintImage(); return true; }
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Z)) { ClearEdits(); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { FitImage(); return true; }
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1)) { ZoomAt(1F, new Point(_canvas.Width / 2, _canvas.Height / 2)); return true; }
            if (keyData == Keys.Escape)
            {
                if (WhiteboardMode) return true;
                if (_drawing || _panning || _draft != null || _transformBefore != null) CancelGesture();
                else if (IsPinEditing) CommitChanges();
                else if (IsInline) CloseInline();
                else SetTool(Tool.Move);
                return true;
            }
            if (keyData == Keys.Enter)
            {
                if (_draft != null && _draft.Tool == Tool.Polyline) FinishPolyline();
                else if (IsPinEditing) CommitChanges();
                else if (IsInline) CopyImage();
                else { _selectedIndex = -1; _canvas.Invalidate(); }
                return true;
            }
            if (keyData == Keys.Space)
            {
                if (IsPinEditing) { CommitChanges(); return true; }
                if (!_spaceHeld) ToggleBars();
                _spaceHeld = true;
                return true;
            }
            if (ActiveControl is NumericUpDown) return base.ProcessCmdKey(ref msg, keyData);
            Keys code = keyData & Keys.KeyCode;
            if (_selectedIndex >= 0 && (code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down))
            {
                int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
                Mark mark = _state.Marks[_selectedIndex].Clone();
                RectangleF bounds = MarkBounds(mark);
                RectangleF target = bounds;
                target.Offset(code == Keys.Left ? -step : code == Keys.Right ? step : 0, code == Keys.Up ? -step : code == Keys.Down ? step : 0);
                TransformMark(mark, bounds, target, false); PushUndo(); _state.Marks[_selectedIndex] = mark; UpdateRender(); return true;
            }
            switch (keyData)
            {
                case Keys.Delete: DeleteSelected(); return true;
                case Keys.V: SetTool(Tool.Move); return true;
                case Keys.R: SetTool(Tool.Rectangle); return true;
                case Keys.E: SetTool(Tool.Ellipse); return true;
                case Keys.A: SetTool(Tool.Arrow); return true;
                case Keys.L: SetTool(Tool.Line); return true;
                case Keys.P: SetTool(Tool.Polyline); return true;
                case Keys.B: SetTool(Tool.Pen); return true;
                case Keys.H: SetTool(Tool.Highlight); return true;
                case Keys.T: SetTool(Tool.Text); return true;
                case Keys.N: SetTool(Tool.Number); return true;
                case Keys.M: SetTool(Tool.Mosaic); return true;
                case Keys.U: SetTool(Tool.Blur); return true;
                case Keys.C: SetTool(Tool.Crop); return true;
                case Keys.X: SetTool(Tool.Eraser); return true;
                case Keys.D1:
                case Keys.OemOpenBrackets: _widthControl.Value = Math.Max(_widthControl.Minimum, _widthControl.Value - 1); return true;
                case Keys.D2:
                case Keys.OemCloseBrackets: _widthControl.Value = Math.Min(_widthControl.Maximum, _widthControl.Value + 1); return true;
                case Keys.Add:
                case Keys.Oemplus: ZoomAt(_zoom * 1.25F, new Point(_canvas.Width / 2, _canvas.Height / 2)); return true;
                case Keys.Subtract:
                case Keys.OemMinus: ZoomAt(_zoom / 1.25F, new Point(_canvas.Width / 2, _canvas.Height / 2)); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                if (IsPinEditing) { CommitChanges(); e.Handled = true; return; }
                if (!_spaceHeld) ToggleBars(); _spaceHeld = true; e.Handled = true;
            }
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
                if (_desktop != null) { _desktop.Dispose(); _desktop = null; }
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
