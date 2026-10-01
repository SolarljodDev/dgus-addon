using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BizDraw.Controls;
using BizDraw.Core;
using BizDraw.Objects;

namespace DgusPlus
{
    static class Arrange
    {
        enum Act { Left, HCenter, Right, Top, VCenter, Bottom, DistH, DistV, SameW, SameH, SameBoth }

        // Порядок выделения: эталоном для «одинаковый размер» служит первый выделенный.
        static readonly List<DrawRectangle> order = new List<DrawRectangle>();
        static GraphicsList hookedItems;

        static ToolStripMenuItem alignMenu, distMenu, sizeMenu;
        static ContextMenuStrip menu;
        static DrawRectangle keyObj;   // выделенный элемент под курсором при открытии меню — эталон размера

        public static List<DrawRectangle> Selected(Document doc)
        {
            List<DrawRectangle> r = new List<DrawRectangle>();
            if (doc == null) return r;
            for (int i = 0; i < doc.Items.Count; i++)
            {
                DrawRectangle o = doc.Items[i] as DrawRectangle;
                if (o != null && o.Selected) r.Add(o);
            }
            return r;
        }

        public static Rectangle Union(List<DrawRectangle> list)
        {
            Rectangle u = list[0].Rectangle;
            for (int i = 1; i < list.Count; i++) u = Rectangle.Union(u, list[i].Rectangle);
            return u;
        }

        public static void Poll()
        {
            Document doc = Plus.CurrentDoc;
            if (doc == null) return;
            if (doc.Items != hookedItems)
            {
                hookedItems = doc.Items;
                order.Clear();
            }
            if (legacyCompat == null) legacyCompat = delegate { Guard(UpdateOrder); };
            foreach (PageRef p in Pages.All()) PurgeOwnHandlers(p.Doc);
            UpdateOrder();
        }

        // Метод этого делегата (<Poll>b__0) нужен BinaryFormatter, чтобы читать страницы, сохранённые сборкой 1.0.
        static EventHandler legacyCompat;

