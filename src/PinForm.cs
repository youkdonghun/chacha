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
        private bool selected;
        private bool userMoving;
        private Point lastDragLocation;
        private readonly List<Rectangle> snapWindows = new List<Rectangle>();
        private bool magnifierVisible;
        private bool rgbColor;
        private MagnifierForm magnifier;
        private FloatingToolbarForm floatingToolbar;
        private DateTime toolbarRevealUntil = DateTime.MinValue;
        private DateTime toolbarLastPointerAt = DateTime.MinValue;
        private AnimatedImageSource animation;
        private int currentFrame;
        private bool playing;
        private double playbackSpeed = 1.0;
        private readonly List<RotateFlipType> animationTransforms = new List<RotateFlipType>();
        private readonly Timer animationTimer;
        private readonly Timer stateTimer;
        private readonly Timer hoverTimer;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem topmostMenu;
        private readonly ToolStripMenuItem clickThroughMenu;
        private readonly ToolStripMenuItem thumbnailMenu;
        private readonly ToolStripMenuItem animationMenu;
        private readonly ToolStripMenuItem closeMenu;
        private string closeHotkey = "Esc";
        private Keys closeKeyData = Keys.Escape;
        private bool hasCloseHotkey = true;

        public event Action<Bitmap> EditRequested;
        public event Action StateChanged;
        public event Action ReplaceRequested;
        public event Action SelectAllRequested;
        public event Action SelectionToggleRequested;
        public event Action<Point> MovePeersRequested;
        public event Action ManageGroupsRequested;
        public event Action PreferencesRequested;
        public event Action HiddenByUser;
        public event Action<IDataObject> DropRequested;

        public string PersistentId { get; set; }
        public string SaveDirectory { get; set; }
        public string QuickSaveDirectory { get; set; }
        public string SourceText { get; set; }
        public string GroupId { get; set; }
        public bool ClosedByUser { get; set; }
        /// <summary>Optional shortcut for this image and its toolbar, never a global binding.</summary>
        public string CloseHotkey
        {
            get { return closeHotkey; }
            set
            {
                closeHotkey = (value ?? String.Empty).Trim();
                uint modifiers, key;
                hasCloseHotkey = HotkeyWindow.Parse(closeHotkey, out modifiers, out key);
                closeKeyData = hasCloseHotkey ? (Keys)key |
                    ((modifiers & 2) != 0 ? Keys.Control : Keys.None) |
                    ((modifiers & 1) != 0 ? Keys.Alt : Keys.None) |
                    ((modifiers & 4) != 0 ? Keys.Shift : Keys.None) : Keys.None;
                if (closeMenu != null) closeMenu.ShortcutKeyDisplayString = CloseShortcutText;
                if (floatingToolbar != null) floatingToolbar.RefreshCloseShortcut();
            }
        }
        private string CloseShortcutText
        {
            get { return !hasCloseHotkey || closeKeyData == (Keys.Control | Keys.W) ? "Ctrl+W" : closeHotkey + " / Ctrl+W"; }
        }
        public bool IsSelected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }
        public int FrameCount { get { return animation == null ? 1 : animation.FrameCount; } }
        public int CurrentFrame { get { return currentFrame; } }
        public bool IsPlaying { get { return playing && animation != null && FrameCount > 1; } }
        public double PlaybackSpeed { get { return playbackSpeed; } }
        public string AnimationTransformState
        {
            get
            {
                List<string> values = new List<string>();
                foreach (RotateFlipType transform in animationTransforms) values.Add(((int)transform).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return String.Join(",", values.ToArray());
            }
            set
            {
                List<RotateFlipType> transforms = new List<RotateFlipType>();
                if (!String.IsNullOrEmpty(value))
                    foreach (string part in value.Split(','))
                    {
                        int parsed;
                        if (!Int32.TryParse(part, out parsed) || !Enum.IsDefined(typeof(RotateFlipType), parsed))
                            throw new ArgumentException("잘못된 GIF 변환 상태입니다.", "value");
                        transforms.Add((RotateFlipType)parsed);
                    }
                animationTransforms.Clear();
                animationTransforms.AddRange(transforms);
                if (animation != null)
                {
                    Point center = new Point(Left + Width / 2, Top + Height / 2);
                    SetFrame(currentFrame);
                    SetScale(scale, null);
                    Location = new Point(center.X - Width / 2, center.Y - Height / 2);
                    QueueStateChanged();
                }
            }
        }

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
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) || e.Data.GetDataPresent(DataFormats.Text))
                    e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object sender, DragEventArgs e) { Action<IDataObject> handler = DropRequested; if (handler != null) handler(e.Data); };
            TopMost = true;
            BackColor = Color.FromArgb(20, 27, 35);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            stateTimer = new Timer();
            stateTimer.Interval = 240;
            stateTimer.Tick += delegate { stateTimer.Stop(); RaiseStateChanged(); };
            hoverTimer = new Timer();
            hoverTimer.Interval = 50;
            hoverTimer.Tick += delegate
            {
                if (magnifierVisible) UpdateMagnifier();
                UpdateFloatingToolbar();
                if (!Visible || !Bounds.Contains(Cursor.Position))
                {
                    mouseOver = false;
                    if (!magnifierVisible && (floatingToolbar == null || !floatingToolbar.Visible)) hoverTimer.Stop();
                    Invalidate();
                }
            };
            animationTimer = new Timer();
            animationTimer.Tick += delegate
            {
                if (IsPlaying && Visible) { SetFrame((currentFrame + 1) % FrameCount); UpdateAnimationTimer(); }
                else animationTimer.Stop();
            };

            menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Items.Add(Item("복사", "Ctrl+C", delegate { CopyImage(); }));
            menu.Items.Add(Item("다른 이름으로 저장…", "Ctrl+S", delegate { SaveImage(); }));
            menu.Items.Add(Item("빠른 저장", "Ctrl+Shift+S", delegate { QuickSave(); }));
            menu.Items.Add(Item("인쇄…", "Ctrl+P", delegate { PrintImage(); }));
            menu.Items.Add(Item("클립보드 내용으로 교체", "Ctrl+V", delegate { Raise(ReplaceRequested); }));
            menu.Items.Add(Item("원본 텍스트 복사", "Ctrl+Shift+C", delegate { CopySourceText(); }));
            menu.Items.Add(Item("편집", "Space", delegate { RequestEdit(); }));
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem rotate = new ToolStripMenuItem("회전 / 뒤집기");
            rotate.DropDownItems.Add(Item("오른쪽으로 90° 회전", "1", delegate { Transform(RotateFlipType.Rotate90FlipNone); }));
            rotate.DropDownItems.Add(Item("왼쪽으로 90° 회전", "2", delegate { Transform(RotateFlipType.Rotate270FlipNone); }));
            rotate.DropDownItems.Add(Item("좌우 뒤집기", "3", delegate { Transform(RotateFlipType.RotateNoneFlipX); }));
            rotate.DropDownItems.Add(Item("상하 뒤집기", "4", delegate { Transform(RotateFlipType.RotateNoneFlipY); }));
            menu.Items.Add(rotate);
            animationMenu = new ToolStripMenuItem("GIF 재생");
            animationMenu.DropDownItems.Add(Item("재생 / 일시 정지", "G", delegate { SetPlaying(!IsPlaying); }));
            animationMenu.DropDownItems.Add(Item("이전 프레임", "1", delegate { StepFrame(-1); }));
            animationMenu.DropDownItems.Add(Item("다음 프레임", "2", delegate { StepFrame(1); }));
            animationMenu.DropDownItems.Add(Item("기본 재생 속도", "", delegate { SetPlaybackSpeed(1); }));
            menu.Items.Add(animationMenu);

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
            menu.Items.Add(Item("전체 고정 이미지 선택", "Ctrl+A", delegate { Raise(SelectAllRequested); }));
            menu.Items.Add(Item("이 이미지 선택 / 해제", "Ctrl+클릭", delegate { ToggleSelection(); }));
            menu.Items.Add(Item("이미지 그룹 관리…", "", delegate { Raise(ManageGroupsRequested); }));
            menu.Items.Add(Item("설정…", "Ctrl+Shift+P", delegate { Raise(PreferencesRequested); }));
            menu.Items.Add(new ToolStripSeparator());
            closeMenu = Item("닫기 · 다시 불러올 수 있음", CloseShortcutText, delegate { HideByUser(); });
            menu.Items.Add(closeMenu);
            menu.Items.Add(Item("완전히 삭제", "Shift+Esc", delegate { Close(); }));
            menu.Opening += delegate
            {
                topmostMenu.Checked = TopMost;
                clickThroughMenu.Checked = clickThrough;
                thumbnailMenu.Checked = thumbnail;
                animationMenu.Visible = animation != null;
                animationMenu.Text = "GIF · " + (currentFrame + 1) + " / " + FrameCount + " · " + playbackSpeed.ToString("0.##") + "×";
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
            Point cursor = Cursor.Position;
            Location = ClampFloatingLocation(new Point(cursor.X + 18, cursor.Y + 18), screen);
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
            ClearAnimation();
            SourceText = null;
            ReplaceRaster(new Bitmap(source));
        }

        private void ReplaceRaster(Bitmap replacement)
        {
            Point center = new Point(Left + Width / 2, Top + Height / 2);
            Bitmap previous = image;
            image = replacement;
            if (previous != null) previous.Dispose();
            thumbnail = false;
            SetScale(scale, null);
            Location = new Point(center.X - Width / 2, center.Y - Height / 2);
            Invalidate();
            QueueStateChanged();
        }

        public void LoadAnimatedFile(string path)
        {
            LoadAnimation(File.ReadAllBytes(path));
        }

        public void LoadAnimation(byte[] data)
        {
            AnimatedImageSource next = new AnimatedImageSource(data);
            Bitmap first;
            try { first = next.GetFrame(0); }
            catch { next.Dispose(); throw; }
            ClearAnimation();
            animation = next;
            currentFrame = 0;
            playbackSpeed = 1;
            playing = FrameCount > 1;
            SourceText = null;
            ReplaceRaster(first);
            UpdateAnimationTimer();
        }

        public byte[] ExportAnimation()
        {
            return animation == null ? null : animation.ExportBytes();
        }

        public void SetPlaying(bool enabled)
        {
            playing = enabled && animation != null && FrameCount > 1;
            UpdateAnimationTimer();
            Invalidate();
            QueueStateChanged();
        }

        public void StepFrame(int delta)
        {
            if (animation == null) return;
            SetPlaying(false);
            SetFrame(((currentFrame + delta) % FrameCount + FrameCount) % FrameCount);
            QueueStateChanged();
        }

        public void SeekFrame(int frame)
        {
            if (animation == null) return;
            SetFrame(Math.Max(0, Math.Min(FrameCount - 1, frame)));
            UpdateAnimationTimer();
            QueueStateChanged();
        }

        public void SetPlaybackSpeed(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            playbackSpeed = Math.Max(0.1, Math.Min(8, value));
            UpdateAnimationTimer();
            Invalidate();
            QueueStateChanged();
        }

        private void SetFrame(int frame)
        {
            Bitmap next = animation.GetFrame(frame);
            foreach (RotateFlipType transform in animationTransforms) next.RotateFlip(transform);
            Bitmap old = image;
            image = next;
            currentFrame = frame;
            old.Dispose();
            Invalidate();
        }

        private void UpdateAnimationTimer()
        {
            if (animationTimer == null || disposing) return;
            animationTimer.Stop();
            if (IsPlaying && Visible)
            {
                animationTimer.Interval = Math.Max(15, Math.Min(600000, (int)Math.Round(animation.DelayForFrame(currentFrame) / playbackSpeed)));
                animationTimer.Start();
            }
        }

        private void ClearAnimation()
        {
            if (animationTimer != null) animationTimer.Stop();
            if (animation != null) { animation.Dispose(); animation = null; }
            animationTransforms.Clear();
            currentFrame = 0;
            playing = false;
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        private void ToggleSelection()
        {
            Action handler = SelectionToggleRequested;
            if (handler != null) handler();
            else IsSelected = !IsSelected;
        }

        private void HideByUser()
        {
            ClosedByUser = true;
            Hide();
            Raise(HiddenByUser);
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
            ClosedByUser = false;
            SetClickThrough(false);
            EnsureReachable();
            if (!Visible) Show();
            BringToFront();
            RevealFloatingToolbar();
            FocusForKeyboard();
        }

        /// <summary>Show a captured image as a separate, reachable window above other applications.</summary>
        public void ShowFloating(Rectangle sourceBounds)
        {
            ShowFloating((Rectangle?)sourceBounds);
        }

        public void ShowFloating()
        {
            ShowFloating((Rectangle?)null);
        }

        public void ShowFloating(Rectangle? sourceBounds)
        {
            if (IsDisposed || disposing) throw new ObjectDisposedException("PinForm");
            bool hasBounds = sourceBounds.HasValue && sourceBounds.Value.Width > 0 && sourceBounds.Value.Height > 0;
            Point anchor = hasBounds ? sourceBounds.Value.Location : Cursor.Position;
            Rectangle work = Screen.FromPoint(anchor).WorkingArea;
            double fit = Math.Min((work.Width - 12.0) / image.Width, (work.Height - 12.0) / image.Height);
            double requested = hasBounds ? Math.Min((double)sourceBounds.Value.Width / image.Width, (double)sourceBounds.Value.Height / image.Height) : 1;
            ClosedByUser = false;
            SetClickThrough(false);
            TopMost = true;
            Opacity = 1;
            WindowState = FormWindowState.Normal;
            if (Owner != null) Owner = null;
            ScaleFactor = Math.Min(requested, Math.Min(1, fit));
            Rectangle pixels = ImageRectangle;
            Point position = hasBounds ? new Point(anchor.X - pixels.Left, anchor.Y - pixels.Top) : new Point(anchor.X + 18, anchor.Y + 18);
            Location = ClampFloatingLocation(position, work);
            if (!Visible) Show();
            // Explicitly raise after Show so previously layered or owned pins cannot stay behind another topmost window.
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
            BringToFront();
            RevealFloatingToolbar();
            FocusForKeyboard();
            QueueStateChanged();
        }

        private void FocusForKeyboard()
        {
            if (IsDisposed || disposing || !Visible || clickThrough) return;
            Activate();
            Focus();
        }

        private Point ClampFloatingLocation(Point location, Rectangle work)
        {
            return new Point(Math.Max(work.Left, Math.Min(location.X, work.Right - Math.Min(Width, work.Width))),
                Math.Max(work.Top, Math.Min(location.Y, work.Bottom - Math.Min(Height, work.Height))));
        }

        private void EnsureReachable()
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible = Rectangle.Intersect(Bounds, screen.WorkingArea);
                if (visible.Width >= Math.Min(48, Width) && visible.Height >= Math.Min(48, Height)) return;
            }
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Width > work.Width || Height > work.Height)
                SetScale(Math.Min((work.Width - 8.0) / image.Width, (work.Height - 8.0) / image.Height), null);
            Location = ClampFloatingLocation(new Point(Cursor.Position.X + 18, Cursor.Position.Y + 18), work);
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
                if (!ShowInTaskbar) parameters.ExStyle |= 0x80; // Normal pins do not crowd Alt+Tab; UI tests can expose them.
                return parameters;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == 0x100 || m.Msg == 0x104) && m.WParam.ToInt32() == (int)Keys.Menu)
            {
                SetMagnifier(true); m.Result = IntPtr.Zero; return;
            }
            if ((m.Msg == 0x101 || m.Msg == 0x105) && m.WParam.ToInt32() == (int)Keys.Menu)
            {
                SetMagnifier(false); m.Result = IntPtr.Zero; return;
            }
            if ((m.Msg == 0x100 || m.Msg == 0x104) && m.WParam.ToInt32() == (int)Keys.ShiftKey && magnifierVisible)
            {
                if ((m.LParam.ToInt64() & (1L << 30)) == 0) rgbColor = !rgbColor;
                UpdateMagnifier(); m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == WmMouseWheel)
            {
                int delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                if ((ModifierKeys & Keys.Control) != 0)
                    SetOpacity(Opacity + delta / 120.0 * 0.05);
                else if (animation != null) SetPlaybackSpeed(playbackSpeed * Math.Pow(1.12, delta / 120.0));
                else Zoom(Math.Pow(1.12, delta / 120.0), Cursor.Position);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WmNcLButtonDoubleClick)
            {
                if ((ModifierKeys & Keys.Shift) != 0) ToggleThumbnail();
                else HideByUser();
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0xA1) // WM_NCLBUTTONDOWN
            {
                if ((ModifierKeys & Keys.Control) != 0)
                {
                    ToggleSelection(); m.Result = IntPtr.Zero; return;
                }
                userMoving = m.WParam.ToInt32() == 2;
                lastDragLocation = Location;
                if (userMoving) SnapshotSnapWindows();
            }
            if (m.Msg == 0xA8) // WM_NCMBUTTONUP
            {
                ResetDisplay(); m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == 0x216 && (ModifierKeys & Keys.Shift) != 0) // WM_MOVING
                SnapMovingWindow(m.LParam);
            if (m.Msg == 0x214) // WM_SIZING
            {
                ResizeFromBorder(m.WParam.ToInt32(), m.LParam);
                m.Result = new IntPtr(1); return;
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
            {
                if (magnifierVisible) return;
                long coordinates = m.LParam.ToInt64();
                Point point = PointToClient(new Point(unchecked((short)(coordinates & 0xFFFF)), unchecked((short)((coordinates >> 16) & 0xFFFF))));
                bool left = point.X < 7, right = point.X >= Width - 7;
                bool top = point.Y < 7, bottom = point.Y >= Height - 7;
                int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 :
                    left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 2;
                m.Result = new IntPtr(hit);
            }
            else if (m.Msg == WmExitSizeMove) { userMoving = false; QueueStateChanged(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+W and Shift+Esc retain their fixed recoverable/permanent meanings.
            // A configured local binding takes precedence over the other pin commands.
            if (keyData == (Keys.Shift | Keys.Escape)) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.W) || (hasCloseHotkey && keyData == closeKeyData))
            { HideByUser(); return true; }
            Keys key = keyData & Keys.KeyCode;
            if (magnifierVisible && key == Keys.C && (keyData & Keys.Control) == 0) { CopySampleColor(); return true; }
            if (magnifierVisible && (keyData & Keys.Control) == 0)
            {
                if (key == Keys.W) { Cursor.Position = new Point(Cursor.Position.X, Cursor.Position.Y - 1); UpdateMagnifier(); return true; }
                if (key == Keys.S) { Cursor.Position = new Point(Cursor.Position.X, Cursor.Position.Y + 1); UpdateMagnifier(); return true; }
                if (key == Keys.A) { Cursor.Position = new Point(Cursor.Position.X - 1, Cursor.Position.Y); UpdateMagnifier(); return true; }
                if (key == Keys.D) { Cursor.Position = new Point(Cursor.Position.X + 1, Cursor.Position.Y); UpdateMagnifier(); return true; }
            }
            switch (keyData)
            {
                case Keys.Control | Keys.C: CopyImage(); return true;
                case Keys.Control | Keys.Shift | Keys.C: CopySourceText(); return true;
                case Keys.Control | Keys.V: Raise(ReplaceRequested); return true;
                case Keys.Control | Keys.A: Raise(SelectAllRequested); return true;
                case Keys.Control | Keys.S: SaveImage(); return true;
                case Keys.Control | Keys.Shift | Keys.S: QuickSave(); return true;
                case Keys.Control | Keys.P: PrintImage(); return true;
                case Keys.Control | Keys.Shift | Keys.P: Raise(PreferencesRequested); return true;
                case Keys.Space: RequestEdit(); return true;
                case Keys.D1: case Keys.NumPad1:
                    if (animation != null) StepFrame(-1); else Transform(RotateFlipType.Rotate90FlipNone); return true;
                case Keys.D2: case Keys.NumPad2:
                    if (animation != null) StepFrame(1); else Transform(RotateFlipType.Rotate270FlipNone); return true;
                case Keys.D3: case Keys.NumPad3: Transform(RotateFlipType.RotateNoneFlipX); return true;
                case Keys.D4: case Keys.NumPad4: Transform(RotateFlipType.RotateNoneFlipY); return true;
                case Keys.Add: case Keys.Oemplus: case Keys.Shift | Keys.Oemplus:
                    Zoom(1.12, null); return true;
                case Keys.Subtract: case Keys.OemMinus:
                    Zoom(1.0 / 1.12, null); return true;
                case Keys.Control | Keys.D0: case Keys.Control | Keys.NumPad0: ResetDisplay(); return true;
                case Keys.Control | Keys.Add: case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Shift | Keys.Oemplus:
                    SetOpacity(Opacity + 0.05); return true;
                case Keys.Control | Keys.Subtract: case Keys.Control | Keys.OemMinus:
                    SetOpacity(Opacity - 0.05); return true;
                case Keys.Left: MoveBy(-1, 0); return true;
                case Keys.Right: MoveBy(1, 0); return true;
                case Keys.Up: MoveBy(0, -1); return true;
                case Keys.Down: MoveBy(0, 1); return true;
                case Keys.G: if (animation != null) { SetPlaying(!IsPlaying); return true; } break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public Rectangle ImageScreenBounds { get { Rectangle bounds = ImageRectangle; bounds.Offset(Location); return bounds; } }
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
            using (Pen border = new Pen(IsSelected ? Color.FromArgb(255, 201, 75) : Color.FromArgb(mouseOver ? 110 : 62, 211, 187), FrameSize))
                e.Graphics.DrawRectangle(border, 1, 1, Math.Max(0, ClientSize.Width - 2), Math.Max(0, ClientSize.Height - 2));
            if (IsSelected)
            {
                using (Brush handle = new SolidBrush(Color.FromArgb(255, 201, 75)))
                {
                    e.Graphics.FillRectangle(handle, 1, 1, 6, 6);
                    e.Graphics.FillRectangle(handle, Width - 7, 1, 6, 6);
                    e.Graphics.FillRectangle(handle, 1, Height - 7, 6, 6);
                    e.Graphics.FillRectangle(handle, Width - 7, Height - 7, 6, 6);
                }
            }
            if (mouseOver && Width >= 190 && Height >= 80)
            {
                string hint = animation == null ? Math.Round(scale * 100) + "% · 우클릭 메뉴 · 휠 확대" :
                    (IsPlaying ? "▶ " : "Ⅱ ") + (currentFrame + 1) + "/" + FrameCount + " · " + playbackSpeed.ToString("0.##") + "× · G 재생";
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
            requested = LimitScale(requested);
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

        private double LimitScale(double requested)
        {
            Rectangle desktop = SystemInformation.VirtualScreen;
            double maximum = Math.Min(16.0, Math.Min((desktop.Width - 4.0) / image.Width, (desktop.Height - 4.0) / image.Height));
            double minimum = Math.Min(maximum, 44.0 / Math.Max(image.Width, image.Height));
            return Math.Max(minimum, Math.Min(maximum, requested));
        }

        private void ResetDisplay()
        {
            ScaleFactor = 1;
            SetOpacity(1);
        }

        private void MoveBy(int dx, int dy)
        {
            Location = new Point(Left + dx, Top + dy);
            Action<Point> handler = MovePeersRequested;
            if (handler != null) handler(new Point(dx, dy));
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
            if (animation != null) animationTransforms.Add(transform);
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

        private void CopySourceText()
        {
            if (String.IsNullOrEmpty(SourceText)) return;
            CopyText(SourceText);
        }

        private void CopyText(string text)
        {
            try { Clipboard.SetDataObject(text, true, 5, 70); }
            catch (ExternalException)
            {
                MessageBox.Show(this, "클립보드를 사용할 수 없습니다. 잠시 후 다시 시도하세요.", "Chacha", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void QuickSave()
        {
            string folder = String.IsNullOrWhiteSpace(QuickSaveDirectory) ? SaveDirectory : QuickSaveDirectory;
            if (String.IsNullOrWhiteSpace(folder)) { SaveImage(); return; }
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "Chacha_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 4) + (animation == null ? ".png" : ".gif"));
                if (animation == null) image.Save(path, ImageFormat.Png);
                else File.WriteAllBytes(path, ExportAnimation());
                Text = "Chacha · 저장 완료 · " + Path.GetFileName(path);
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ExternalException || error is ArgumentException)) throw;
                MessageBox.Show(this, "이미지를 저장할 수 없습니다.\n" + error.Message, "Chacha", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PrintImage()
        {
            using (Bitmap copy = ExportImage())
            using (PrintDocument document = new PrintDocument())
            using (PrintDialog dialog = new PrintDialog())
            {
                document.DocumentName = "Chacha Capture";
                document.PrintPage += delegate(object sender, PrintPageEventArgs e)
                {
                    double fit = Math.Min((double)e.MarginBounds.Width / copy.Width, (double)e.MarginBounds.Height / copy.Height);
                    int width = Math.Max(1, (int)(copy.Width * fit)), height = Math.Max(1, (int)(copy.Height * fit));
                    e.Graphics.DrawImage(copy, new Rectangle(e.MarginBounds.Left + (e.MarginBounds.Width - width) / 2,
                        e.MarginBounds.Top + (e.MarginBounds.Height - height) / 2, width, height));
                    e.HasMorePages = false;
                };
                dialog.Document = document;
                dialog.UseEXDialog = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { document.Print(); }
                catch (Exception error)
                {
                    if (!(error is InvalidPrinterException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException)) throw;
                    MessageBox.Show(this, "인쇄를 완료할 수 없습니다.\n" + error.Message, "Chacha", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void CopyImage()
        {
            try
            {
                using (Bitmap copy = ExportImage())
                {
                    ClipboardImages.Copy(copy);
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
                if (animation != null) dialog.Filter = "GIF 원본 애니메이션 (*.gif)|*.gif|현재 프레임 PNG (*.png)|*.png|현재 프레임 JPEG (*.jpg)|*.jpg";
                dialog.FileName = "Chacha_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                dialog.AddExtension = true;
                dialog.DefaultExt = "png";
                if (animation != null) dialog.DefaultExt = "gif";
                if (!String.IsNullOrWhiteSpace(SaveDirectory) && Directory.Exists(SaveDirectory))
                    dialog.InitialDirectory = SaveDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    if (extension == ".gif" && animation != null) { File.WriteAllBytes(dialog.FileName, ExportAnimation()); return; }
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

        private void SnapshotSnapWindows()
        {
            snapWindows.Clear();
            foreach (Screen screen in Screen.AllScreens) snapWindows.Add(screen.WorkingArea);
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (window == Handle || !IsWindowVisible(window) || IsIconic(window)) return true;
                PinForm peer = Control.FromHandle(window) as PinForm;
                if (IsSelected && peer != null && peer.IsSelected) return true;
                NativeRect rect;
                if (GetWindowRect(window, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                    snapWindows.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                return true;
            }, IntPtr.Zero);
        }

        private void SnapMovingWindow(IntPtr address)
        {
            NativeRect native = (NativeRect)Marshal.PtrToStructure(address, typeof(NativeRect));
            Rectangle moving = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            int dx = 0, dy = 0, bestX = 11, bestY = 11;
            foreach (Rectangle other in snapWindows)
            {
                if (moving.Bottom > other.Top - 10 && moving.Top < other.Bottom + 10)
                {
                    ConsiderSnap(other.Left - moving.Left, ref dx, ref bestX);
                    ConsiderSnap(other.Right - moving.Left, ref dx, ref bestX);
                    ConsiderSnap(other.Left - moving.Right, ref dx, ref bestX);
                    ConsiderSnap(other.Right - moving.Right, ref dx, ref bestX);
                }
                if (moving.Right > other.Left - 10 && moving.Left < other.Right + 10)
                {
                    ConsiderSnap(other.Top - moving.Top, ref dy, ref bestY);
                    ConsiderSnap(other.Bottom - moving.Top, ref dy, ref bestY);
                    ConsiderSnap(other.Top - moving.Bottom, ref dy, ref bestY);
                    ConsiderSnap(other.Bottom - moving.Bottom, ref dy, ref bestY);
                }
            }
            native.Left += dx; native.Right += dx; native.Top += dy; native.Bottom += dy;
            Marshal.StructureToPtr(native, address, false);
        }

        private static void ConsiderSnap(int distance, ref int offset, ref int best)
        {
            if (Math.Abs(distance) < best) { best = Math.Abs(distance); offset = distance; }
        }

        private void ResizeFromBorder(int edge, IntPtr address)
        {
            NativeRect rect = (NativeRect)Marshal.PtrToStructure(address, typeof(NativeRect));
            double requested = edge == 3 || edge == 6 ? (rect.Bottom - rect.Top - 4.0) / image.Height : (rect.Right - rect.Left - 4.0) / image.Width;
            scale = LimitScale(requested);
            thumbnail = false;
            int width = Math.Max(48, (int)Math.Round(image.Width * scale) + 4);
            int height = Math.Max(48, (int)Math.Round(image.Height * scale) + 4);
            if (edge == 1 || edge == 4 || edge == 7) rect.Left = rect.Right - width;
            else rect.Right = rect.Left + width;
            if (edge == 3 || edge == 4 || edge == 5) rect.Top = rect.Bottom - height;
            else rect.Bottom = rect.Top + height;
            Marshal.StructureToPtr(rect, address, false);
            Invalidate();
            QueueStateChanged();
        }

        private Point SamplePoint()
        {
            Point position = PointToClient(Cursor.Position);
            Rectangle target = ImageRectangle;
            return new Point(Math.Max(0, Math.Min(image.Width - 1, (int)Math.Floor((position.X - target.Left) / scale))),
                Math.Max(0, Math.Min(image.Height - 1, (int)Math.Floor((position.Y - target.Top) / scale))));
        }

        private string SampleText()
        {
            Point point = SamplePoint();
            Color color = image.GetPixel(point.X, point.Y);
            return rgbColor ? "rgb(" + color.R + ", " + color.G + ", " + color.B + ")" : "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private void CopySampleColor()
        {
            CopyText(SampleText());
        }

        private void SetMagnifier(bool visible)
        {
            magnifierVisible = visible;
            if (visible)
            {
                if (floatingToolbar != null) floatingToolbar.Hide();
                if (magnifier == null) magnifier = new MagnifierForm(this);
                hoverTimer.Start();
                UpdateMagnifier();
            }
            else if (magnifier != null) magnifier.Hide();
        }

        private void RevealFloatingToolbar()
        {
            toolbarRevealUntil = DateTime.UtcNow.AddSeconds(4);
            hoverTimer.Start();
            UpdateFloatingToolbar();
        }

        private void UpdateFloatingToolbar()
        {
            if (disposing || IsDisposed) return;
            if (!Visible || clickThrough || magnifierVisible || WindowState == FormWindowState.Minimized)
            {
                if (floatingToolbar != null) floatingToolbar.Hide();
                return;
            }
            Point pointer = Cursor.Position;
            bool pointerOnImage = Bounds.Contains(pointer);
            bool pointerOnToolbar = floatingToolbar != null && floatingToolbar.Visible && floatingToolbar.Bounds.Contains(pointer);
            DateTime now = DateTime.UtcNow;
            if (pointerOnImage || pointerOnToolbar) toolbarLastPointerAt = now;
            bool show = now < toolbarRevealUntil || pointerOnImage || pointerOnToolbar || (now - toolbarLastPointerAt).TotalMilliseconds < 380;
            if (!show) { if (floatingToolbar != null) floatingToolbar.Hide(); return; }
            if (floatingToolbar == null) floatingToolbar = new FloatingToolbarForm(this);
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(work.Left, Math.Min(Left, work.Right - floatingToolbar.Width));
            int y = Top - floatingToolbar.Height - 6;
            if (y < work.Top) y = Bottom + 6;
            if (y + floatingToolbar.Height > work.Bottom) y = Math.Max(work.Top, Top + 6);
            floatingToolbar.Location = new Point(x, y);
            floatingToolbar.RefreshTopmost();
            if (!floatingToolbar.Visible) floatingToolbar.Show(this);
        }

        private void StartToolbarDrag()
        {
            FocusForKeyboard();
            ReleaseCapture();
            SendWindowMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private sealed class FloatingToolbarForm : Form
        {
            private readonly PinForm pin;
            private readonly Button topmost;
            private readonly Button closeButton;
            private readonly ToolTip tips = new ToolTip();
            private readonly Font toolbarFont = new Font("Malgun Gothic", 8.5F, FontStyle.Regular);

            internal FloatingToolbarForm(PinForm source)
            {
                pin = source;
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = source.TopMost;
                DoubleBuffered = true;
                ClientSize = new Size(326, 36);
                BackColor = Color.FromArgb(23, 31, 40);
                ForeColor = Color.White;
                Font = toolbarFont;
                Label status = new Label { Text = "● 화면에 띄움", Bounds = new Rectangle(9, 1, 102, 34), TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(94, 234, 196), Cursor = Cursors.SizeAll };
                status.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) pin.StartToolbarDrag(); };
                tips.SetToolTip(status, "이미지나 이 표시를 드래그해서 이동하세요.\n마우스 휠 또는 테두리 드래그로 크기를 조절합니다.");
                Controls.Add(status);
                topmost = ButtonAt("항상 위", 111, 70, "다른 프로그램보다 위에 유지 / 해제", delegate
                {
                    pin.TopMost = !pin.TopMost;
                    RefreshTopmost();
                    pin.QueueStateChanged();
                });
                ButtonAt("편집", 184, 44, "이 이미지에 표시하기 · Space", delegate { pin.RequestEdit(); });
                ButtonAt("저장", 231, 44, "이미지 파일로 저장 · Ctrl+S", delegate { pin.SaveImage(); });
                closeButton = ButtonAt("×", 278, 39, "", delegate { pin.HideByUser(); });
                closeButton.AccessibleName = "플로팅 이미지 닫기";
                RefreshCloseShortcut();
                RefreshTopmost();
            }

            private Button ButtonAt(string text, int x, int width, string hint, EventHandler action)
            {
                Button button = new RoundedButton { Text = text, Bounds = new Rectangle(x, 5, width, 26), CornerRadius = 8, FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(36, 47, 59), ForeColor = Color.White, Cursor = Cursors.Hand, TabStop = false,
                    Font = toolbarFont, UseVisualStyleBackColor = false };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.BorderColor = Color.FromArgb(54, 72, 82);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 76, 86);
                button.Click += delegate(object sender, EventArgs e)
                {
                    // WS_EX_NOACTIVATE keeps a hovering toolbar from stealing focus.
                    // An intentional click must give the image its keyboard shortcuts.
                    pin.FocusForKeyboard();
                    if (action != null) action(sender, e);
                };
                tips.SetToolTip(button, hint);
                Controls.Add(button);
                return button;
            }

            internal void RefreshTopmost()
            {
                if (TopMost != pin.TopMost) TopMost = pin.TopMost;
                topmost.Text = pin.TopMost ? "항상 위 ✓" : "항상 위";
                topmost.BackColor = pin.TopMost ? Color.FromArgb(48, 101, 89) : Color.FromArgb(36, 47, 59);
            }

            internal void RefreshCloseShortcut()
            {
                string hint = "닫기 · " + pin.CloseShortcutText + " (다시 표시할 수 있습니다)";
                tips.SetToolTip(closeButton, hint);
                closeButton.AccessibleDescription = hint;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) pin.FocusForKeyboard();
                base.OnMouseDown(e);
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                if (!pin.IsDisposed && pin.ProcessCmdKey(ref message, keyData)) return true;
                return base.ProcessCmdKey(ref message, keyData);
            }

            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000080; return p; }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 2 || Height < 2) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 11))
                using (Pen border = new Pen(Color.FromArgb(62, 121, 108))) e.Graphics.DrawPath(border, shape);
            }
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Width < 2 || Height < 2) return;
                using (GraphicsPath shape = Theme.Round(new RectangleF(0, 0, Width, Height), 11))
                {
                    Region previous = Region; Region = new Region(shape); if (previous != null) previous.Dispose();
                }
            }
            protected override void Dispose(bool disposingManaged)
            {
                if (disposingManaged) { tips.Dispose(); toolbarFont.Dispose(); }
                base.Dispose(disposingManaged);
            }
        }

        private void UpdateMagnifier()
        {
            if (!magnifierVisible || magnifier == null) return;
            if (!Visible || !Bounds.Contains(Cursor.Position)) { magnifier.Hide(); return; }
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Point location = new Point(Cursor.Position.X + 22, Cursor.Position.Y + 22);
            if (location.X + magnifier.Width > work.Right) location.X = Cursor.Position.X - magnifier.Width - 22;
            if (location.Y + magnifier.Height > work.Bottom) location.Y = Cursor.Position.Y - magnifier.Height - 22;
            location.X = Math.Max(work.Left, location.X); location.Y = Math.Max(work.Top, location.Y);
            magnifier.Location = location;
            if (!magnifier.Visible) magnifier.Show(this);
            magnifier.Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (magnifierVisible) UpdateMagnifier();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Middle) ResetDisplay();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            SetMagnifier(false);
            base.OnDeactivate(e);
        }

        private sealed class MagnifierForm : Form
        {
            private readonly PinForm pin;
            internal MagnifierForm(PinForm source)
            {
                pin = source;
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                DoubleBuffered = true;
                ClientSize = new Size(196, 241);
                BackColor = Color.FromArgb(22, 28, 37);
                Font = new Font("Segoe UI", 9);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000080 | 0x20; return p; }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (pin.image == null) return;
                Point sample = pin.SamplePoint();
                const int cell = 10;
                for (int gy = 0; gy < 17; gy++)
                    for (int gx = 0; gx < 17; gx++)
                    {
                        int x = Math.Max(0, Math.Min(pin.image.Width - 1, sample.X + gx - 8));
                        int y = Math.Max(0, Math.Min(pin.image.Height - 1, sample.Y + gy - 8));
                        using (Brush color = new SolidBrush(pin.image.GetPixel(x, y)))
                            e.Graphics.FillRectangle(color, 13 + gx * cell, 12 + gy * cell, cell, cell);
                    }
                using (Pen grid = new Pen(Color.FromArgb(50, 0, 0, 0)))
                    for (int i = 0; i <= 17; i++)
                    {
                        e.Graphics.DrawLine(grid, 13 + i * cell, 12, 13 + i * cell, 182);
                        e.Graphics.DrawLine(grid, 13, 12 + i * cell, 183, 12 + i * cell);
                    }
                e.Graphics.DrawRectangle(Pens.Black, 92, 91, 12, 12);
                e.Graphics.DrawRectangle(Pens.White, 93, 92, 10, 10);
                TextRenderer.DrawText(e.Graphics, pin.SampleText(), Font, new Rectangle(8, 191, 180, 22), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, "C 복사 · Shift HEX/RGB", Font, new Rectangle(8, 214, 180, 19), Color.FromArgb(140, 161, 181),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (Pen frame = new Pen(Color.FromArgb(83, 221, 189))) e.Graphics.DrawRectangle(frame, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (userMoving && !changingSize)
            {
                Point delta = new Point(Left - lastDragLocation.X, Top - lastDragLocation.Y);
                lastDragLocation = Location;
                Action<Point> handler = MovePeersRequested;
                if (handler != null && (delta.X != 0 || delta.Y != 0)) handler(delta);
            }
            if (!changingSize) QueueStateChanged();
            if (floatingToolbar != null && floatingToolbar.Visible) UpdateFloatingToolbar();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) SetMagnifier(false);
            if (!Visible && floatingToolbar != null) floatingToolbar.Hide();
            UpdateAnimationTimer();
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
                if (animationTimer != null) animationTimer.Dispose();
                if (animation != null) { animation.Dispose(); animation = null; }
                if (magnifier != null) { magnifier.Dispose(); magnifier = null; }
                if (floatingToolbar != null) { floatingToolbar.Dispose(); floatingToolbar = null; }
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
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll", EntryPoint = "SendMessage")] private static extern IntPtr SendWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }

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
