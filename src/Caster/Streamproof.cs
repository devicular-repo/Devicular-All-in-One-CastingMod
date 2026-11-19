using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DevicularCaster
{
    /// <summary>
    /// Streamproof: the menus are drawn into a hidden picture instead of onto the game's screen, copied off the
    /// graphics card every frame, and shown in a window of their own that recordings (OBS, Discord...) leave out. You
    /// see the menus as normal; your stream doesn't. The caster menu (Tab) and the Auto Ref panel both go through it.
    ///
    /// Each frame, every streamproof menu draws into the picture (<see cref="Layer"/>). At the end of the frame
    /// (<see cref="EndFrame"/>) the graphics card is asked for one copy, written straight into one of a few buffers
    /// that are used again and again. When it arrives, a background thread (with a few helpers) turns it into the
    /// window's picture. Nothing is made per frame, and the game's main thread only asks for the copies.
    /// </summary>
    internal sealed class Streamproof
    {
        public const string Tip = "Hides the menu and the Auto Ref panel from OBS, Discord and other recordings. You still see them on your screen, and the overlays stay on stream.";

        /// <summary>The one in use (the caster's), so the Auto Ref panel can draw through it too.</summary>
        public static Streamproof Current;

        public bool Enabled;
        public string Error = "";
        /// <summary>The caster menu's tooltips, where this one's tip (or why it can't work) goes.</summary>
        public Dictionary<string, string> Tips;

        private HiddenWindow window;
        private RenderTexture rt;
        private bool showing;
        private int generation;
        private int drawnFrame = -1, clearedFrame = -1;

        // The buffers the copies go into. Up to two copies are on their way at once; one waits to be shown and one is
        // being shown.
        private const int PoolSize = 4, MaxInFlight = 2;
        private readonly List<Shot> all = new List<Shot>();
        private readonly Stack<Shot> free = new Stack<Shot>();
        private readonly ConcurrentQueue<Shot> returned = new ConcurrentQueue<Shot>();
        private int inFlight;

        public void Toggle()
        {
            Enabled = !Enabled;
            if (!Enabled) return;
            Error = "";
            SetTip(Tip);
        }

        private void SetTip(string tip)
        {
            if (Tips != null) Tips["Streamproof"] = tip;
        }

        /// <summary>
        /// Draws a menu that's hidden from recordings while Streamproof is on. Off, or for any event but painting, it
        /// just draws as normal. Call it from OnGUI, only while that menu is open.
        /// </summary>
        public void Layer(Action draw)
        {
            if (!Enabled || Event.current.type != EventType.Repaint || !Ready())
            {
                draw();
                return;
            }
            Prepare(Screen.width, Screen.height);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            try
            {
                if (clearedFrame != Time.frameCount)
                {
                    clearedFrame = Time.frameCount;
                    GL.Clear(true, true, Color.clear);
                }
                draw();
                Markers();
            }
            finally
            {
                RenderTexture.active = prev;
            }
            drawnFrame = Time.frameCount;
        }

        /// <summary>
        /// At the end of every frame, after all the menus are drawn: asks for a copy of the picture, or hides the
        /// window when no streamproof menu was open this frame.
        /// </summary>
        public void EndFrame()
        {
            Collect();
            if (drawnFrame != Time.frameCount)
            {
                Hide();
                return;
            }
            showing = true;
            Request();
        }

        public void Shutdown()
        {
            Hide();
            bool stopped = true;
            if (window != null) stopped = window.Dispose();
            window = null;
            if (inFlight > 0)
            {
                try { AsyncGPUReadback.WaitAllRequests(); }
                catch (Exception) { }
            }
            // Only freed when nothing can still be using them.
            if (stopped)
                foreach (var s in all) s.Dispose();
            all.Clear();
            free.Clear();
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
        }

        private bool Ready()
        {
            if (window == null)
            {
                if (!SystemInfo.supportsAsyncGPUReadback) return Fail("Your graphics card can't do this.");
                IntPtr game;
                try { game = HiddenWindow.FindGameWindow(); }
                catch (Exception) { return Fail("Only works on Windows."); }
                if (game == IntPtr.Zero) return Fail("Couldn't find the game window.");
                window = new HiddenWindow(game, returned.Enqueue);
            }
            string err = window.Error;
            if (err == null && window.Lost > 30) err = "Couldn't hide the menu on this PC.";
            if (err == null) return true;
            Hide();
            window.Dispose();
            window = null;
            return Fail(err);
        }

        private bool Fail(string why)
        {
            Enabled = false;
            Error = why;
            SetTip(why);
            return false;
        }

        private void Hide()
        {
            if (!showing) return;
            showing = false;
            generation++;
            window?.Hide();
        }

        private void Prepare(int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h && rt.IsCreated()) return;
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            var format = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            rt = new RenderTexture(w, h, format, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                name = "Devicular Streamproof",
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
        }

        private static void Markers()
        {
            var matrix = GUI.matrix;
            var color = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = new Color(1f, 0f, 1f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, 1f, 1f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(1f, 0f, 1f, 1f), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = color;
        }

        /// <summary>Buffers the window has finished with go back on the free list (or are freed, if the screen size changed).</summary>
        private void Collect()
        {
            while (returned.TryDequeue(out var s))
            {
                if (rt != null && s.Width == rt.width && s.Height == rt.height && !s.Disposed) free.Push(s);
                else Retire(s);
            }
        }

        private void Request()
        {
            if (window == null || rt == null || inFlight >= MaxInFlight) return;
            var shot = Rent(rt.width, rt.height);
            // Every buffer is busy (the window is behind): skip this frame's copy rather than make another one.
            if (shot == null) return;
            shot.Gen = generation;
            shot.Target = window;
            inFlight++;
            try
            {
                AsyncGPUReadback.RequestIntoNativeArray(ref shot.Data, rt, 0, shot.Callback);
            }
            catch (Exception)
            {
                inFlight--;
                free.Push(shot);
            }
        }

        private Shot Rent(int w, int h)
        {
            while (free.Count > 0)
            {
                var s = free.Pop();
                if (s.Width == w && s.Height == h && !s.Disposed) return s;
                Retire(s);
            }
            if (all.Count >= PoolSize) return null;
            var shot = new Shot(this, w, h);
            all.Add(shot);
            return shot;
        }

        private void Retire(Shot s)
        {
            all.Remove(s);
            s.Dispose();
        }

        /// <summary>A copy has arrived (on the main thread): it goes to the window, or back to the free list if it's stale.</summary>
        internal void Done(Shot shot, AsyncGPUReadbackRequest r)
        {
            if (inFlight > 0) inFlight--;
            if (shot.Disposed) return;
            if (r.hasError || shot.Gen != generation || shot.Target != window || window == null)
            {
                free.Push(shot);
                return;
            }
            window.Push(shot);
        }
    }

    /// <summary>One reusable buffer for a full-screen copy of the menus.</summary>
    internal sealed class Shot : IDisposable
    {
        public NativeArray<uint> Data;
        public readonly int Width, Height;
        public readonly IntPtr Pixels;
        /// <summary>Made once, so asking for a copy makes nothing new.</summary>
        public readonly Action<AsyncGPUReadbackRequest> Callback;
        public int Gen;
        public HiddenWindow Target;
        public bool Disposed;

        public unsafe Shot(Streamproof owner, int width, int height)
        {
            Width = width;
            Height = height;
            Data = new NativeArray<uint>(width * height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Pixels = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(Data);
            Callback = r => owner.Done(this, r);
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (Data.IsCreated) Data.Dispose();
        }
    }

    internal sealed class HiddenWindow
    {
        private const string ClassName = "DevicularStreamproof";
        private static readonly byte[] Root = MakeRoot();

        private readonly IntPtr game;
        private readonly Thread thread;
        private readonly object sync = new object();
        private readonly Action<Shot> giveBack;
        private IntPtr wake;
        private Shot ready;
        private bool show;
        private volatile bool quit;
        private volatile string error;
        private volatile int lost;
        private IntPtr hwnd, dc, dib, oldBitmap, bits;
        private int dibW, dibH;
        private bool visible;
        private bool flip = true;
        private bool squared;

        // Helpers that each turn a band of rows into the window's picture, so it keeps up with the game's frame rate.
        private readonly Workers workers;
        private readonly int parts;
        private readonly int[] boxes;
        private IntPtr jobSrc, jobDst;
        private int jobW, jobH;
        private bool jobFlip, jobSquared;

        public HiddenWindow(IntPtr game, Action<Shot> giveBack)
        {
            this.game = game;
            this.giveBack = giveBack;
            parts = Mathf.Clamp(Environment.ProcessorCount / 4, 1, 4);
            boxes = new int[parts * 4];
            workers = new Workers(parts - 1, ConvertPart);
            wake = CreateEvent(IntPtr.Zero, false, false, null);
            thread = new Thread(Run) { IsBackground = true, Name = "Devicular Streamproof" };
            thread.Start();
        }

        public string Error => error;
        public int Lost => lost;
        public IntPtr Handle => hwnd;

        public void Push(Shot shot)
        {
            lock (sync)
            {
                // A picture the window hasn't shown yet is replaced by the newer one, and its buffer goes back.
                if (ready != null) giveBack(ready);
                ready = shot;
                show = true;
                if (wake != IntPtr.Zero) SetEvent(wake);
            }
        }

        public void Hide()
        {
            lock (sync)
            {
                show = false;
                if (ready != null)
                {
                    giveBack(ready);
                    ready = null;
                }
                if (wake != IntPtr.Zero) SetEvent(wake);
            }
        }

        /// <summary>Closes the window. True once its thread has stopped (so nothing is still reading the buffers).</summary>
        public bool Dispose()
        {
            quit = true;
            lock (sync)
            {
                if (wake != IntPtr.Zero) SetEvent(wake);
            }
            if (thread.IsAlive && Thread.CurrentThread != thread) thread.Join(1000);
            bool stopped = !thread.IsAlive;
            workers.Dispose();
            lock (sync)
            {
                if (ready != null)
                {
                    giveBack(ready);
                    ready = null;
                }
            }
            return stopped;
        }

        public static IntPtr FindGameWindow()
        {
            uint pid = GetCurrentProcessId();
            IntPtr found = IntPtr.Zero;
            var name = new StringBuilder(64);
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint owner);
                if (owner != pid || !IsWindowVisible(h)) return true;
                name.Length = 0;
                GetClassName(h, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private void Run()
        {
            try
            {
                if (!Open()) return;
                var handles = new[] { wake };
                while (!quit)
                {
                    MsgWaitForMultipleObjects(1, handles, false, 100, QS_ALLINPUT);
                    Pump();
                    if (quit) break;
                    Shot f;
                    bool want;
                    lock (sync)
                    {
                        f = ready;
                        ready = null;
                        want = show;
                    }
                    if (!want || !IsWindow(game) || IsIconic(game))
                    {
                        SetVisible(false);
                        if (f != null) giveBack(f);
                        continue;
                    }
                    if (f == null) continue;
                    try
                    {
                        Present(f);
                    }
                    finally
                    {
                        giveBack(f);
                    }
                }
            }
            catch (Exception)
            {
                if (error == null) error = "The menu window stopped working.";
            }
            finally
            {
                Close();
            }
        }

        private bool Open()
        {
            try { SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(game)); }
            catch (Exception) { }
            IntPtr inst = GetModuleHandle(null);
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
                hInstance = inst,
                lpszClassName = ClassName
            };
            RegisterClassEx(ref wc);
            hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "Devicular Caster Menu",
                WS_POPUP, 0, 0, 1, 1, game, IntPtr.Zero, inst, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                error = "Couldn't open the menu window.";
                return false;
            }
            if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE) || !GetWindowDisplayAffinity(hwnd, out uint affinity) || affinity != WDA_EXCLUDEFROMCAPTURE)
            {
                error = "Needs Windows 10 (version 2004) or newer.";
                return false;
            }
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc != IntPtr.Zero) return true;
            error = "Couldn't open the menu window.";
            return false;
        }

        private void Close()
        {
            if (dc != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(dc, oldBitmap);
                DeleteDC(dc);
            }
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
            dc = dib = oldBitmap = bits = hwnd = IntPtr.Zero;
            visible = false;
            lock (sync)
            {
                if (wake != IntPtr.Zero) CloseHandle(wake);
                wake = IntPtr.Zero;
            }
        }

        private static void Pump()
        {
            while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private void SetVisible(bool on)
        {
            if (on == visible || hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, on ? SW_SHOWNOACTIVATE : SW_HIDE);
            visible = on;
        }

        private void Present(Shot f)
        {
            var origin = new POINT();
            if (!ClientToScreen(game, ref origin)) return;
            if (!EnsureBitmap(f.Width, f.Height)) return;
            // The window only covers the part of the screen the menus are drawn on, not the whole screen: Windows
            // has much less to blend with the game every time it's updated.
            if (!Convert(f, out int x0, out int y0, out int x1, out int y1))
            {
                SetVisible(false);
                return;
            }
            var dst = new POINT { x = origin.x + x0, y = origin.y + y0 };
            var size = new SIZE { cx = x1 - x0 + 1, cy = y1 - y0 + 1 };
            var src = new POINT { x = x0, y = y0 };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, ULW_ALPHA)) SetVisible(true);
        }

        private bool EnsureBitmap(int w, int h)
        {
            if (dib != IntPtr.Zero && w == dibW && h == dibH) return true;
            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(dc, oldBitmap);
                oldBitmap = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero)
            {
                DeleteObject(dib);
                dib = IntPtr.Zero;
            }
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            dib = CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
            oldBitmap = SelectObject(dc, dib);
            dibW = w;
            dibH = h;
            return true;
        }

        private static bool IsMarker(uint p) => (p & 0xFFFFFFu) == 0xFF00FFu && (p >> 24) >= 250u;

        /// <summary>
        /// Copies the picture into the window's bitmap (split across the helpers), and finds the box around
        /// everything that isn't see-through (x0..x1, y0..y1, from the top left). False when there's nothing to show.
        /// </summary>
        private unsafe bool Convert(Shot f, out int x0, out int y0, out int x1, out int y1)
        {
            int w = f.Width, h = f.Height;
            x0 = y0 = int.MaxValue;
            x1 = y1 = -1;
            GdiFlush();
            uint* src = (uint*)f.Pixels;
            uint* bottom = src + (long)(h - 1) * w;
            if (IsMarker(*src)) flip = false;
            else if (IsMarker(*bottom)) flip = true;
            uint* top = flip ? bottom : src;
            if (IsMarker(*top))
            {
                lost = 0;
                if (w > 1)
                {
                    uint half = top[1] >> 24;
                    if (half > 0) squared = half < 96u;
                    top[1] = 0;
                }
                top[0] = 0;
            }
            else lost = lost + 1;

            jobSrc = f.Pixels;
            jobDst = bits;
            jobW = w;
            jobH = h;
            jobFlip = flip;
            jobSquared = squared;
            workers.Run(parts);

            for (int i = 0; i < parts; i++)
            {
                int bx0 = boxes[i * 4], by0 = boxes[i * 4 + 1], bx1 = boxes[i * 4 + 2], by1 = boxes[i * 4 + 3];
                if (bx1 < 0) continue;
                if (bx0 < x0) x0 = bx0;
                if (by0 < y0) y0 = by0;
                if (bx1 > x1) x1 = bx1;
                if (by1 > y1) y1 = by1;
            }
            return x1 >= x0 && y1 >= y0;
        }

        /// <summary>One band of rows (run by the window thread and the helpers at the same time).</summary>
        private unsafe void ConvertPart(int part)
        {
            int w = jobW, h = jobH;
            int rows = (h + parts - 1) / parts;
            int from = part * rows, to = Math.Min(h, from + rows);
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
            uint* src = (uint*)jobSrc;
            uint* dst = (uint*)jobDst;
            bool fl = jobFlip, sq = jobSquared;
            fixed (byte* root = Root)
            {
                for (int y = from; y < to; y++)
                {
                    uint* s = fl ? src + (long)(h - 1 - y) * w : src + (long)y * w;
                    uint* d = dst + (long)y * w;
                    int first = -1, last = -1;
                    for (int x = 0; x < w; x++)
                    {
                        uint p = s[x];
                        if (p == 0)
                        {
                            d[x] = 0;
                            continue;
                        }
                        uint r = p & 0xFFu, g = (p >> 8) & 0xFFu, b = (p >> 16) & 0xFFu, a = p >> 24;
                        if (sq) a = root[a];
                        if (r > a) a = r;
                        if (g > a) a = g;
                        if (b > a) a = b;
                        d[x] = b | (g << 8) | (r << 16) | (a << 24);
                        if (first < 0) first = x;
                        last = x;
                    }
                    if (first < 0) continue;
                    if (first < bx0) bx0 = first;
                    if (last > bx1) bx1 = last;
                    if (y < by0) by0 = y;
                    by1 = y;
                }
            }
            boxes[part * 4] = bx0;
            boxes[part * 4 + 1] = by0;
            boxes[part * 4 + 2] = bx1;
            boxes[part * 4 + 3] = by1;
        }

        /// <summary>
        /// A few helper threads that wait for work. <see cref="Run"/> gives each one a part and does part 0 itself,
        /// then waits for them. Made once; nothing is made per frame.
        /// </summary>
        private sealed class Workers : IDisposable
        {
            private readonly Thread[] threads;
            private readonly AutoResetEvent[] go;
            private readonly CountdownEvent done = new CountdownEvent(1);
            private readonly Action<int> job;
            private volatile bool quit;

            public Workers(int count, Action<int> job)
            {
                this.job = job;
                threads = new Thread[count];
                go = new AutoResetEvent[count];
                for (int i = 0; i < count; i++)
                {
                    int part = i + 1;
                    go[i] = new AutoResetEvent(false);
                    var signal = go[i];
                    threads[i] = new Thread(() => Loop(part, signal)) { IsBackground = true, Name = "Devicular Streamproof " + part };
                    threads[i].Start();
                }
            }

            private void Loop(int part, AutoResetEvent signal)
            {
                while (true)
                {
                    signal.WaitOne();
                    if (quit) return;
                    try { job(part); }
                    catch (Exception) { }
                    finally { done.Signal(); }
                }
            }

            public void Run(int parts)
            {
                int helpers = Math.Min(parts - 1, threads.Length);
                if (helpers > 0)
                {
                    done.Reset(helpers);
                    for (int i = 0; i < helpers; i++) go[i].Set();
                }
                try { job(0); }
                finally
                {
                    if (helpers > 0) done.Wait();
                }
            }

            public void Dispose()
            {
                quit = true;
                foreach (var g in go) g.Set();
            }
        }

        private static byte[] MakeRoot()
        {
            var t = new byte[256];
            for (int i = 0; i < 256; i++) t[i] = (byte)Math.Min(255, Math.Round(Math.Sqrt(i / 255.0) * 255.0));
            return t;
        }

        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        private const uint ULW_ALPHA = 2;
        private const uint PM_REMOVE = 1;
        private const uint QS_ALLINPUT = 0x04FF;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, uint key, ref BLENDFUNCTION blend, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll")]
        private static extern bool SetEvent(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern bool GdiFlush();
    }
}
