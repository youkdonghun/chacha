using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChachaCapture
{
    /// <summary>A movable, zoomable image which stays above other windows.</summary>
    public sealed class PinForm : Form
    {
        private const int FrameSize = 2;
        private const int GwlExStyle = -20;
        private const long WsExTransparent = 0x20L;
        private const long WsExLayered = 0x80000L;
        private const int WmNcHitTest = 0x0084;
        private const int WmNcMouseMove = 0x00A0;
        private const int WmNcLButtonDoubleClick = 0x00A3;
        private const int WmNcRButtonUp = 0x00A5;
        private const int WmMouseWheel = 0x020A;
        private const int WmExitSizeMove = 0x0232;
        private Bitmap image;
        private double scale = 1.0;
        private double normalScale = 1.0;
        private bool thumbnail;
        private bool clickThrough;
        private bool mouseOver;
        private bool changingSize;
        private bool disposing;
        private readonly Timer stateTimer;
        private readonly Timer hoverTimer;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem topmostMenu;
        private readonly ToolStripMenuItem clickThroughMenu;
        private readonly ToolStripMenuItem thumbnailMenu;

        public event Action<Bitmap> EditRequested;
        public event Action StateChanged;

        public string PersistentId { get; set; }
        public string SaveDirectory { get; set; }

        public bool ClickThrough
        {
            get { return clickThrough; }
        }

        public double ScaleFactor
        {
            get { return scale; }
            set
            {
                thumbnail = false;
                SetScale(value, null);
            }
        }

        public PinForm(Bitmap source)
        {
            if (source == null) throw new ArgumentNullException("source");
            image = new Bitmap(source);
            PersistentId = Guid.NewGuid().ToString("N");
            Text = "Chacha · 고정 이미지";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            TopMost = true;
            BackColor = Color.FromArgb(20, 27, 35);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            stateTimer = new Timer();
            stateTimer.Interval = 240;
            stateTimer.Tick += delegate { stateTimer.Stop(); RaiseStateChanged(); };
            hoverTimer = new Timer();
            hoverTimer.Interval = 180;
            hoverTimer.Tick += delegate
            {
                if (!Visible || !Bounds.Contains(Cursor.Position))
                {
                    mouseOver = false;
                    hoverTimer.Stop();
                    Invalidate();
                }
            };

            menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Items.Add(Item("복사", "Ctrl+C", delegate { CopyImage(); }));
            menu.Items.Add(Item("다른 이름으로 저장…", "Ctrl+S", delegate { SaveImage(); }));
            menu.Items.Add(Item("편집", "Space", delegate { RequestEdit(); }));
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem rotate = new ToolStripMenuItem("회전 / 뒤집기");
            rotate.DropDownItems.Add(Item("왼쪽으로 90° 회전", "1", delegate { Transform(RotateFlipType.Rotate270FlipNone); }));
            rotate.DropDownItems.Add(Item("오른쪽으로 90° 회전", "2", delegate { Transform(RotateFlipType.Rotate90FlipNone); }));
            rotate.DropDownItems.Add(Item("좌우 뒤집기", "3", delegate { Transform(RotateFlipType.RotateNoneFlipX); }));
            rotate.DropDownItems.Add(Item("상하 뒤집기", "4", delegate { Transform(RotateFlipType.RotateNoneFlipY); }));
            menu.Items.Add(rotate);

            ToolStripMenuItem zoom = new ToolStripMenuItem("확대 / 축소");
            zoom.DropDownItems.Add(Item("확대", "+ / 휠 ↑", delegate { Zoom(1.12, null); }));
            zoom.DropDownItems.Add(Item("축소", "− / 휠 ↓", delegate { Zoom(1.0 / 1.12, null); }));
            zoom.DropDownItems.Add(new ToolStripSeparator());
            foreach (int percent in new int[] { 25, 50, 100, 150, 200 })
            {
                int selectedPercent = percent;
                zoom.DropDownItems.Add(new ToolStripMenuItem(percent + "%", null,
                    delegate { ScaleFactor = selectedPercent / 100.0; }));
            }
            menu.Items.Add(zoom);

            ToolStripMenuItem opacity = new ToolStripMenuItem("불투명도 · Ctrl+휠");
            foreach (int percent in new int[] { 25, 50, 75, 100 })
            {
                int selectedPercent = percent;
                opacity.DropDownItems.Add(new ToolStripMenuItem(percent + "%", null,
                    delegate { SetOpacity(selectedPercent / 100.0); }));
            }
            menu.Items.Add(opacity);
            thumbnailMenu = Item("썸네일로 접기", "Shift+더블 클릭", delegate { ToggleThumbnail(); });
            menu.Items.Add(thumbnailMenu);
            topmostMenu = new ToolStripMenuItem("항상 위에 표시", null, delegate
            {
                TopMost = !TopMost;
                QueueStateChanged();
            });
            clickThroughMenu = new ToolStripMenuItem("마우스 클릭 통과", null, delegate { SetClickThrough(!clickThrough); });
            menu.Items.Add(topmostMenu);
            menu.Items.Add(clickThroughMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("숨기기", "Esc / 더블 클릭", delegate { Hide(); }));
            menu.Items.Add(Item("닫기", "Ctrl+W", delegate { Close(); }));
            menu.Opening += delegate
            {
                topmostMenu.Checked = TopMost;
                clickThroughMenu.Checked = clickThrough;
                thumbnailMenu.Checked = thumbnail;
                foreach (ToolStripItem child in opacity.DropDownItems)
                {
                    ToolStripMenuItem choice = child as ToolStripMenuItem;
                    if (choice != null) choice.Checked = choice.Text == Math.Round(Opacity * 100) + "%";
                }
            };
            ContextMenuStrip = menu;

            Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            double initialScale = Math.Min(1.0, Math.Min(
                (screen.Width * 0.72 - FrameSize * 2) / image.Width,
                (screen.Height * 0.72 - FrameSize * 2) / image.Height));
            SetScale(initialScale, null);
            Location = new Point(screen.Left + (screen.Width - Width) / 2,
                screen.Top + (screen.Height - Height) / 2);
        }

        private static ToolStripMenuItem Item(string text, string shortcut, EventHandler action)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, action);
            item.ShortcutKeyDisplayString = shortcut;
            return item;
        }

        public Bitmap ExportImage()
        {
            if (image == null) throw new ObjectDisposedException("PinForm");
            return new Bitmap(image);
        }

        public void ReplaceImage(Bitmap source)
        {
            if (source == null) throw new ArgumentNullException("source");
            Point center = new Point(Left + Width / 2, Top + Height / 2);
            Bitmap replacement = new Bitmap(source);
            Bitmap previous = image;
            image = replacement;
            if (previous != null) previous.Dispose();
            thumbnail = false;
            SetScale(scale, null);
            Location = new Point(center.X - Width / 2, center.Y - Height / 2);
            Invalidate();
            QueueStateChanged();
        }

        public void SetClickThrough(bool enabled)
        {
            if (clickThrough == enabled) return;
            clickThrough = enabled;
            if (IsHandleCreated) ApplyClickThrough();
            QueueStateChanged();
        }

        public void RestoreInteractive()
        {
            SetClickThrough(false);
            if (!Visible) Show();
            BringToFront();
            Activate();
        }

        public void ToggleVisible()
        {
            if (Visible) Hide();
            else RestoreInteractive();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyClickThrough();
        }

        private void ApplyClickThrough()
        {
            long style = ReadWindowLong(Handle, GwlExStyle).ToInt64();
            if (clickThrough) style |= WsExTransparent | WsExLayered;
            else
            {
                style &= ~WsExTransparent;
                if (Opacity >= 1.0) style &= ~WsExLayered;
            }
            WriteWindowLong(Handle, GwlExStyle, new IntPtr(style));
            if ((style & WsExLayered) != 0)
                SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(Opacity * 255), 2);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80; // Tool window: pins do not crowd Alt+Tab.
                return parameters;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseWheel)
            {
                int delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                if ((ModifierKeys & Keys.Control) != 0)
                    SetOpacity(Opacity + delta / 120.0 * 0.05);
                else Zoom(Math.Pow(1.12, delta / 120.0), Cursor.Position);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WmNcLButtonDoubleClick)
            {
                if ((ModifierKeys & Keys.Shift) != 0) ToggleThumbnail();
                else Hide();
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WmNcRButtonUp)
            {
                menu.Show(Cursor.Position);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WmNcMouseMove && !mouseOver)
            {
                mouseOver = true;
                hoverTimer.Start();
                Invalidate();
            }
            base.WndProc(ref m);
            if (m.Msg == WmNcHitTest && m.Result == new IntPtr(1))
                m.Result = new IntPtr(2); // HTCAPTION gives native, multi-monitor dragging.
            else if (m.Msg == WmExitSizeMove) QueueStateChanged();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.C: CopyImage(); return true;
                case Keys.Control | Keys.S: SaveImage(); return true;
                case Keys.Control | Keys.W: Close(); return true;
                case Keys.Escape: Hide(); return true;
                case Keys.Space: RequestEdit(); return true;
                case Keys.D1: case Keys.NumPad1: Transform(RotateFlipType.Rotate270FlipNone); return true;
                case Keys.D2: case Keys.NumPad2: Transform(RotateFlipType.Rotate90FlipNone); return true;
                case Keys.D3: case Keys.NumPad3: Transform(RotateFlipType.RotateNoneFlipX); return true;
                case Keys.D4: case Keys.NumPad4: Transform(RotateFlipType.RotateNoneFlipY); return true;
                case Keys.Add: case Keys.Oemplus: case Keys.Shift | Keys.Oemplus:
                    Zoom(1.12, null); return true;
                case Keys.Subtract: case Keys.OemMinus:
                    Zoom(1.0 / 1.12, null); return true;
                case Keys.Control | Keys.D0: ScaleFactor = 1.0; return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Rectangle ImageRectangle
        {
            get
            {
                int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                return new Rectangle((ClientSize.Width - width) / 2,
                    (ClientSize.Height - height) / 2, width, height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image == null) return;
            Rectangle target = ImageRectangle;
            // A checkerboard makes transparency in clipboard PNGs visible.
            using (HatchBrush checker = new HatchBrush(HatchStyle.LargeCheckerBoard,
                Color.FromArgb(52, 57, 65), Color.FromArgb(39, 44, 52)))
                e.Graphics.FillRectangle(checker, target);
            e.Graphics.InterpolationMode = scale >= 2.0 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                e.Graphics.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
            using (Pen border = new Pen(Color.FromArgb(mouseOver ? 110 : 62, 211, 187), FrameSize))
                e.Graphics.DrawRectangle(border, 1, 1, Math.Max(0, ClientSize.Width - 2), Math.Max(0, ClientSize.Height - 2));
            if (mouseOver && Width >= 190 && Height >= 80)
            {
                string hint = Math.Round(scale * 100) + "% · 우클릭 메뉴 · 휠 확대";
                Size hintSize = TextRenderer.MeasureText(hint, Font);
                Rectangle label = new Rectangle(6, Height - hintSize.Height - 12,
                    Math.Min(Width - 12, hintSize.Width + 12), hintSize.Height + 6);
                using (SolidBrush background = new SolidBrush(Color.FromArgb(215, 21, 30, 39)))
                    e.Graphics.FillRectangle(background, label);
                TextRenderer.DrawText(e.Graphics, hint, Font, label, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void Zoom(double factor, Point? anchor)
        {
            thumbnail = false;
            SetScale(scale * factor, anchor);
        }

        private void SetScale(double requested, Point? screenAnchor)
        {
            if (image == null || double.IsNaN(requested) || double.IsInfinity(requested)) return;
            Rectangle desktop = SystemInformation.VirtualScreen;
            double maximum = Math.Min(16.0, Math.Min((desktop.Width - 4.0) / image.Width,
                (desktop.Height - 4.0) / image.Height));
            double minimum = Math.Min(maximum, 44.0 / Math.Max(image.Width, image.Height));
            requested = Math.Max(minimum, Math.Min(maximum, requested));
            Point anchor = screenAnchor ?? new Point(Left + Width / 2, Top + Height / 2);
            Rectangle oldRect = ImageRectangle;
            double imageX = (anchor.X - Left - oldRect.Left) / scale;
            double imageY = (anchor.Y - Top - oldRect.Top) / scale;
            scale = requested;
            changingSize = true;
            try
            {
                ClientSize = new Size(Math.Max(48, (int)Math.Round(image.Width * scale) + FrameSize * 2),
                    Math.Max(48, (int)Math.Round(image.Height * scale) + FrameSize * 2));
                Rectangle newRect = ImageRectangle;
                Location = new Point((int)Math.Round(anchor.X - newRect.Left - imageX * scale),
                    (int)Math.Round(anchor.Y - newRect.Top - imageY * scale));
            }
            finally { changingSize = false; }
            Invalidate();
            QueueStateChanged();
        }

        private void SetOpacity(double value)
        {
            Opacity = Math.Max(0.15, Math.Min(1.0, value));
            if (clickThrough) ApplyClickThrough();
            QueueStateChanged();
        }

        private void ToggleThumbnail()
        {
            if (thumbnail)
            {
                thumbnail = false;
                SetScale(normalScale, null);
            }
            else
            {
                normalScale = scale;
                thumbnail = true;
                SetScale(Math.Min(scale, 150.0 / Math.Max(image.Width, image.Height)), null);
            }
        }

        private void Transform(RotateFlipType transform)
        {
            Point center = new Point(Left + Width / 2, Top + Height / 2);
            image.RotateFlip(transform);
            // Rebuild about the window center after swapping image dimensions.
            SetScale(scale, null);
            Location = new Point(center.X - Width / 2, center.Y - Height / 2);
            Invalidate();
            QueueStateChanged();
        }

        private void RequestEdit()
        {
            Action<Bitmap> handler = EditRequested;
            if (handler == null) return;
            Bitmap editImage = ExportImage();
            try { handler(editImage); }
            catch
            {
                editImage.Dispose();
                throw;
            }
        }

        private void CopyImage()
        {
            try
            {
                using (Bitmap copy = ExportImage())
                {
                    DataObject data = new DataObject();
                    data.SetData(DataFormats.Bitmap, true, copy);
                    Clipboard.SetDataObject(data, true, 5, 70);
                }
            }
            catch (ExternalException)
            {
                MessageBox.Show(this, "다른 프로그램이 클립보드를 사용 중입니다. 잠시 후 다시 시도해 주세요.",
                    "Chacha", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SaveImage()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "고정 이미지 저장";
                dialog.Filter = "PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg)|*.jpg|비트맵 이미지 (*.bmp)|*.bmp";
                dialog.FileName = "Chacha_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                dialog.AddExtension = true;
                dialog.DefaultExt = "png";
                if (!String.IsNullOrWhiteSpace(SaveDirectory) && Directory.Exists(SaveDirectory))
                    dialog.InitialDirectory = SaveDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    ImageFormat format = extension == ".jpg" || extension == ".jpeg" ? ImageFormat.Jpeg :
                        extension == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png;
                    if (format == ImageFormat.Jpeg)
                    {
                        using (Bitmap flattened = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
                        {
                            using (Graphics graphics = Graphics.FromImage(flattened))
                            {
                                graphics.Clear(Color.White);
                                graphics.DrawImageUnscaled(image, 0, 0);
                            }
                            flattened.Save(dialog.FileName, format);
                        }
                    }
                    else image.Save(dialog.FileName, format);
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                        !(error is ExternalException) && !(error is ArgumentException)) throw;
                    MessageBox.Show(this, "이미지를 저장할 수 없습니다.\n" + error.Message,
                        "Chacha", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (!changingSize) QueueStateChanged();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            QueueStateChanged();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!changingSize) QueueStateChanged();
        }

        private void QueueStateChanged()
        {
            if (stateTimer == null || disposing || IsDisposed) return;
            stateTimer.Stop();
            stateTimer.Start();
        }

        private void RaiseStateChanged()
        {
            Action handler = StateChanged;
            if (handler != null) handler();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            stateTimer.Stop();
            base.OnFormClosed(e);
            RaiseStateChanged();
        }

        protected override void Dispose(bool disposingManaged)
        {
            disposing = true;
            if (disposingManaged)
            {
                if (stateTimer != null) stateTimer.Dispose();
                if (hoverTimer != null) hoverTimer.Dispose();
                if (menu != null) menu.Dispose();
                if (image != null) { image.Dispose(); image = null; }
            }
            base.Dispose(disposingManaged);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLong64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLong64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

        private static IntPtr ReadWindowLong(IntPtr window, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(window, index) : new IntPtr(GetWindowLong32(window, index));
        }

        private static void WriteWindowLong(IntPtr window, int index, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLong64(window, index, value);
            else SetWindowLong32(window, index, value.ToInt32());
        }
    }
}
