using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace ChachaCapture
{
    internal sealed class ElementSnapshot
    {
        internal IntPtr Root;
        internal Rectangle Bounds;
    }

    /// <summary>Passive UIA geometry collection. Provider calls never run on the capture UI thread.</summary>
    internal static class ElementSnapshots
    {
        private sealed class Job
        {
            internal readonly object Gate = new object();
            internal readonly List<ElementSnapshot> Rectangles = new List<ElementSnapshot>();
            internal Thread Worker;
        }

        private static readonly object Gate = new object();
        private static Job active;

        internal static IList<ElementSnapshot> Collect(IList<IntPtr> handles, Rectangle desktopBounds)
        {
            Job job;
            lock (Gate)
            {
                // A broken provider can block indefinitely. Keep at most one isolated background worker.
                if (active == null || !active.Worker.IsAlive)
                {
                    job = new Job();
                    List<IntPtr> roots = new List<IntPtr>();
                    IntPtr foreground = GetForegroundWindow();
                    if (handles.Contains(foreground)) roots.Add(foreground);
                    foreach (IntPtr handle in handles)
                    {
                        if (!roots.Contains(handle)) roots.Add(handle);
                        if (roots.Count >= 8) break;
                    }
                    job.Worker = new Thread(delegate() { CollectWorker(job, roots, desktopBounds); });
                    job.Worker.IsBackground = true;
                    job.Worker.Name = "Chacha UIA geometry snapshot";
                    job.Worker.SetApartmentState(ApartmentState.MTA);
                    active = job;
                    job.Worker.Start();
                }
                else job = active;
            }
            job.Worker.Join(150);
            lock (job.Gate) return new List<ElementSnapshot>(job.Rectangles);
        }

        private static void CollectWorker(Job job, IList<IntPtr> roots, Rectangle desktopBounds)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            int count = 0;
            int fetched = 0;
            try
            {
                CacheRequest cache = new CacheRequest();
                cache.Add(AutomationElement.BoundingRectangleProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                cache.TreeScope = TreeScope.Element;
                TreeWalker walker = TreeWalker.ControlViewWalker;
                foreach (IntPtr handle in roots)
                {
                    if (elapsed.ElapsedMilliseconds >= 250 || fetched >= 1500) break;
                    try
                    {
                        AutomationElement root;
                        using (cache.Activate()) root = AutomationElement.FromHandle(handle);
                        fetched++;
                        Stack<AutomationElement> pending = new Stack<AutomationElement>();
                        pending.Push(root);
                        while (pending.Count > 0 && count < 1500 && elapsed.ElapsedMilliseconds < 250)
                        {
                            AutomationElement node = pending.Pop();
                            count++;
                            try
                            {
                                System.Windows.Rect rect = node.Cached.BoundingRectangle;
                                if (!node.Cached.IsOffscreen && !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
                                    !Double.IsInfinity(rect.Width) && !Double.IsInfinity(rect.Height) &&
                                    !Double.IsNaN(rect.X) && !Double.IsNaN(rect.Y))
                                {
                                    Rectangle bounds = Rectangle.Intersect(desktopBounds, Rectangle.FromLTRB(
                                        (int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top),
                                        (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom)));
                                    if (bounds.Width > 1 && bounds.Height > 1)
                                        lock (job.Gate) job.Rectangles.Add(new ElementSnapshot { Root = handle, Bounds = bounds });
                                }
                                if (elapsed.ElapsedMilliseconds >= 250 || fetched >= 1500) continue;
                                AutomationElement child = walker.GetFirstChild(node, cache);
                                List<AutomationElement> children = new List<AutomationElement>();
                                while (child != null && fetched < 1500 && elapsed.ElapsedMilliseconds < 250)
                                {
                                    children.Add(child);
                                    fetched++;
                                    if (fetched >= 1500) break;
                                    child = walker.GetNextSibling(child, cache);
                                }
                                // Preserve native tree order while using a bounded stack.
                                for (int i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
                            }
                            catch (ElementNotAvailableException) { }
                            catch (COMException) { }
                            catch (InvalidOperationException) { }
                            catch (ArgumentException) { }
                        }
                    }
                    catch (ElementNotAvailableException) { }
                    catch (COMException) { }
                    catch (InvalidOperationException) { }
                    catch (ArgumentException) { }
                }
            }
            catch (Exception) { /* Optional geometry detection always has a native HWND fallback. */ }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    }
}
