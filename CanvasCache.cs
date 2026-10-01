using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BizDraw.Controls;

namespace DgusPlus
{
    // DGUS медленно рисует холст, и при прокрутке открывающиеся полосы «размазываются». На время перемещения холст
    // один раз рендерится в битмап, а WM_PAINT отдаёт его куски (содержимое страницы в это время не меняется).
    class CanvasCache : NativeWindow
    {
        const int WM_PAINT = 0x000F;
        const long MaxPixels = 40L * 1000 * 1000;   // ~160 МБ — больше не кэшируем

        static CanvasCache instance;
        static Timer idle;

        DrawArea area;
        Bitmap bmp;
        int users;          // перемещение мышью + прокрутка колесом могут пересекаться

        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public bool fErase; public int l, t, r, b;
            public bool fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);

        public static void Begin()
        {
            DrawArea da = Plus.CurrentDrawArea;
            if (da == null || !da.IsHandleCreated) return;
            if (instance != null && instance.area != da) instance.Drop();
            if (instance == null) instance = new CanvasCache();
            instance.Acquire(da);
        }

        public static void End()
        {
            if (instance != null) instance.Release();
        }

        // Прокрутка колесом: кэш живёт, пока колесо крутят, и отпускается через 300 мс тишины.
        public static void Touch()
        {
            if (idle == null)
            {
                idle = new Timer();
                idle.Interval = 300;
                idle.Tick += delegate { idle.Stop(); End(); };
            }
            if (!idle.Enabled) Begin();
            idle.Stop();
            idle.Start();
        }

        void Acquire(DrawArea da)
        {
            users++;
            if (bmp != null) return;
            if ((long)da.Width * da.Height > MaxPixels || da.Width <= 0 || da.Height <= 0) return;
            area = da;
            bmp = new Bitmap(da.Width, da.Height);
            da.DrawToBitmap(bmp, new Rectangle(0, 0, da.Width, da.Height));
            AssignHandle(da.Handle);
        }

        void Release()
        {
            if (--users > 0) return;
            users = 0;
            Drop();
        }

        void Drop()
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (area != null && !area.IsDisposed) area.Invalidate();
            area = null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PAINT && bmp != null)
            {
                PAINTSTRUCT ps;
                IntPtr hdc = BeginPaint(m.HWnd, out ps);
                try
                {
                    using (Graphics g = Graphics.FromHdc(hdc))
                    {
                        Rectangle r = Rectangle.FromLTRB(ps.l, ps.t, ps.r, ps.b);
                        g.DrawImage(bmp, r, r, GraphicsUnit.Pixel);
                    }
                }
                finally { EndPaint(m.HWnd, ref ps); }
                return;
            }
            base.WndProc(ref m);
        }
    }
}
