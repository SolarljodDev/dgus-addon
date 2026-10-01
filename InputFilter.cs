using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BizDraw.Controls;
using BizDraw.Core;
using BizDraw.Objects;
using BizDraw.Tools;

namespace DgusPlus
{
    // Перехват мыши и клавиатуры до DGUS: зум и прокрутка колесом, перемещение по полю, защита от случайного перетаскивания,
    // горячие клавиши отмены/повтора и масштаба.
    class InputFilter : IMessageFilter
    {
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102,
                  WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
                  WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205,
                  WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A,
                  WM_MOUSEHWHEEL = 0x020E;
        const int MK_LBUTTON = 0x0001;
        const int DragThreshold = 4;

        enum DragGuard { None, Block, DeadZone }

        DragGuard guard = DragGuard.None;
        Point downPt;

        bool panning;
        bool panByLeft;
        Point panStartCursor;
        Point panStartScroll;

        int wheelAccum;
        bool altUsed;   // Alt участвовал в перетаскивании (магнит) — его отпускание глотаем

        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr GetFocus();
        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);

        public bool PreFilterMessage(ref Message m)
        {
            try { return Handle(ref m); }
            catch (Exception ex) { Plus.Log(ex); return false; }
        }

        bool Handle(ref Message m)
        {
            int msg = m.Msg;
            if (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP) Undo.NoteInput(false);
            else if (msg == WM_KEYUP || msg == WM_CHAR) Undo.NoteInput(true);

            if (Plus.Space == null) return false;
            DrawArea da = Plus.CurrentDrawArea;
            Panel panel = Plus.ScrollPanel;
            if (da == null || panel == null || !da.IsHandleCreated) return false;

            // Alt+S больше не сохраняет (сохранение — Ctrl+S).
            if (msg == WM_SYSKEYDOWN && (Keys)(int)m.WParam == Keys.S && Form.ActiveForm == Plus.MainForm) return true;
            // Отпускание Alt после магнита не должно открывать меню окна.
            if (msg == WM_SYSKEYUP || (msg == WM_KEYUP && (Keys)(int)m.WParam == Keys.Menu))
            {
                if (altUsed && (Keys)(int)m.WParam == Keys.Menu)
                {
                    altUsed = false;
                    Snap.Reset(da);
                    return true;
                }
                if (Snap.Showing) Snap.Reset(da);
            }

            switch (msg)
            {
                case WM_KEYDOWN:
                    return OnKeyDown((Keys)(int)m.WParam);

                case WM_MOUSEWHEEL:
                case WM_MOUSEHWHEEL:
                    return OnWheel(ref m, panel, msg == WM_MOUSEHWHEEL);

                case WM_MBUTTONDOWN:
                    if (IsCanvas(m.HWnd, da, panel)) { StartPan(m.HWnd, panel, false); return true; }
                    break;

                case WM_MBUTTONUP:
                    if (panning && !panByLeft) { EndPan(); return true; }
                    break;

                case WM_LBUTTONDOWN:
                case WM_LBUTTONDBLCLK:
                    if (IsCanvas(m.HWnd, da, panel) && KeyDown(0x20) && !TextFocused())
                    {
                        StartPan(m.HWnd, panel, true);
                        return true;
                    }
                    if (m.HWnd == da.Handle) OnCanvasDown(m, da);
                    break;

                case WM_LBUTTONUP:
                    guard = DragGuard.None;
                    Snap.Reset(da);
                    if (panning && panByLeft) { EndPan(); return true; }
                    break;

                case WM_MOUSEMOVE:
                    if (panning) { DoPan(panel); return true; }
                    if (m.HWnd == da.Handle)
                    {
                        if (FilterDrag(m)) return true;
                        if (KeyDown(0x12)) altUsed = true;
                        if (Snap.OnMove(m, da)) return true;
                    }
                    break;
            }
            return false;
        }

        static bool IsCanvas(IntPtr hwnd, DrawArea da, Panel panel)
        {
            return hwnd == da.Handle || hwnd == panel.Handle ||
                   (Plus.Space.tempeditor != null && hwnd == Plus.Space.tempeditor.Handle);
        }

        static bool KeyDown(int vk) { return (GetKeyState(vk) & 0x8000) != 0; }

        // Фокус в поле ввода — горячие клавиши оставляем ему (Ctrl+Z там — отмена текста).
        static bool TextFocused()
        {
            Control c = Control.FromChildHandle(GetFocus());
            for (; c != null; c = c.Parent)
            {
                if (c is TextBoxBase || c is UpDownBase) return true;
                ComboBox cb = c as ComboBox;
                if (cb != null && cb.DropDownStyle != ComboBoxStyle.DropDownList) return true;
                if (c is DrawArea) return false;
            }
            return false;
        }


