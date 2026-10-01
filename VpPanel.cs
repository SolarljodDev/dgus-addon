using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using BizDraw.Controls;
using BizDraw.Core;
using BizDraw.Objects;
using WeifenLuo.WinFormsUI.Docking;

namespace DgusPlus
{
    // Пристыкованная панель «VP»: все занятые адреса текущей страницы.
    // Наведение на строку подсвечивает элемент, клик — выделяет его на холсте.
    class VpPanel : DockContent
    {
        public static VpPanel Instance;
        public static List<VpEntry> Entries = new List<VpEntry>();

        static string signature = "";
        static bool dirty = true;
        static DockPanel dockedTo;

        readonly ListView list;
        readonly Label summary;
        readonly TextBox filter;
        bool syncing;

        public static void MarkDirty() { dirty = true; }

        public static void Poll()
        {
            Document doc = Plus.CurrentDoc;
            if (doc == null || !Plus.ProjectOpen) return;

            string sig = Signature(doc);
            if (sig != signature || dirty)
            {
                signature = sig;
                dirty = false;
                Entries = VpModel.Collect(doc);
                if (Instance != null) Instance.Rebuild();
                if (Plus.CurrentDrawArea != null) Plus.CurrentDrawArea.Invalidate();
            }
            if (Instance != null) Instance.SyncSelection();
            EnsureShown();
        }

        // Дешёвая «подпись» страницы: меняется при смене VP, имён, позиций, выделения.
        static string Signature(Document doc)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(doc.GetHashCode()).Append('|').Append(doc.Showtype).Append('|');
            foreach (DrawObject o in doc.Items)
            {
                DrawRectangle r = o as DrawRectangle;
                if (r == null || r.ConfigObject == null) continue;
                object cfg = r.ConfigObject;
                sb.Append(R.Get(cfg, "VarStrPoint")).Append(',')
                  .Append(R.Get(cfg, "Var_Name")).Append(',')
                  .Append(R.Get(cfg, "FVarType")).Append(',')
                  .Append(R.Get(cfg, "Text_Length")).Append(',')
                  .Append(R.Get(cfg, "VP_Len_Max")).Append(';');
            }
            return sb.ToString();
        }

        // Показываем панель под «Property», как только открыт проект.
        static void EnsureShown()
        {
            DockPanel dp = R.Get(Plus.Space, "dockPanel1") as DockPanel;
            if (dp == null || dp == dockedTo) return;
            DockContent prop = R.Get(Plus.Space, "m_Btnproperty") as DockContent;
            if (prop == null || prop.Pane == null || prop.DockPanel != dp) return;

            dockedTo = dp;
            if (Instance == null || Instance.IsDisposed) Instance = new VpPanel();
            Instance.Show(prop.Pane, DockAlignment.Bottom, 0.4);
            Instance.Rebuild();
        }

        public static void ShowPanel()
        {
            if (Instance == null || Instance.IsDisposed || dockedTo == null) { dockedTo = null; EnsureShown(); return; }
            Instance.Show(dockedTo);
            Instance.Activate();
        }

        VpPanel()
        {
            Text = "VP";
            TabText = "VP";
            HideOnClose = true;
            DockAreas = DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockBottom | DockAreas.Float;
            Font = Theme.UiFont;

            summary = new Label();
            summary.Dock = DockStyle.Top;
            summary.Height = 26;
            summary.Padding = new Padding(8, 0, 8, 0);
            summary.TextAlign = ContentAlignment.MiddleLeft;

            filter = new TextBox();
            filter.Dock = DockStyle.Top;
            filter.BorderStyle = BorderStyle.FixedSingle;
            filter.TextChanged += delegate { Rebuild(); };
            SetCue(filter, "Filter: address, type or name");

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.BorderStyle = BorderStyle.None;
            list.OwnerDraw = true;
            list.Columns.Add("VP", 88);
            list.Columns.Add("Type", 100);
            list.Columns.Add("Name", 120);
            list.DrawColumnHeader += OnDrawHeader;
            list.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            list.DrawSubItem += OnDrawSubItem;
            list.MouseMove += OnListMouseMove;
            list.MouseLeave += delegate { SetHover(null); };
            list.SelectedIndexChanged += OnListSelect;
            list.DoubleClick += delegate { ScrollToSelected(); };
            list.Resize += delegate { FitColumns(); };
            DoubleBuffer(list);

            Controls.Add(list);
            Controls.Add(filter);
            Controls.Add(summary);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Panel;
            summary.BackColor = Theme.Panel;
            summary.ForeColor = Theme.TextDim;
            filter.BackColor = Theme.Input;
            filter.ForeColor = Theme.Text;
            list.BackColor = Theme.Panel;
            list.ForeColor = Theme.Text;
            list.Invalidate();
        }

        void FitColumns()
        {
            int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width;
            if (w > 40) list.Columns[2].Width = w;
        }

