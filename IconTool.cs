using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DgusPlus { internal static class IconTool
{
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr value);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo {
        public int cb; public string reserved, desktop, title;
        public int x, y, xSize, ySize, xChars, yChars, fill, flags;
        public short showWindow, reserved2Count; public IntPtr reserved2, stdin, stdout, stderr;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr process, thread; public uint pid, tid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int x, y; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreateDesktop(string name, string device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool EnumDesktopWindows(IntPtr desktop, WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wp, string lp);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string cwd, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private static IntPtr Find(IntPtr desktop, uint pid, string cls, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumDesktopWindows(desktop, delegate(IntPtr hwnd, IntPtr value) {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            if (owner != pid) return true;
            StringBuilder c = new StringBuilder(256), t = new StringBuilder(256);
            GetClassName(hwnd, c, c.Capacity); GetWindowText(hwnd, t, t.Capacity);
            if (c.ToString() == cls && (title == "" || t.ToString().Contains(title)) && IsWindowVisible(hwnd)) found = hwnd;
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }
    private static IntPtr Wait(IntPtr desktop, uint pid, string cls, string title)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < end) {
            IntPtr h = Find(desktop, pid, cls, title);
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(300);
        }
        throw new Exception("Window not found: " + cls + " " + title);
    }
    private static void Click(IntPtr hwnd, int x, int y)
    {
        Rect rect; GetWindowRect(hwnd, out rect);
        Point p = new Point(); p.x = rect.left + x; p.y = rect.top + y;
        ScreenToClient(hwnd, ref p);
        IntPtr lp = new IntPtr((p.y << 16) | (p.x & 0xffff));
        PostMessage(hwnd, 0x0200, IntPtr.Zero, lp); Thread.Sleep(100);
        PostMessage(hwnd, 0x0201, new IntPtr(1), lp); Thread.Sleep(80);
        PostMessage(hwnd, 0x0202, IntPtr.Zero, lp);
    }
    private static IntPtr Child(IntPtr parent, string cls, int id)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr hwnd, IntPtr value) {
            StringBuilder c = new StringBuilder(256); GetClassName(hwnd, c, c.Capacity);
            int control = GetDlgCtrlID(hwnd);
            if (c.ToString() == cls && control == id) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) throw new Exception("Control not found: " + cls + " " + id);
        return found;
    }
    internal static object Generate(string folder, string output, bool overwrite)
    {
        folder = System.IO.Path.GetFullPath(folder); output = System.IO.Path.GetFullPath(output);
        if (!System.IO.Directory.Exists(folder)) throw new Exception("Source folder does not exist");
        if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(output))) throw new Exception("Output folder does not exist");
        if (!output.EndsWith(".icl", StringComparison.OrdinalIgnoreCase)) throw new Exception("Output must have .icl extension");
        if (System.IO.File.Exists(output) && !overwrite) throw new Exception("Output exists; specify overwrite=true");
        string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output), "dgus-icon-" + Guid.NewGuid().ToString("N") + ".icl");
        string name = "DgusIconProbe_" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(name, null, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Exception("CreateDesktop error " + Marshal.GetLastWin32Error());
        ProcessInfo p = new ProcessInfo();
        try {
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOL");
            string exe = dir + @"\AllTool.exe";
            StartupInfo s = new StartupInfo(); s.cb = Marshal.SizeOf(typeof(StartupInfo)); s.desktop = name;
            s.flags = 1; s.showWindow = 5;
            if (!CreateProcess(exe, new StringBuilder("\"" + exe + "\" \"" + folder + "\" 0 1"), IntPtr.Zero, IntPtr.Zero,
                false, 0, IntPtr.Zero, dir, ref s, out p)) throw new Exception("CreateProcess error " + Marshal.GetLastWin32Error());
            IntPtr main = Wait(desktop, p.pid, "Qt5QWindowIcon", "DwinPCKits");
            Thread.Sleep(1000);
            Click(main, 104, 279);
            IntPtr dialog = Wait(desktop, p.pid, "#32770", "");
            IntPtr edit = Child(dialog, "Edit", 1152);
            IntPtr ok = Child(dialog, "Button", 1);
            SendMessage(edit, 0x000c, IntPtr.Zero, folder);
            Thread.Sleep(300);
            SendMessage(ok, 0x00f5, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(3000);
            Click(main, 230, 629); Thread.Sleep(500);
            Click(main, 150, 759);
            IntPtr save = Wait(desktop, p.pid, "#32770", "");
            IntPtr file = Child(save, "Edit", 1001);
            IntPtr saveButton = Child(save, "Button", 1);
            SendMessage(file, 0x000c, IntPtr.Zero, temp);
            Thread.Sleep(300);
            SendMessage(saveButton, 0x00f5, IntPtr.Zero, IntPtr.Zero);
            IntPtr info = Wait(desktop, p.pid, "Qt5QWindowIcon", "Information");
            PostMessage(info, 0x0100, new IntPtr(0x0d), IntPtr.Zero);
            PostMessage(info, 0x0101, new IntPtr(0x0d), IntPtr.Zero);
            Thread.Sleep(300);
            if (!System.IO.File.Exists(temp) || new System.IO.FileInfo(temp).Length == 0) throw new Exception("Icon Tool did not produce an ICL file");
            long size = new System.IO.FileInfo(temp).Length;
            if (System.IO.File.Exists(output)) System.IO.File.Replace(temp, output, null); else System.IO.File.Move(temp, output);
            System.Collections.Generic.Dictionary<string, object> result = new System.Collections.Generic.Dictionary<string, object>();
            result["file"] = output; result["bytes"] = size; result["blocks256k"] = (size + 262143) / 262144; return result;
        }
        finally {
            if (p.pid != 0) { try { Process.GetProcessById((int)p.pid).Kill(); Process.GetProcessById((int)p.pid).WaitForExit(3000); } catch { } }
            if (p.thread != IntPtr.Zero) CloseHandle(p.thread);
            if (p.process != IntPtr.Zero) CloseHandle(p.process);
            CloseDesktop(desktop);
            if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);
        }
    }
}

}