        bool OnKeyDown(Keys key)
        {
            if (Form.ActiveForm != Plus.MainForm || !Plus.ProjectOpen) return false;
            bool ctrl = KeyDown(0x11), shift = KeyDown(0x10), alt = KeyDown(0x12);
            if (ctrl && !alt && !shift && key == Keys.S) { Plus.SaveProject(); return true; }
            if (!ctrl || alt || TextFocused()) return false;

            if (key == Keys.Z && !shift) { Undo.DoUndo(); return true; }
            if (key == Keys.Y || (key == Keys.Z && shift)) { Undo.DoRedo(); return true; }
            if (key == Keys.D0 || key == Keys.NumPad0) { Plus.ZoomFit(); return true; }
            if (key == Keys.D1 || key == Keys.NumPad1) { ZoomCenter(1f); return true; }
            if (key == Keys.Oemplus || key == Keys.Add) { ZoomCenter(Plus.NextZoom(Plus.CurrentDrawArea.Zoom, true)); return true; }
            if (key == Keys.OemMinus || key == Keys.Subtract) { ZoomCenter(Plus.NextZoom(Plus.CurrentDrawArea.Zoom, false)); return true; }
            return false;
        }

        static void ZoomCenter(float z)
        {
            Panel p = Plus.ScrollPanel;
            Plus.ZoomAt(z, p.PointToScreen(new Point(p.ClientSize.Width / 2, p.ClientSize.Height / 2)));
        }


        bool OnWheel(ref Message m, Panel panel, bool horizontal)
        {
            Point sp = Cursor.Position;
            if (!panel.Visible || !panel.RectangleToScreen(panel.ClientRectangle).Contains(sp)) return false;
            // Под курсором должен быть именно холст, а не всплывающее окно поверх него.
            Control under = Control.FromChildHandle(WindowFromPoint(sp));
            if (under == null || !(under == panel || panel.Contains(under))) return false;

            int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            Keys mods = Control.ModifierKeys;
            bool scrollMode = !Plus.Cfg.WheelZoom ^ (mods == Keys.Control);

            if (horizontal || mods == Keys.Shift)
            {
                ScrollBy(panel, horizontal ? delta : -delta, 0);
                return true;
            }
            if (scrollMode)
            {
                ScrollBy(panel, 0, -delta);
                return true;
            }

            // Тачпады шлют мелкие дельты — копим до одного «щелчка» колеса.
            wheelAccum += delta;
            if (Math.Abs(wheelAccum) < 120) return true;
            bool up = wheelAccum > 0;
            wheelAccum = 0;
            DrawArea da = Plus.CurrentDrawArea;
            Plus.ZoomAt(Plus.NextZoom(da.Zoom, up), sp);
            return true;
        }

        static void ScrollBy(Panel p, int dx, int dy)
        {
            CanvasCache.Touch();
            Point cur = new Point(-p.AutoScrollPosition.X, -p.AutoScrollPosition.Y);
            p.AutoScrollPosition = new Point(Math.Max(0, cur.X + dx), Math.Max(0, cur.Y + dy));
        }


        void StartPan(IntPtr hwnd, Panel panel, bool byLeft)
        {
            panning = true;
            panByLeft = byLeft;
            panStartCursor = Cursor.Position;
            panStartScroll = new Point(-panel.AutoScrollPosition.X, -panel.AutoScrollPosition.Y);
            CanvasCache.Begin();
            SetCapture(hwnd);
            Cursor.Current = Cursors.SizeAll;
        }

        void DoPan(Panel panel)
        {
            MouseButtons need = panByLeft ? MouseButtons.Left : MouseButtons.Middle;
            if ((Control.MouseButtons & need) == 0) { EndPan(); return; }
            Point c = Cursor.Position;
            panel.AutoScrollPosition = new Point(
                Math.Max(0, panStartScroll.X - (c.X - panStartCursor.X)),
                Math.Max(0, panStartScroll.Y - (c.Y - panStartCursor.Y)));
            Cursor.Current = Cursors.SizeAll;
        }

        void EndPan()
        {
            panning = false;
            CanvasCache.End();
            ReleaseCapture();
            Cursor.Current = Cursors.Default;
        }


        void OnCanvasDown(Message m, DrawArea da)
        {
            guard = DragGuard.None;
            int dp = (int)(long)m.LParam;
            Snap.NoteDown(da, (short)(dp & 0xFFFF), (short)((dp >> 16) & 0xFFFF));
            if (!Plus.Cfg.ClickToSelect) return;
            ToolPointer tp = da.ActiveTool as ToolPointer;
            Document doc = da.Document;
            if (tp == null || tp.OnClick || doc == null) return;

            int lp = (int)(long)m.LParam;
            downPt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            // Та же формула, что в DrawArea.GetScaledMouseEvent.
            Point pt = new Point(Convert.ToInt32(downPt.X / da.Zoom), Convert.ToInt32(downPt.Y / da.Zoom));

            GraphicsList items = doc.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Selected && items[i].HitTest(pt) > 0) { guard = DragGuard.DeadZone; return; }
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].HitTest(pt) == 0)
                {
                    guard = items[i].Selected ? DragGuard.DeadZone : DragGuard.Block;
                    return;
                }
            }
        }

        bool FilterDrag(Message m)
        {
            if (guard == DragGuard.None || ((int)(long)m.WParam & MK_LBUTTON) == 0) return false;
            if (guard == DragGuard.Block) return true;
            int lp = (int)(long)m.LParam;
            int x = (short)(lp & 0xFFFF), y = (short)((lp >> 16) & 0xFFFF);
            if (Math.Abs(x - downPt.X) < DragThreshold && Math.Abs(y - downPt.Y) < DragThreshold) return true;
            // Порог пройден: отпускаем. DGUS считает смещение от точки нажатия,
            // так что элемент сразу встанет куда надо, без «рывка».
            guard = DragGuard.None;
            return false;
        }
    }
}