        void Rebuild()
        {
            string f = filter.Text.Trim().ToLowerInvariant();
            list.BeginUpdate();
            list.Items.Clear();
            int overlaps = 0, shown = 0;
            Document doc = Plus.CurrentDoc;
            foreach (VpEntry e in Entries)
            {
                if (e.Conflict == VpConflict.Overlap) overlaps++;
                if (doc != null && !VpModel.Visible(doc, e)) continue;
                if (f.Length > 0 && e.RangeText.ToLowerInvariant().IndexOf(f) < 0 &&
                    e.Type.ToLowerInvariant().IndexOf(f) < 0 && e.Name.ToLowerInvariant().IndexOf(f) < 0)
                    continue;
                ListViewItem it = new ListViewItem(new string[] { e.RangeText, e.Type, e.Name });
                it.Tag = e;
                if (e.Conflict != VpConflict.None)
                    it.ToolTipText = (e.Conflict == VpConflict.Overlap ? "Overlaps with: " : "Shares address with: ") + e.ConflictWith;
                list.Items.Add(it);
                shown++;
            }
            list.ShowItemToolTips = true;
            list.EndUpdate();
            FitColumns();
            summary.Text = "Elements with VP: " + shown +
                           (overlaps > 0 ? "   ⚠ overlaps: " + overlaps : "");
            summary.ForeColor = overlaps > 0 ? Theme.Warn : Theme.TextDim;
            SyncSelection();
        }


        void OnDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (Brush b = new SolidBrush(Theme.PanelAlt)) e.Graphics.FillRectangle(b, e.Bounds);
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            Rectangle r = e.Bounds;
            r.X += 6;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, r, Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            VpEntry v = (VpEntry)e.Item.Tag;
            bool hover = v.Obj == Overlay.Hovered;
            Color back = e.Item.Selected ? Theme.Selection : hover ? Theme.Hover : Theme.Panel;
            using (Brush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);

            // Служебный AP (вспомогательный адрес элемента) — приглушённая строка, серая метка.
            Color fore = v.IsAux ? Theme.TextDim : Theme.Text;
            if (e.ColumnIndex == 0)
            {
                Color mark = v.Conflict == VpConflict.Overlap ? Theme.Warn :
                             v.Conflict == VpConflict.Shared ? Theme.Shared :
                             v.IsAux ? Theme.Border :
                             v.IsInput ? Theme.InputMark : Theme.OutputMark;
                using (Brush b = new SolidBrush(mark))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 3, 3, e.Bounds.Height - 6);
                if (v.Conflict == VpConflict.Overlap) fore = Theme.Warn;
            }
            else if (e.ColumnIndex == 1) fore = Theme.TextDim;

            Rectangle r = e.Bounds;
            r.X += e.ColumnIndex == 0 ? 8 : 4;
            r.Width -= 8;
            Font font = e.ColumnIndex == 0 ? MonoFont : list.Font;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, r, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        static readonly Font MonoFont = Theme.UiFont;


        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            ListViewItem it = list.GetItemAt(e.X, e.Y);
            SetHover(it == null ? null : ((VpEntry)it.Tag).Obj);
        }

        void SetHover(DrawRectangle obj)
        {
            if (Overlay.Hovered == obj) return;
            Overlay.Hovered = obj;
            list.Invalidate();
            DrawArea da = Plus.CurrentDrawArea;
            if (da != null) da.Invalidate();
        }

        void OnListSelect(object sender, EventArgs e)
        {
            if (syncing) return;
            DrawArea da = Plus.CurrentDrawArea;
            Document doc = Plus.CurrentDoc;
            if (da == null || doc == null || list.SelectedItems.Count == 0) return;
            syncing = true;
            try
            {
                doc.Items.UnselectAll();
                foreach (ListViewItem it in list.SelectedItems)
                    ((VpEntry)it.Tag).Obj.SetSelectedState(true);
                da.Refresh();
            }
            finally { syncing = false; }
        }

        void SyncSelection()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                foreach (ListViewItem it in list.Items)
                {
                    bool sel = ((VpEntry)it.Tag).Obj.Selected;
                    if (it.Selected != sel) it.Selected = sel;
                }
            }
            finally { syncing = false; }
        }

        void ScrollToSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            DrawArea da = Plus.CurrentDrawArea;
            Panel p = Plus.ScrollPanel;
            Rectangle r = ((VpEntry)list.SelectedItems[0].Tag).Obj.Rectangle;
            Point center = new Point((int)((r.X + r.Width / 2) * da.Zoom), (int)((r.Y + r.Height / 2) * da.Zoom));
            Point inPanel = p.PointToClient(da.PointToScreen(center));
            Point scroll = new Point(-p.AutoScrollPosition.X, -p.AutoScrollPosition.Y);
            p.AutoScrollPosition = new Point(Math.Max(0, scroll.X + inPanel.X - p.ClientSize.Width / 2),
                                             Math.Max(0, scroll.Y + inPanel.Y - p.ClientSize.Height / 2));
        }


        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        static void SetCue(TextBox tb, string text)
        {
            tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); };
        }

        static void DoubleBuffer(Control c)
        {
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(c, true, null);
        }
    }
}
