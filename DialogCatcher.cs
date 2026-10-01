using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DgusPlus
{
    // На время вызова DGUS из MCP (например, Generate) перехватывает его MessageBox: забирает текст и закрывает окно.
    // Хук ставится на текущий поток (поток интерфейса DGUS) и снимается после вызова.
    class DialogCatcher : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, int thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool EndDialog(IntPtr h, IntPtr result);

        const int WH_CBT = 5, HCBT_ACTIVATE = 5, IDOK = 1;

        public readonly List<string> Messages = new List<string>();
        public event Action<string> Caught;
        readonly Predicate<string> filter;   // null — закрывать все MessageBox
        readonly HookProc proc;
        readonly IntPtr hook;
        readonly List<IntPtr> pending = new List<IntPtr>();
        readonly List<IntPtr> traced = new List<IntPtr>();
        readonly Timer timer = new Timer();

        public DialogCatcher() : this(null) { }

        public DialogCatcher(Predicate<string> filter)
        {
            this.filter = filter;
            proc = Proc;
            hook = SetWindowsHookEx(WH_CBT, proc, IntPtr.Zero, GetCurrentThreadId());
            // Закрываем не из хука (окно ещё только активируется, а в фоне Windows не даёт
            // ему стать активным, и клик/команда теряются), а таймером: WM_TIMER приходит
            // и внутри модального цикла MessageBox, в том же потоке, где EndDialog законен.
            timer.Interval = 30;
            timer.Tick += delegate { CloseTick(); };
        }

        IntPtr Proc(int code, IntPtr w, IntPtr l)
        {
            if (code == HCBT_ACTIVATE)
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassName(w, cls, cls.Capacity);
                if (cls.ToString() == "#32770" && !pending.Contains(w))   // стандартный MessageBox
                {
                    StringBuilder text = new StringBuilder(1024);
                    GetWindowText(GetDlgItem(w, 0xFFFF), text, text.Capacity);   // статический текст сообщения
                    string msg = text.ToString().Trim();
                    if (filter == null || filter(msg))
                    {
                        Messages.Add(msg);
                        pending.Add(w);
                        timer.Start();
                        if (Caught != null) Caught(msg);
                    }
                }
            }
            return CallNextHookEx(hook, code, w, l);
        }

        void CloseTick()
        {
            foreach (IntPtr w in pending.ToArray())
            {
                if (!IsWindow(w)) { pending.Remove(w); continue; }
                IntPtr btn = FindWindowEx(w, IntPtr.Zero, "Button", null);
                int id = btn != IntPtr.Zero ? GetDlgCtrlID(btn) : IDOK;
                bool ok = EndDialog(w, (IntPtr)id);
                if (!traced.Contains(w)) { traced.Add(w); Trace("EndDialog id=" + id + " -> " + ok); }
            }
            if (pending.Count == 0) timer.Stop();
        }

        static void Trace(string s)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DgusPlus.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + "DialogCatcher: " + s + Environment.NewLine);
            }
            catch { }
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
            UnhookWindowsHookEx(hook);
        }
    }
}