        // Подписки на события элементов DGUS попадают в сохранённый .tft (BinaryFormatter пишет делегаты),
        // и файл перестаёт читаться другой сборкой. Сборка 1.0 подписывалась так (метод <Poll>b__0) —
        // убираем такие обработчики из уже загруженных страниц, а сам метод ниже оставляем,
        // чтобы проекты, сохранённые 1.0, по-прежнему открывались.
        static void PurgeOwnHandlers(Document doc)
        {
            if (doc == null) return;
            FieldInfo f = typeof(GraphicsList).GetField("SelectedObjectChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Delegate all = f == null ? null : f.GetValue(doc.Items) as Delegate;
            if (all == null) return;
            foreach (Delegate d in all.GetInvocationList())
                if (d.Method.DeclaringType == typeof(Arrange)) doc.Items.SelectedObjectChanged -= (EventHandler)d;
        }

        static void Guard(MethodInvoker a) { Plus.Guard(a); }

        static void UpdateOrder()
        {
            Document doc = Plus.CurrentDoc;
            if (doc == null) return;
            List<DrawRectangle> sel = Selected(doc);
            order.RemoveAll(delegate(DrawRectangle o) { return !sel.Contains(o); });
            foreach (DrawRectangle o in sel) if (!order.Contains(o)) order.Add(o);
        }

        public static void Attach(Control da)
        {
            menu = R.Get(da, "myNewMenu1") as ContextMenuStrip;
            if (menu == null || menu.Tag as string == "plus") return;
            menu.Tag = "plus";

            // Родные пункты «Выровнять»/«Одинаковые» работают по схеме «потом кликни эталон» — прячем.
            ToolStripItem oldAlign = R.Get(da, "toolStripMenuItem1") as ToolStripItem;
            ToolStripItem oldSame = R.Get(da, "toolStripMenuItem2") as ToolStripItem;
            ToolStripItem sep1 = R.Get(da, "toolStripSeparator1") as ToolStripItem;
            ToolStripItem sep2 = R.Get(da, "toolStripSeparator2") as ToolStripItem;
            if (oldAlign != null) oldAlign.Visible = false;
            if (oldSame != null) oldSame.Visible = false;
            if (sep1 != null) sep1.Visible = false;
            if (sep2 != null) sep2.Visible = false;

            ToolStripItem model = oldAlign ?? (menu.Items.Count > 0 ? menu.Items[0] : null);

            alignMenu = Sub("Align", model,
                Item("Left", Act.Left), Item("Center", Act.HCenter), Item("Right", Act.Right),
                null,
                Item("Top", Act.Top), Item("Middle", Act.VCenter), Item("Bottom", Act.Bottom));
            distMenu = Sub("Distribute", model,
                Item("Horizontally", Act.DistH), Item("Vertically", Act.DistV));
            sizeMenu = Sub("Size", model,
                Item("Same width", Act.SameW), Item("Same height", Act.SameH),
                Item("Same size", Act.SameBoth));

            menu.AutoSize = true;
            menu.Items.Insert(0, alignMenu);
            menu.Items.Insert(1, distMenu);
            menu.Items.Insert(2, sizeMenu);
            menu.Items.Insert(3, new ToolStripSeparator());
            menu.Opening += delegate { Guard(OnOpening); };
            Restyle();
        }

        public static void Restyle() { Theme.StyleContextMenu(menu); }

        static ToolStripMenuItem Sub(string text, ToolStripItem model, params ToolStripItem[] items)
        {
            ToolStripMenuItem m = new ToolStripMenuItem();
            Loc.Bind(delegate(string s) { m.Text = s; }, text);
            Style(m, model);
            foreach (ToolStripItem it in items)
            {
                ToolStripItem add = it ?? new ToolStripSeparator();
                if (it != null) Style(add, model);
                m.DropDownItems.Add(add);
            }
            return m;
        }

        static ToolStripMenuItem Item(string text, Act act)
        {
            ToolStripMenuItem m = new ToolStripMenuItem();
            Loc.Bind(delegate(string s) { m.Text = s; }, text);
            Act a = act;
            m.Click += delegate { Guard(delegate { Run(a); }); };
            if (act == Act.SameW || act == Act.SameH || act == Act.SameBoth)
                Loc.Bind(delegate(string s) { m.ToolTipText = s; }, "The reference is the element you right-clicked");
            return m;
        }

        static void Style(ToolStripItem it, ToolStripItem model)
        {
            if (model == null) return;
            it.Font = model.Font;


        }

        static void OnOpening()
        {
            UpdateOrder();
            int n = order.Count;
            alignMenu.Enabled = n >= 1;
            distMenu.Enabled = n >= 3;
            sizeMenu.Enabled = n >= 2;
            keyObj = ObjectUnderCursor();
        }

        static DrawRectangle ObjectUnderCursor()
        {
            DrawArea da = Plus.CurrentDrawArea;
            if (da == null) return null;
            Point p = da.PointToClient(Control.MousePosition);
            p = new Point(Convert.ToInt32(p.X / da.Zoom), Convert.ToInt32(p.Y / da.Zoom));
            for (int i = 0; i < da.Document.Items.Count; i++)
            {
                DrawRectangle o = da.Document.Items[i] as DrawRectangle;
                if (o != null && o.Selected && o.HitTest(p) == 0) return o;
            }
            return null;
        }

        static void Run(Act act)
        {
            Document doc = Plus.CurrentDoc;
            if (doc == null) return;
            UpdateOrder();
            List<DrawRectangle> sel = Selected(doc);
            sel.RemoveAll(delegate(DrawRectangle o) { return o.bLocked; });
            if (sel.Count == 0) return;

            Undo.BeforeEdit(doc);
            switch (act)
            {
                case Act.DistH: DistributeH(sel); break;
                case Act.DistV: DistributeV(sel); break;
                case Act.SameW: case Act.SameH: case Act.SameBoth: SameSize(doc, sel, act); break;
                default: Align(doc, sel, act); break;
            }
            Undo.AfterEdit(doc);
            doc.SetDirtyFlag(true);
            Plus.Space.TempDocument_SelectedObjectChanged(null, EventArgs.Empty);
            Plus.CurrentDrawArea.Refresh();
            VpPanel.MarkDirty();
        }

        static void Put(DrawRectangle o, int x, int y, int w, int h)
        {
            o.SetRectangle(x, y, w, h);
        }

        static void Align(Document doc, List<DrawRectangle> sel, Act act)
        {
            // Для одного элемента «по чему выравнивать» — страница; для нескольких — их общая рамка.
            Rectangle all = Union(Selected(doc));
            Rectangle box = Selected(doc).Count == 1 ? new Rectangle(0, 0, doc.Width, doc.Height) : all;
            foreach (DrawRectangle o in sel)
            {
                Rectangle r = o.Rectangle;
                int x = r.X, y = r.Y;
                switch (act)
                {
                    case Act.Left: x = box.Left; break;
                    case Act.HCenter: x = box.Left + (box.Width - r.Width) / 2; break;
                    case Act.Right: x = box.Right - r.Width; break;
                    case Act.Top: y = box.Top; break;
                    case Act.VCenter: y = box.Top + (box.Height - r.Height) / 2; break;
                    case Act.Bottom: y = box.Bottom - r.Height; break;
                }
                Put(o, x, y, r.Width, r.Height);
            }
        }

        static void DistributeH(List<DrawRectangle> sel)
        {
            if (sel.Count < 3) return;
            sel.Sort(delegate(DrawRectangle a, DrawRectangle b) { return a.Rectangle.X.CompareTo(b.Rectangle.X); });
            Rectangle u = Union(sel);
            int sum = 0;
            foreach (DrawRectangle o in sel) sum += o.Rectangle.Width;
            double gap = (u.Width - sum) / (double)(sel.Count - 1);
            double x = u.Left;
            foreach (DrawRectangle o in sel)
            {
                Rectangle r = o.Rectangle;
                Put(o, (int)Math.Round(x), r.Y, r.Width, r.Height);
                x += r.Width + gap;
            }
        }

        static void DistributeV(List<DrawRectangle> sel)
        {
            if (sel.Count < 3) return;
            sel.Sort(delegate(DrawRectangle a, DrawRectangle b) { return a.Rectangle.Y.CompareTo(b.Rectangle.Y); });
            Rectangle u = Union(sel);
            int sum = 0;
            foreach (DrawRectangle o in sel) sum += o.Rectangle.Height;
            double gap = (u.Height - sum) / (double)(sel.Count - 1);
            double y = u.Top;
            foreach (DrawRectangle o in sel)
            {
                Rectangle r = o.Rectangle;
                Put(o, r.X, (int)Math.Round(y), r.Width, r.Height);
                y += r.Height + gap;
            }
        }

        static void SameSize(Document doc, List<DrawRectangle> sel, Act act)
        {
            DrawRectangle refObj = keyObj != null && keyObj.Selected ? keyObj : order.Count > 0 ? order[0] : sel[0];
            Rectangle rr = refObj.Rectangle;
            foreach (DrawRectangle o in sel)
            {
                if (o == refObj) continue;
                Rectangle r = o.Rectangle;
                int w = act == Act.SameH ? r.Width : rr.Width;
                int h = act == Act.SameW ? r.Height : rr.Height;
                Put(o, r.X, r.Y, w, h);
            }
        }
    }
}
