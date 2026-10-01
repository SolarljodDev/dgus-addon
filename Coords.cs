using System;
using System.Drawing;
using System.Windows.Forms;
using BizDraw.Controls;

namespace DgusPlus
{
    // Координаты курсора в левом нижнем углу рабочей области вместо подсказки у курсора.
    static class Coords
    {
        static Label label;

        public static void Attach(DrawArea da)
        {
            ToolTip tip = R.Get(da, "toolTip1") as ToolTip;
            if (tip != null) tip.Active = false;

            da.MouseMove += delegate(object s, MouseEventArgs e)
            {
                Plus.Guard(delegate { Show(da, e.X, e.Y); });
            };
            da.MouseLeave += delegate { if (label != null) label.Visible = false; };
        }

        static void Show(DrawArea da, int x, int y)
        {
            Panel p = Plus.ScrollPanel;
            if (p == null || p.Parent == null) return;
            Control host = p.Parent;
            if (label == null)
            {
                label = new Label();
                label.AutoSize = true;
                label.Padding = new Padding(6, 2, 6, 2);
                label.Font = new Font("Segoe UI", 9f);
            }
            if (label.Parent != host) host.Controls.Add(label);

            label.BackColor = Theme.Panel;
            label.ForeColor = Theme.Text;
            label.Text = "X " + Convert.ToInt32(x / da.Zoom) + "   Y " + Convert.ToInt32(y / da.Zoom);
            label.Location = new Point(8, host.ClientSize.Height - label.Height - 8);
            label.Visible = true;
            label.BringToFront();
        }
    }
}
