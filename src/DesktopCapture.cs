using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace ChachaCapture
{
    public enum DesktopCaptureMode { Automatic = 0, Compatibility = 1, Graphics = 2 }

    public sealed class DesktopCaptureReport
    {
        public string Backend { get; internal set; }
        public string Details { get; internal set; }
        public string Notice { get; internal set; }
        public bool UniformFrame { get; internal set; }
        public bool ProtectedContentMasked { get; internal set; }
        public bool EmbeddedCursor { get; internal set; }
        internal readonly List<string> Events = new List<string>();
    }

    /// <summary>Supported desktop capture APIs with explicit pixel ownership and opaque screen pixels.</summary>
    public static class DesktopCapture
    {
        private const uint SourceCopy = 0x00CC0020;
        private const uint CaptureLayeredWindows = 0x40000000;
        private const uint NoMirrorBitmap = 0x80000000;
        private const int DxgiNotFound = unchecked((int)0x887A0002);
        private const int DxgiStillDrawing = unchecked((int)0x887A000A);
        private static readonly Guid Factory1Id = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        private static readonly Guid Output1Id = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
        private static readonly Guid Texture2DId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        public static string LastBackend { get; private set; }
        public static DesktopCaptureReport LastReport { get; private set; }

        private sealed class Display
        {
            internal string Name;
            internal Rectangle Bounds;
        }

        public static Bitmap Capture(out Rectangle bounds)
        { return Capture(out bounds, DesktopCaptureMode.Automatic); }

        public static Bitmap Capture(out Rectangle bounds, bool preferGpu)
        { return Capture(out bounds, preferGpu ? DesktopCaptureMode.Graphics : DesktopCaptureMode.Compatibility); }

        public static Bitmap Capture(out Rectangle bounds, DesktopCaptureMode mode)
        {
            if (!Enum.IsDefined(typeof(DesktopCaptureMode), mode)) mode = DesktopCaptureMode.Automatic;
            DesktopCaptureReport report = new DesktopCaptureReport();
            LastReport = report; LastBackend = "";
            Stopwatch elapsed = Stopwatch.StartNew();
            IntPtr previousDpi = EnterPhysicalCoordinates();
            try
            {
                List<Display> displays = Displays(out bounds);
                report.Events.Add("Mode=" + mode + "; displays=" + displays.Count + "; bounds=" + bounds);
                FlushComposition();
                foreach (DesktopCaptureMode backend in BackendOrder(mode))
                {
                    try
                    {
                        Bitmap result = backend == DesktopCaptureMode.Graphics ? CaptureDuplication(displays, bounds, report) : CaptureGdi(displays, bounds, true);
                        try
                        {
                            LastBackend = backend == DesktopCaptureMode.Graphics ? "DXGI Desktop Duplication" : "GDI / 24-bit DIB / layered windows";
                            report.Backend = LastBackend;
                            report.UniformFrame = IsUniformFrame(result, displays, bounds);
                            report.Notice = DescribeNotice(report);
                            report.Events.Add("Result=" + LastBackend + "; uniform=" + report.UniformFrame + "; protected=" + report.ProtectedContentMasked + "; embeddedCursor=" + report.EmbeddedCursor);
                            return result;
                        }
                        catch { result.Dispose(); throw; }
                    }
                    catch (Exception error)
                    {
                        if (!IsCaptureFailure(error)) throw;
                        report.Events.Add(backend + ": " + DescribeFailure(error));
                        // Do not use another backend to work around an explicit desktop access denial.
                        if (IsAccessDenied(error) || mode != DesktopCaptureMode.Automatic)
                            throw new InvalidOperationException(DescribeFailure(error), error);
                    }
                }
                throw new InvalidOperationException("현재 화면을 캡처하지 못했습니다. 설정의 캡처 진단에서 사용한 방식과 오류를 확인해 주세요.");
            }
            finally
            {
                report.Events.Add("ElapsedMs=" + elapsed.ElapsedMilliseconds);
                report.Details = String.Join(Environment.NewLine, report.Events.ToArray());
                LeavePhysicalCoordinates(previousDpi);
            }
        }

        internal static DesktopCaptureMode[] BackendOrder(DesktopCaptureMode mode)
        {
            if (mode == DesktopCaptureMode.Graphics) return new[] { DesktopCaptureMode.Graphics };
            if (mode == DesktopCaptureMode.Compatibility) return new[] { DesktopCaptureMode.Compatibility };
            return new[] { DesktopCaptureMode.Compatibility, DesktopCaptureMode.Graphics };
        }

        /// <summary>Explicit supported GPU backend, useful for diagnosing a display-driver capture problem.</summary>
        public static Bitmap CaptureDxgi(out Rectangle bounds)
        { return Capture(out bounds, DesktopCaptureMode.Graphics); }

        private static bool IsCaptureFailure(Exception error)
        {
            return error is ExternalException || error is Win32Exception || error is DllNotFoundException ||
                error is UnauthorizedAccessException || error.HResult == unchecked((int)0x80004002) ||
                error is EntryPointNotFoundException || error is NotSupportedException || error is InvalidOperationException;
        }

        internal static bool IsAccessDenied(Exception error)
        {
            Win32Exception native = error as Win32Exception;
            return (native != null && native.NativeErrorCode == 5) || error.HResult == unchecked((int)0x80070005);
        }

        internal static string DescribeFailure(Exception error)
        {
            Win32Exception native = error as Win32Exception;
            string code = "0x" + error.HResult.ToString("X8") + (native == null ? "" : "; Win32=" + native.NativeErrorCode);
            if (IsAccessDenied(error)) return "Windows가 현재 화면에 대한 접근을 허용하지 않았습니다. (" + code + ")";
            if (error.HResult == unchecked((int)0x887A0022)) return "다른 캡처 앱이 GPU 캡처 연결을 사용 중입니다. 호환 캡처를 선택해 주세요. (" + code + ")";
            if (error.HResult == unchecked((int)0x887A0026)) return "화면 구성이 변경되어 GPU 캡처 연결이 끊겼습니다. 다시 캡처해 주세요. (" + code + ")";
            if (error.HResult == unchecked((int)0x887A0027)) return "GPU에서 새 화면을 받는 데 시간이 초과되었습니다. (" + code + ")";
            if (error is DllNotFoundException || error is EntryPointNotFoundException || error is NotSupportedException || error.HResult == unchecked((int)0x80004002))
                return "선택한 캡처 방식을 현재 Windows 또는 디스플레이에서 지원하지 않습니다. (" + code + ")";
            return "화면을 읽지 못했습니다. (" + code + ")";
        }

        internal static string DescribeNotice(DesktopCaptureReport report)
        {
            if (report.ProtectedContentMasked) return "Windows가 일부 화면을 보호된 콘텐츠로 표시했습니다. 해당 영역은 캡처에 표시되지 않을 수 있습니다.";
            if (report.UniformFrame) return "캡처한 화면이 단색입니다. 미리보기가 실제 화면과 다르면 설정에서 캡처 방식을 바꾸고 다시 시도해 주세요.";
            if (report.EmbeddedCursor) return "현재 GPU가 마우스 포인터를 화면에 포함했습니다. 포인터를 빼려면 호환 캡처를 사용해 주세요.";
            return "";
        }

        private static bool IsUniformFrame(Bitmap image, IList<Display> displays, Rectangle bounds)
        {
            foreach (Display display in displays)
                if (!IsUniformRegion(image, new Rectangle(display.Bounds.X - bounds.X, display.Bounds.Y - bounds.Y, display.Bounds.Width, display.Bounds.Height))) return false;
            return displays.Count > 0;
        }

        internal static bool IsUniformRegion(Bitmap image, Rectangle area)
        {
            if (image == null || area.Width < 1 || area.Height < 1 || !new Rectangle(Point.Empty, image.Size).Contains(area)) return false;
            BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[checked(area.Width * 4)];
                Marshal.Copy(data.Scan0, row, 0, row.Length);
                byte blue = row[0], green = row[1], red = row[2];
                for (int y = 0; y < area.Height; y++)
                {
                    if (y != 0) Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += 4)
                        if (row[x] != blue || row[x + 1] != green || row[x + 2] != red) return false;
                }
                return true;
            }
            finally { image.UnlockBits(data); }
        }

        /// <summary>Screen buffers are RGB data. Keep their RGB values even when a driver left alpha at zero.</summary>
        public static Bitmap CloneOpaque(Bitmap source)
        {
            if (source == null) throw new ArgumentNullException("source");
            Rectangle rectangle = new Rectangle(Point.Empty, source.Size);
            BitmapData data = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { return ReadPixels(data.Scan0, source.Width, source.Height, data.Stride, 4); }
            finally { source.UnlockBits(data); }
        }

        private static List<Display> Displays(out Rectangle bounds)
        {
            List<Display> displays = new List<Display>();
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr state)
            {
                MonitorInfo info = new MonitorInfo();
                info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (GetMonitorInfo(monitor, ref info))
                {
                    Rectangle area = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
                    if (area.Width > 0 && area.Height > 0) displays.Add(new Display { Name = info.Device, Bounds = area });
                }
                return true;
            }, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "디스플레이 목록을 읽지 못했습니다.");
            if (displays.Count == 0) throw new InvalidOperationException("사용할 수 있는 디스플레이가 없습니다.");
            bounds = displays[0].Bounds;
            foreach (Display display in displays) bounds = Rectangle.Union(bounds, display.Bounds);
            checked { int bytes = bounds.Width * bounds.Height * 4; if (bytes <= 0) throw new InvalidOperationException("화면 크기가 올바르지 않습니다."); }
            return displays;
        }

        private static Bitmap CaptureGdi(IList<Display> displays, Rectangle bounds, bool layered)
        {
            Bitmap result = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result)) graphics.Clear(Color.Black);
                foreach (Display display in displays)
                    using (Bitmap monitor = CaptureGdiRegion(display.Bounds, layered))
                        using (Graphics graphics = Graphics.FromImage(result))
                            graphics.DrawImageUnscaled(monitor, display.Bounds.X - bounds.X, display.Bounds.Y - bounds.Y);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static Bitmap CaptureGdiRegion(Rectangle area, bool layered)
        {
            IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                screen = GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "바탕 화면 DC를 열지 못했습니다.");
                memory = CreateCompatibleDC(screen);
                if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "캡처 버퍼를 만들지 못했습니다.");
                int stride = checked((area.Width * 3 + 3) & ~3);
                BitmapInfo info = new BitmapInfo();
                info.Header.Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader));
                info.Header.Width = area.Width; info.Header.Height = -area.Height;
                info.Header.Planes = 1; info.Header.BitCount = 24;
                info.Header.SizeImage = checked((uint)(stride * area.Height));
                IntPtr pixels;
                bitmap = CreateDIBSection(screen, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "캡처 픽셀 버퍼를 만들지 못했습니다.");
                previous = SelectObject(memory, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "캡처 버퍼를 선택하지 못했습니다.");
                uint operation = SourceCopy | NoMirrorBitmap | (layered ? CaptureLayeredWindows : 0);
                if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, operation))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "화면 픽셀을 읽지 못했습니다.");
                // GDI owns the buffer until its queued drawing has completed.
                GdiFlush();
                return ReadPixels(pixels, area.Width, area.Height, stride, 3);
            }
            finally
            {
                if (memory != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }

        // Shared by GDI and DXGI: row pitch is independent from pixel width, and screen alpha is always opaque.
        internal static Bitmap ReadPixels(IntPtr pixels, int width, int height, int stride, int bytesPerPixel)
        {
            if (pixels == IntPtr.Zero || width <= 0 || height <= 0 || (bytesPerPixel != 3 && bytesPerPixel != 4) ||
                Math.Abs((long)stride) < (long)width * bytesPerPixel)
                throw new ArgumentException("Invalid desktop pixel buffer.");
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                BitmapData target = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] input = new byte[checked(width * bytesPerPixel)];
                    byte[] output = new byte[checked(width * 4)];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(new IntPtr(pixels.ToInt64() + (long)y * stride), input, 0, input.Length);
                        for (int x = 0; x < width; x++)
                        {
                            int from = x * bytesPerPixel, to = x * 4;
                            output[to] = input[from]; output[to + 1] = input[from + 1]; output[to + 2] = input[from + 2]; output[to + 3] = 255;
                        }
                        Marshal.Copy(output, 0, new IntPtr(target.Scan0.ToInt64() + (long)y * target.Stride), output.Length);
                    }
                }
                finally { result.UnlockBits(target); }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static Bitmap CaptureDuplication(IList<Display> displays, Rectangle bounds, DesktopCaptureReport report)
        {
            IntPtr factory = IntPtr.Zero;
            Bitmap result = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result)) graphics.Clear(Color.Black);
                Guid factoryId = Factory1Id;
                Check(CreateDXGIFactory1(ref factoryId, out factory));
                HashSet<string> captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Stopwatch elapsed = Stopwatch.StartNew();
                for (uint adapterIndex = 0; adapterIndex < 16 && captured.Count < displays.Count && elapsed.ElapsedMilliseconds < 900; adapterIndex++)
                {
                    IntPtr adapter;
                    int adapterResult = Method<Enumerate>(factory, 12)(factory, adapterIndex, out adapter);
                    if (adapterResult == DxgiNotFound) break;
                    Check(adapterResult);
                    IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
                    try
                    {
                        uint featureLevel;
                        Check(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out featureLevel, out context));
                        for (uint outputIndex = 0; outputIndex < 32 && captured.Count < displays.Count && elapsed.ElapsedMilliseconds < 900; outputIndex++)
                        {
                            IntPtr output;
                            int outputResult = Method<Enumerate>(adapter, 7)(adapter, outputIndex, out output);
                            if (outputResult == DxgiNotFound) break;
                            Check(outputResult);
                            try
                            {
                                OutputDescription description;
                                Check(Method<GetOutputDescription>(output, 7)(output, out description));
                                if (!description.AttachedToDesktop) continue;
                                Display display = null;
                                foreach (Display candidate in displays)
                                    if (String.Equals(candidate.Name, description.DeviceName, StringComparison.OrdinalIgnoreCase)) { display = candidate; break; }
                                if (display == null || captured.Contains(display.Name)) continue;
                                using (Bitmap frame = CaptureOutput(output, device, context, description, report))
                                {
                                    if (frame.Size != display.Bounds.Size) throw new InvalidOperationException("캡처 중 디스플레이 크기가 변경되었습니다.");
                                    using (Graphics graphics = Graphics.FromImage(result))
                                        graphics.DrawImageUnscaled(frame, display.Bounds.X - bounds.X, display.Bounds.Y - bounds.Y);
                                }
                                captured.Add(display.Name);
                            }
                            finally { Release(output); }
                        }
                    }
                    finally { Release(context); Release(device); Release(adapter); }
                }
                if (captured.Count != displays.Count) throw new NotSupportedException("현재 구성의 모든 디스플레이에서 Desktop Duplication을 사용할 수 없습니다.");
                return result;
            }
            catch { result.Dispose(); throw; }
            finally { Release(factory); }
        }

        private static Bitmap CaptureOutput(IntPtr output, IntPtr device, IntPtr context, OutputDescription description, DesktopCaptureReport report)
        {
            IntPtr output1 = IntPtr.Zero, duplication = IntPtr.Zero, resource = IntPtr.Zero, texture = IntPtr.Zero, staging = IntPtr.Zero;
            bool acquired = false, mapped = false;
            try
            {
                Guid outputId = Output1Id;
                Check(Marshal.QueryInterface(output, ref outputId, out output1));
                Check(Method<Duplicate>(output1, 22)(output1, device, out duplication));
                FrameInfo info;
                Check(Method<Acquire>(duplication, 8)(duplication, 120, out info, out resource));
                acquired = true;
                Rectangle outputBounds = Rectangle.FromLTRB(description.Coordinates.Left, description.Coordinates.Top,
                    description.Coordinates.Right, description.Coordinates.Bottom);
                // A hardware/driver cursor must not silently replace a successful DXGI frame with
                // a different backend. Keep the chosen pixels and let the caller avoid drawing it twice.
                report.ProtectedContentMasked |= info.ProtectedContentMaskedOut != 0;
                report.EmbeddedCursor |= HasEmbeddedCursor(info, outputBounds);
                report.Events.Add("DXGI output=" + outputBounds + "; present=" + (info.LastPresentTime != 0) + "; protected=" + (info.ProtectedContentMaskedOut != 0));
                Guid textureId = Texture2DId;
                Check(Marshal.QueryInterface(resource, ref textureId, out texture));
                TextureDescription textureDescription;
                Method<GetTextureDescription>(texture, 10)(texture, out textureDescription);
                if (textureDescription.Format != 87 || textureDescription.Width == 0 || textureDescription.Height == 0)
                    throw new NotSupportedException("지원하지 않는 Desktop Duplication 픽셀 형식입니다.");
                textureDescription.Usage = 3; // D3D11_USAGE_STAGING
                textureDescription.BindFlags = 0; textureDescription.CpuAccessFlags = 0x20000; textureDescription.MiscFlags = 0;
                Check(Method<CreateTexture>(device, 5)(device, ref textureDescription, IntPtr.Zero, out staging));
                Method<CopyResource>(context, 47)(context, staging, texture);
                Method<ContextAction>(context, 111)(context); // Submit copy before nonblocking Map.
                MappedSubresource pixels;
                Stopwatch wait = Stopwatch.StartNew();
                int mapResult;
                do
                {
                    mapResult = Method<MapResource>(context, 14)(context, staging, 0, 1, 0x100000, out pixels);
                    if (mapResult != DxgiStillDrawing) break;
                    Thread.Sleep(1);
                } while (wait.ElapsedMilliseconds < 100);
                Check(mapResult);
                mapped = true;
                Bitmap frame = ReadPixels(pixels.Data, checked((int)textureDescription.Width), checked((int)textureDescription.Height), checked((int)pixels.RowPitch), 4);
                try
                {
                    if (description.Rotation == 2) frame.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    else if (description.Rotation == 3) frame.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    else if (description.Rotation == 4) frame.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    return frame;
                }
                catch { frame.Dispose(); throw; }
            }
            finally
            {
                if (mapped) Method<UnmapResource>(context, 15)(context, staging, 0);
                Release(staging); Release(texture); Release(resource);
                if (acquired) Method<ReleaseFrame>(duplication, 14)(duplication);
                Release(duplication); Release(output1);
            }
        }

        private static bool HasEmbeddedCursor(FrameInfo info, Rectangle outputBounds)
        {
            if (info.LastMouseUpdateTime == 0 || info.PointerVisible != 0) return false;
            NativeCursorInfo cursor = new NativeCursorInfo();
            cursor.Size = Marshal.SizeOf(typeof(NativeCursorInfo));
            if (!GetCursorInfo(ref cursor)) return false;
            bool visible = (cursor.Flags & 1) != 0 && (cursor.Flags & 2) == 0 && cursor.Handle != IntPtr.Zero;
            return HasEmbeddedCursorMetadata(info.LastMouseUpdateTime, info.PointerVisible != 0,
                visible, new Point(cursor.X, cursor.Y), outputBounds);
        }

        internal static bool HasEmbeddedCursorMetadata(long mouseUpdateTime, bool separatePointerVisible,
            bool systemCursorVisible, Point systemCursor, Rectangle outputBounds)
        {
            // DXGI_OUTDUPL_FRAME_INFO.PointerPosition is undefined when LastMouseUpdateTime is zero.
            return mouseUpdateTime != 0 && !separatePointerVisible && systemCursorVisible &&
                outputBounds.Contains(systemCursor);
        }

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T));
        }
        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Release(IntPtr instance) { if (instance != IntPtr.Zero) Marshal.Release(instance); }
        private static IntPtr EnterPhysicalCoordinates()
        {
            try { return SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
        }
        private static void LeavePhysicalCoordinates(IntPtr previous)
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
        private static void FlushComposition()
        {
            try { DwmFlush(); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            GdiFlush();
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeCursorInfo
        {
            public int Size, Flags; public IntPtr Handle; public int X, Y;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
        {
            public int Size; public NativeRect Monitor, Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
        {
            public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OutputDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public NativeRect Coordinates; [MarshalAs(UnmanagedType.Bool)] public bool AttachedToDesktop;
            public uint Rotation; public IntPtr Monitor;
        }
        [StructLayout(LayoutKind.Sequential)] private struct FrameInfo
        {
            public long LastPresentTime, LastMouseUpdateTime; public uint AccumulatedFrames; public int RectsCoalesced, ProtectedContentMaskedOut;
            public int PointerX, PointerY, PointerVisible; public uint TotalMetadataBufferSize, PointerShapeBufferSize;
        }
        [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MappedSubresource { public IntPtr Data; public uint RowPitch, DepthPitch; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr self, uint index, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputDescription(IntPtr self, out OutputDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Duplicate(IntPtr self, IntPtr device, out IntPtr duplication);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Acquire(IntPtr self, uint milliseconds, out FrameInfo info, out IntPtr resource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseFrame(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetTextureDescription(IntPtr self, out TextureDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self, ref TextureDescription description, IntPtr data, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(IntPtr self, IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ContextAction(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapResource(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint flags, out MappedSubresource data);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapResource(IntPtr self, IntPtr resource, uint subresource);
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr state);

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorInfo(ref NativeCursorInfo cursor);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out uint featureLevel, out IntPtr context);
    }
}
