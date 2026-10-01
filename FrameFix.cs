using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DgusPlus
{
    // У окна DGUS остаётся белая верхняя рамка в 8 px — убираем её из неклиентской области, заголовок встаёт вплотную к верху.
    // Левая, правая и нижняя рамки остаются: за них окно тянется.
    class FrameFix : NativeWindow
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCACTIVATE = 0x86;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        static FrameFix instance;

        static bool Active { get { return Plus.Cfg != null && Plus.Cfg.Theme != "original"; } }

        public static void Install(Form f)
        {
            if (instance != null || f == null) return;
            instance = new FrameFix();
            instance.AssignHandle(f.Handle);
            f.HandleDestroyed += delegate { instance.ReleaseHandle(); instance = null; };
            Refresh();
        }

        public static void Refresh()
        {
            if (instance == null || instance.Handle == IntPtr.Zero) return;
            SetWindowPos(instance.Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero && Active)
            {
                // rgrc[0] на входе — предлагаемое окно; на выходе — клиентская область.
                int top = Marshal.ReadInt32(m.LParam, 4);
                base.WndProc(ref m);
                Marshal.WriteInt32(m.LParam, 4, top);
                return;
            }
            // Смена активности окна (модальный диалог, заливка в дисплей…): по умолчанию система
            // перерисовывает рамку окна и белая полоса возвращается. lParam = -1 — не трогать рамку.
            if (m.Msg == WM_NCACTIVATE && Active) m.LParam = (IntPtr)(-1);
            base.WndProc(ref m);
        }
    }
}
