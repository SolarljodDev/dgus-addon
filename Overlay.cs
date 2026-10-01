using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BizDraw;
using BizDraw.Controls;
using BizDraw.Core;
using BizDraw.Objects;

namespace DgusPlus
{
    // Рисуется поверх холста DGUS после его собственной отрисовки.
    static class Overlay
    {
        public static DrawRectangle Hovered;

        // Шрифт подписи элементов — тот же, что в DrawRectangle.DrawText.
        static readonly Font LabelFont = new Font("宋体", 9f);
        static readonly Brush VpBrush = new SolidBrush(Color.FromArgb(0, 70, 170));
        static readonly Brush VpBrushWarn = new SolidBrush(Color.FromArgb(200, 0, 0));

        public static void OnPaint(object sender, PaintEventArgs e)
        {
            try { Paint((DrawArea)sender, e.Graphics); }
            catch (Exception ex) { Plus.Log(ex); }
        }

        static void Paint(DrawArea da, Graphics g)
        {
            Document doc = da.Document;
            if (doc == null) return;
            g.ResetTransform();
            g.ScaleTransform(da.Zoom, da.Zoom);

            if (Plus.Cfg.ShowVpLabels && Globel.ShowText && doc.TempDrawBtn)
            {
                foreach (VpEntry e in VpPanel.Entries)
                {
                    if (!e.IsAux && VpModel.Visible(doc, e)) DrawVpLabel(g, e);
                }
            }

            DrawRectangle h = Hovered;
            if (h != null && doc.Items.Contains(h))
            {
                Rectangle r = h.Rectangle;
                r.Inflate(2, 2);
                using (Brush b = new SolidBrush(Color.FromArgb(60, Theme.Accent)))
                    g.FillRectangle(b, r);
                using (Pen p = new Pen(Theme.Accent, Math.Max(1f, 2.5f / da.Zoom)))
                    g.DrawRectangle(p, r);
            }

            Snap.Paint(g, da);
        }

        // DGUS пишет "-Имя" над элементом; дописываем ", 5000" сразу за ним.
        public static void DrawVpLabel(Graphics g, VpEntry e)
        {
            Rectangle r = e.Obj.Rectangle;
            int y = r.Y - 10;
            if (y < 0) y = r.Bottom + 2;

            string prefix = "-" + e.Label;
            string full = prefix + ", " + e.RangeText;
            StringFormat sf = new StringFormat(StringFormat.GenericDefault);
            sf.SetMeasurableCharacterRanges(new CharacterRange[] {
                new CharacterRange(0, 1),
                new CharacterRange(prefix.Length, full.Length - prefix.Length) });
            Region[] regs = g.MeasureCharacterRanges(full, LabelFont, new RectangleF(0, 0, 4000, 100), sf);
            // DrawString добавляет одинаковый левый отступ к любой строке — вычитаем его.
            float x = r.X + regs[1].GetBounds(g).X - regs[0].GetBounds(g).X;
            g.DrawString(", " + e.RangeText, LabelFont,
                         e.Conflict == VpConflict.Overlap ? VpBrushWarn : VpBrush, x, y);
        }
    }
}
