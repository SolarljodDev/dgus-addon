using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using ContentAlignment = System.Drawing.ContentAlignment;

namespace DgusPlus
{
    // Системные флажки/переключатели всегда белые. После их отрисовки закрашиваем
    // квадратик (кружок) своим тёмным вариантом — текст и поведение остаются родными.
    class GlyphPainter : NativeWindow
    {
        const int WM_PAINT = 0x000F;
        readonly ButtonBase owner;

        public static void Attach(ButtonBase b)
        {
            if (ButtonExt.AppearanceOf(b) == Appearance.Button) return;
            GlyphPainter gp = new GlyphPainter(b);
            if (b.IsHandleCreated) gp.AssignHandle(b.Handle);
            b.HandleCreated += delegate { gp.AssignHandle(b.Handle); };
            b.HandleDestroyed += delegate { gp.ReleaseHandle(); };
        }

        GlyphPainter(ButtonBase b) { owner = b; }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT) Plus.Guard(Paint);
        }

        void Paint()
        {
            if (!owner.Visible || owner.IsDisposed) return;
            using (Graphics g = owner.CreateGraphics())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = GlyphRect(g);
                CheckBox cb = owner as CheckBox;
                bool on = cb != null ? cb.CheckState != CheckState.Unchecked : ((RadioButton)owner).Checked;
                Color fill = on ? Theme.Accent : Theme.Input;
                Color edge = on ? Theme.Accent : (owner.Enabled ? Theme.TextDim : Theme.Border);

                using (Brush bg = new SolidBrush(BackOf(owner))) g.FillRectangle(bg, Rectangle.Inflate(r, 1, 1));
                if (cb != null)
                {
                    using (GraphicsPath p = Round(r, 2))
                    {
                        using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                        using (Pen pen = new Pen(edge)) g.DrawPath(pen, p);
                    }
                    if (cb.CheckState == CheckState.Checked)
                        using (Pen pen = new Pen(Color.White, 1.8f))
                            g.DrawLines(pen, new PointF[] {
                                new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f),
                                new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f),
                                new PointF(r.X + r.Width * 0.78f, r.Y + r.Height * 0.30f) });
                    else if (cb.CheckState == CheckState.Indeterminate)
                        using (Brush b = new SolidBrush(Color.White))
                            g.FillRectangle(b, r.X + 3, r.Y + r.Height / 2 - 1, r.Width - 6, 2);
                }
                else
                {
                    using (Brush b = new SolidBrush(Theme.Input)) g.FillEllipse(b, r);
                    using (Pen pen = new Pen(edge)) g.DrawEllipse(pen, r);
                    if (on)
                        using (Brush b = new SolidBrush(Theme.Accent))
                            g.FillEllipse(b, Rectangle.Inflate(r, -3, -3));
                }
            }
        }

        // Где система нарисовала глиф: размер из темы, положение по CheckAlign.
        Rectangle GlyphRect(Graphics g)
        {
            Size s = owner is CheckBox
                ? CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal)
                : RadioButtonRenderer.GetGlyphSize(g, RadioButtonState.UncheckedNormal);
            ContentAlignment a = owner is CheckBox ? ((CheckBox)owner).CheckAlign : ((RadioButton)owner).CheckAlign;
            Rectangle c = owner.ClientRectangle;
            int x = c.X, y = c.Y + (c.Height - s.Height) / 2;
            if (a == ContentAlignment.MiddleRight || a == ContentAlignment.TopRight || a == ContentAlignment.BottomRight)
                x = c.Right - s.Width - 1;
            else if (a == ContentAlignment.MiddleCenter || a == ContentAlignment.TopCenter || a == ContentAlignment.BottomCenter)
                x = c.X + (c.Width - s.Width) / 2;
            if (a == ContentAlignment.TopLeft || a == ContentAlignment.TopCenter || a == ContentAlignment.TopRight) y = c.Y + 1;
            if (a == ContentAlignment.BottomLeft || a == ContentAlignment.BottomCenter || a == ContentAlignment.BottomRight)
                y = c.Bottom - s.Height - 1;
            return new Rectangle(x, y, s.Width - 1, s.Height - 1);
        }

        static Color BackOf(Control c)
        {
            for (; c != null; c = c.Parent)
                if (c.BackColor.A == 255) return c.BackColor;
            return Theme.Panel;
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class ButtonExt
    {
        public static Appearance AppearanceOf(ButtonBase b)
        {
            CheckBox c = b as CheckBox;
            if (c != null) return c.Appearance;
            RadioButton r = b as RadioButton;
            return r != null ? r.Appearance : System.Windows.Forms.Appearance.Normal;
        }
    }
}
