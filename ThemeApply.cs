using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BizDraw.Controls;
using WeifenLuo.WinFormsUI.Docking;

namespace DgusPlus
{
    // Перекраска окон DGUS. Правило: меняем только нейтральные (серые/белые/чёрные) цвета,
    // цветные метки и образцы цвета не трогаем. Тёмные нейтральные (лента, заголовок)
    // становятся «хромом», светлые — фоном панели/поля ввода в зависимости от роли контрола.
    static partial class Theme
    {
        public static Color Chrome = Color.FromArgb(30, 31, 34);
        public static Color ChromeText = Color.FromArgb(223, 225, 229);

        static readonly Dictionary<Control, bool> hooked = new Dictionary<Control, bool>();
        static readonly Dictionary<DockPanel, bool> skinned = new Dictionary<DockPanel, bool>();
        // Шрифт, которым DGUS рисует свои панели, — им же пишем в своих.
        public static readonly Font UiFont = new Font("微软雅黑", 9f);

        static bool On { get { return Plus.Cfg.Theme != "original"; } }

        public static void Poll()
        {
            if (!On) return;
            EnsureHook();
            foreach (Form f in Application.OpenForms) ApplyTree(f);
            foreach (DockPanel dp in FindDockPanels()) SkinDock(dp);
        }

        public static void Reapply()
        {
            LoadPalette();
            if (VpPanel.Instance != null) VpPanel.Instance.ApplyTheme();
            FrameFix.Refresh();
            Arrange.Restyle();
            if (Plus.MainForm != null) StyleRibbon(R.Get(Plus.MainForm, "tabControlEx1") as TabControl);
        }


        static void ApplyTree(Control c)
        {
            if (c == null || c.IsDisposed) return;
            if (!hooked.ContainsKey(c))
            {
                hooked[c] = true;
                c.ControlAdded += OnControlAdded;
                c.Disposed += delegate(object s, EventArgs e) { hooked.Remove((Control)s); };
                try { ApplyOne(c); }
                catch (Exception ex) { Plus.Log(ex); }
            }
            if (Skip(c)) return;
            foreach (Control child in c.Controls) ApplyTree(child);
        }

        static void OnControlAdded(object sender, ControlEventArgs e)
        {
            Plus.Guard(delegate { ApplyTree(e.Control); });
        }

        // Холст и собственные контролы надстройки не трогаем.
        static bool Skip(Control c)
        {
            return c is DrawArea || c is VpPanel || c.Name == "dgusplus";
        }

        static void ApplyOne(Control c)
        {
            string tn = c.GetType().Name;

            if (Skip(c) || tn == "MyNewMenu") return;

            if (c is BizDrawClient.TabControlEx) { StyleRibbon((TabControl)c); return; }
            if (c is TabPage && c.Parent is BizDrawClient.TabControlEx) return;
            if (c is BizDrawClient.MyButton) return;

            if (IsTitleBar(c)) { StyleTitle(c); return; }

            if (c is DocumentArea)
            {
                DocumentArea da = (DocumentArea)c;
                da.BackColor = Canvas;
                da.BorderStyle = BorderStyle.None;
                da.Paint += PaintPageShadow;
                // DGUS сам двигает/растягивает холст (зум, смена страницы) — тень должна ехать следом.
                da.drawArea1.LocationChanged += delegate { da.Invalidate(); };
                da.drawArea1.SizeChanged += delegate { da.Invalidate(); };
                return;
            }
            if (c is DW_Panel) { c.BackColor = Canvas; return; }

            BizDraw.PropertyFrm.MyNewBtn nb = c as BizDraw.PropertyFrm.MyNewBtn;
            if (nb != null) { StyleNewBtn(nb); return; }

            DataGridView dgv = c as DataGridView;
            if (dgv != null) { StyleGrid(dgv); return; }

            PropertyGrid pg = c as PropertyGrid;
            if (pg != null)
            {
                pg.BackColor = Panel; pg.ViewBackColor = Input; pg.ViewForeColor = Text;
                pg.LineColor = PanelAlt; pg.HelpBackColor = Panel; pg.HelpForeColor = TextDim;
                pg.CategoryForeColor = Text;
                return;
            }

            ToolStrip ts = c as ToolStrip;
            if (ts != null)
            {
                ts.BackgroundImage = null;   // у Images View фон — светлая картинка
                if (Dark)
                    foreach (ToolStripItem it in ts.Items)
                        if (it.Image != null) { Image i = LightenMono(it.Image); if (i != null) it.Image = i; }
                ts.RenderMode = ToolStripRenderMode.Professional;
                ts.Renderer = new ToolStripProfessionalRenderer(new Colors());
                ts.BackColor = MapBack(ts.BackColor, Panel);
                ts.ForeColor = ForeFor(ts.BackColor, ts.ForeColor);
                foreach (ToolStripItem it in ts.Items) StyleMenuItem(it);
                return;
            }

            Color role = Panel;
            if (c is TextBoxBase || c is UpDownBase || c is ComboBox || c is ListBox ||
                c is ListView || c is TreeView)
                role = Input;
            else if (c is ButtonBase && !(c is CheckBox) && !(c is RadioButton))
                role = Button;

            // Метки и флажки берут фон родителя, если свой — нейтральный.
            if ((c is Label || c is CheckBox || c is RadioButton) && IsNeutral(c.BackColor))
                c.BackColor = Color.Transparent;
            else
                c.BackColor = MapBack(c.BackColor, role);

            Color back = EffectiveBack(c);
            c.ForeColor = ForeFor(back, c.ForeColor);

            if (Dark)
            {
                RecolorIcons(c);
                DarkNative(c);
                if (c is CheckBox || c is RadioButton) GlyphPainter.Attach((ButtonBase)c);
                if (c.GetType().Name == "UpDownButtons") SpinPainter.Attach(c);
            }

            LinkLabel ll = c as LinkLabel;
            if (ll != null) { ll.LinkColor = Accent; ll.ActiveLinkColor = Accent; ll.VisitedLinkColor = Accent; }

            Button b = c as Button;
            if (b != null && role == Button)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = Selection;
            }
            TextBox tbx = c as TextBox;
            if (tbx != null && tbx.BorderStyle == BorderStyle.Fixed3D) tbx.BorderStyle = BorderStyle.FixedSingle;
            UpDownBase ud = c as UpDownBase;
            if (ud != null && ud.BorderStyle == BorderStyle.Fixed3D) ud.BorderStyle = BorderStyle.FixedSingle;
            ComboBox combo = c as ComboBox;
            if (combo != null && Dark) combo.FlatStyle = FlatStyle.Standard;   // тёмная системная тема DarkMode_CFD
            ListView lv = c as ListView;
            if (lv != null && lv.BorderStyle == BorderStyle.Fixed3D) lv.BorderStyle = BorderStyle.FixedSingle;
            Panel pnl = c as Panel;
            if (pnl != null && pnl.BorderStyle == BorderStyle.Fixed3D) pnl.BorderStyle = BorderStyle.FixedSingle;
            UserControl uc = c as UserControl;
            if (uc != null && uc.BorderStyle == BorderStyle.Fixed3D) uc.BorderStyle = BorderStyle.None;
        }

        // Большие иконки New/Open file на Welcome. Цвета наведения у них — свойства, которые
        // DWIN задал в дизайнере: (64,64,64) под мышью и (217,217,217) после ухода мыши.
        // Уход мыши к тому же жёстко красит подпись в чёрный — возвращаем цвет темы сразу после.
        static readonly FieldInfo newBtnFont =
            typeof(BizDraw.PropertyFrm.MyNewBtn).GetField("fontColor", BindingFlags.Instance | BindingFlags.NonPublic);

        static void StyleNewBtn(BizDraw.PropertyFrm.MyNewBtn b)
        {
            Color back = EffectiveBack(b.Parent);
            b.BackColorLeave = back;
            b.BackColorMove = Hover;
            b.BackColorM = back;
            if (Dark && b.ImageM != null) { Image i = LightenMono(b.ImageM); if (i != null) b.ImageM = i; }
            SetNewBtnText(b);
            b.MouseLeave += delegate { SetNewBtnText(b); b.Invalidate(); };
        }

        static void SetNewBtnText(BizDraw.PropertyFrm.MyNewBtn b)
        {
            if (newBtnFont != null) newBtnFont.SetValue(b, Text);
        }

        static bool IsTitleBar(Control c)
        {
            Form f = c.FindForm();
            if (f == null || f.GetType().BaseType == null || f.GetType().BaseType.Name != "ZForm") return false;
            for (Control p = c; p != null && p != f; p = p.Parent)
                if (p.Name == "titlepanel") return true;
            return false;
        }

        static void StyleTitle(Control c)
        {
            c.BackColor = Chrome;
            c.ForeColor = ChromeText;
            if (c.Name == "titlepanel") { c.BackgroundImage = null; return; }
            if (c is Label) return;
            Button b = c as Button;
            if (b == null) return;
            // button1 — закрыть, button2 — развернуть, button3 — свернуть.
            b.BackgroundImage = Glyph(b.Name == "button1" ? 'x' : b.Name == "button2" ? 'o' : '-', b.Size);
            b.BackgroundImageLayout = ImageLayout.Center;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.CheckedBackColor = Chrome;
            b.FlatAppearance.MouseOverBackColor = b.Name == "button1" ? Color.FromArgb(196, 43, 28) : ButtonHover;
            b.FlatAppearance.MouseDownBackColor = Selection;
        }

        static Image Glyph(char kind, Size size)
        {
            Bitmap bmp = new Bitmap(Math.Max(16, size.Width), Math.Max(16, size.Height));
            using (Graphics g = Graphics.FromImage(bmp))
            using (Pen p = new Pen(ChromeText, 1.2f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = bmp.Width / 2, cy = bmp.Height / 2, r = 5;
                if (kind == 'x') { g.DrawLine(p, cx - r, cy - r, cx + r, cy + r); g.DrawLine(p, cx - r, cy + r, cx + r, cy - r); }
                else if (kind == 'o') g.DrawRectangle(p, cx - r, cy - r, 2 * r, 2 * r);
                else g.DrawLine(p, cx - r, cy, cx + r, cy);
            }
            return bmp;
        }


        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
        static bool appModeSet;

        static void DarkNative(Control c)
        {
            if (!appModeSet)
            {
                appModeSet = true;
                try { SetPreferredAppMode(2 /* ForceDark */); FlushMenuThemes(); }
                catch (Exception ex) { Plus.Log(ex); }   // старая Windows — просто без тёмных скроллбаров
            }
            string theme = c is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer";
            bool wants = c is ScrollableControl || c is ScrollBar || c is DataGridView || c is ListView ||
                         c is TreeView || c is TextBoxBase || c is ListBox || c is ComboBox ||
                         c.GetType().Name == "UpDownButtons";
            if (!wants || c is Form) return;
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, theme, null);
            c.HandleCreated += delegate(object s, EventArgs e) { SetWindowTheme(((Control)s).Handle, theme, null); };
        }

        // Чёрные одноцветные иконки на тёмном фоне не видны — перекрашиваем в цвет текста.
        static void RecolorIcons(Control c)
        {
            PictureBox pb = c as PictureBox;
            if (pb != null && pb.Image != null) { Image i = LightenMono(pb.Image); if (i != null) pb.Image = i; }
            Label lb = c as Label;
            if (lb != null && lb.Image != null) { Image i = LightenMono(lb.Image); if (i != null) lb.Image = i; }
            ButtonBase bb = c as ButtonBase;
            if (bb != null && bb.Image != null) { Image i = LightenMono(bb.Image); if (i != null) bb.Image = i; }
            if (c.BackgroundImage != null && IsNeutral(c.BackColor) == false) { }
            else if (c.BackgroundImage != null) { Image i = LightenMono(c.BackgroundImage); if (i != null) c.BackgroundImage = i; }
        }

        static Image LightenMono(Image img)
        {
            Bitmap src = img as Bitmap;
            if (src == null || src.Width > 256 || src.Height > 256) return null;
            int dark = 0, total = 0;
            for (int y = 0; y < src.Height; y += 2)
                for (int x = 0; x < src.Width; x += 2)
                {
                    Color p = src.GetPixel(x, y);
                    if (p.A < 128) continue;
                    total++;
                    if (!IsNeutral(Color.FromArgb(255, p))) return null;   // цветная иконка — не трогаем
                    if (Lum(p) < 0.35f) dark++;
                }
            if (total == 0 || dark < total * 0.8) return null;
            Bitmap dst = new Bitmap(src.Width, src.Height);
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    Color p = src.GetPixel(x, y);
                    dst.SetPixel(x, y, Color.FromArgb(p.A, Text));
                }
            return dst;
        }


        public static Color RibStrip, RibPage, RibTab, RibTabSel, RibBtn, RibBorder, RibText, RibDim,
                            RibHover, RibDown;

        static void LoadRibbon()
        {
            string mode = Plus.Cfg == null ? "light" : Plus.Cfg.Theme;
            if (mode == "original")
            {
                RibStrip = Color.FromArgb(51, 51, 51); RibPage = Color.FromArgb(64, 64, 64);
                RibTab = Color.WhiteSmoke; RibTabSel = Color.DodgerBlue;
                RibBtn = Color.FromArgb(58, 61, 66); RibBorder = Color.FromArgb(80, 84, 90);
                RibText = Color.WhiteSmoke; RibDim = Color.FromArgb(125, 128, 134);
                RibHover = Color.FromArgb(74, 78, 84); RibDown = Color.FromArgb(46, 68, 112);
            }
            else
            {
                RibStrip = Bg; RibPage = Panel;
                RibTab = Text; RibTabSel = Accent;
                RibBtn = Button; RibBorder = Border; RibText = Text; RibDim = TextDim;
                RibHover = ButtonHover; RibDown = Selection;
            }
        }

        static readonly Dictionary<Control, Image[]> ribbonIcons = new Dictionary<Control, Image[]>();
        static readonly FieldInfo ribbonFont =
            typeof(BizDrawClient.MyButton).GetField("fontColor", BindingFlags.Instance | BindingFlags.NonPublic);

        // Идемпотентна: зовётся при первом обходе, после добавления вкладки DGUS+ и при смене темы.
        public static void StyleRibbon(TabControl tabs)
        {
            if (tabs == null || tabs.IsDisposed) return;
            BizDrawClient.TabControlEx ex = tabs as BizDrawClient.TabControlEx;
            if (ex != null)
            {
                ex.BackColorA = RibStrip;
                ex.BackColorC = RibPage;
                ex.FontUnSelColor = RibTab;
                ex.FontSelColor = RibTabSel;
            }
            bool original = Plus.Cfg == null || Plus.Cfg.Theme == "original";
            foreach (TabPage tp in tabs.TabPages)
            {
                tp.BackColor = RibPage;
                StyleRibbonChildren(tp, original);
            }
            PlusTab.Restyle();
            tabs.Invalidate(true);
        }

        static void StyleRibbonChildren(Control parent, bool original)
        {
            foreach (Control c in parent.Controls)
            {
                if (c.Name == "dgusplus") continue;
                BizDrawClient.MyButton mb = c as BizDrawClient.MyButton;
                if (mb != null) { StyleRibbonButton(mb, original); continue; }
                if (c is Label || c is GroupBox) c.ForeColor = RibText;
                if (c is ComboBox && !original) { c.BackColor = Input; c.ForeColor = Text; }
                StyleRibbonChildren(c, original);
            }
        }

        static void StyleRibbonButton(BizDrawClient.MyButton b, bool original)
        {
            Image[] src;
            if (!ribbonIcons.TryGetValue(b, out src))
            {
                if (original) return;
                src = new Image[] { b.ImageM, b.ImageMove };
                ribbonIcons[b] = src;
                b.Disposed += delegate(object s, EventArgs e) { ribbonIcons.Remove((Control)s); };
                // Уход мыши жёстко красит подпись в WhiteSmoke — возвращаем цвет темы следом.
                b.MouseLeave += delegate { SetRibbonText(b); b.Invalidate(); };
            }
            b.BackColorLeave = RibPage;
            b.BackColorMove = RibHover;
            b.BackColorM = RibPage;
            b.ImageM = original ? src[0] : RibbonIcon(src[0]);
            b.ImageMove = original ? src[1] : RibbonIcon(src[1]);
            SetRibbonText(b);
            b.Invalidate();
        }

        static void SetRibbonText(BizDrawClient.MyButton b)
        {
            if (ribbonFont != null) ribbonFont.SetValue(b, RibText);
        }

        // Одноцветные иконки ленты: светлые на светлой странице (и тёмные на тёмной) инвертируем.
        static Image RibbonIcon(Image img)
        {
            Bitmap src = img as Bitmap;
            if (src == null || src.Width > 256 || src.Height > 256) return img;
            double sum = 0; int total = 0;
            for (int y = 0; y < src.Height; y += 2)
                for (int x = 0; x < src.Width; x += 2)
                {
                    Color p = src.GetPixel(x, y);
                    if (p.A < 128) continue;
                    if (!IsNeutral(Color.FromArgb(255, p))) return img;   // цветная иконка — не трогаем
                    sum += Lum(p); total++;
                }
            if (total == 0) return img;
            bool lightIcon = sum / total > 0.5;
            if (lightIcon != (Lum(RibPage) > 0.5)) return img;   // контраст уже есть
            Bitmap dst = new Bitmap(src.Width, src.Height);
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    Color p = src.GetPixel(x, y);
                    dst.SetPixel(x, y, Color.FromArgb(p.A, 255 - p.R, 255 - p.G, 255 - p.B));
                }
            return dst;
        }


        static bool IsNeutral(Color c)
        {
            if (c == Color.Transparent || c.A < 255) return false;
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            return max - min < 24;
        }

        static float Lum(Color c) { return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f; }

        static Color MapBack(Color c, Color role)
        {
            if (!IsNeutral(c)) return c;
            return Lum(c) < 0.4f ? Chrome : role;
        }

        static Color ForeFor(Color back, Color fore)
        {
            if (!IsNeutral(fore) && fore != SystemColors.ActiveCaption) return fore;
            return Lum(back) < 0.5f ? ChromeText : Text;
        }

        static Color EffectiveBack(Control c)
        {
            for (Control p = c; p != null; p = p.Parent)
                if (p.BackColor != Color.Transparent && p.BackColor.A == 255) return p.BackColor;
            return Panel;
        }

        static ToolStripRenderer originalMenuRenderer;

        // Контекстное меню холста не лежит в дереве контролов, поэтому обход его не видит.
        public static void StyleContextMenu(ContextMenuStrip m)
        {
            if (m == null) return;
            if (originalMenuRenderer == null) originalMenuRenderer = m.Renderer;
            if (!On)
            {
                m.Renderer = originalMenuRenderer;
                m.BackColor = SystemColors.Control;
                m.ForeColor = Color.White;
                foreach (ToolStripItem it in m.Items) RestoreMenuItem(it);
                return;
            }
            m.Renderer = new ToolStripProfessionalRenderer(new Colors());
            m.BackColor = Panel;
            m.ForeColor = Text;
            foreach (ToolStripItem it in m.Items) StyleMenuItem(it);
        }

        static void RestoreMenuItem(ToolStripItem it)
        {
            it.ForeColor = Color.White;
            ToolStripDropDownItem dd = it as ToolStripDropDownItem;
            if (dd == null) return;
            foreach (ToolStripItem sub in dd.DropDownItems) RestoreMenuItem(sub);
        }

        static void StyleMenuItem(ToolStripItem it)
        {
            it.ForeColor = ForeFor(Panel, it.ForeColor);
            ToolStripDropDownItem dd = it as ToolStripDropDownItem;
            if (dd == null) return;
            foreach (ToolStripItem sub in dd.DropDownItems) StyleMenuItem(sub);
        }

        static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Panel;
            g.GridColor = Border;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            Style(g.ColumnHeadersDefaultCellStyle, PanelAlt, TextDim);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = PanelAlt;
            Style(g.RowHeadersDefaultCellStyle, PanelAlt, TextDim);
            Style(g.DefaultCellStyle, Panel, Text);
            Style(g.RowsDefaultCellStyle, Panel, Text);
            Style(g.AlternatingRowsDefaultCellStyle, Panel, Text);
            if (g.RowTemplate.Height < 22) g.RowTemplate.Height = 22;
            g.RowPrePaint += delegate(object s, DataGridViewRowPrePaintEventArgs e)
            {
                // Строки, которым DGUS задал свой нейтральный фон (серый/белый), выравниваем.
                DataGridViewRow row = g.Rows[e.RowIndex];
                if (row.HasDefaultCellStyle && IsNeutral(row.DefaultCellStyle.BackColor))
                    Style(row.DefaultCellStyle, Panel, Text);
                if (row.Height < 22) row.Height = 22;
            };
        }

        static void Style(DataGridViewCellStyle s, Color back, Color fore)
        {
            s.BackColor = back;
            s.ForeColor = fore;
            s.SelectionBackColor = Selection;
            s.SelectionForeColor = Text;
            s.Font = UiFont;
        }

        static void PaintPageShadow(object sender, PaintEventArgs e)
        {
            DocumentArea area = (DocumentArea)sender;
            Rectangle r = area.drawArea1.Bounds;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 1; i <= 8; i++)
            {
                using (Pen p = new Pen(Color.FromArgb(Dark ? 34 - i * 4 : 22 - i * 2, 0, 0, 0)))
                    e.Graphics.DrawRectangle(p, r.X - i, r.Y - i + 2, r.Width + 2 * i - 1, r.Height + 2 * i - 1);
            }
        }


        static IEnumerable<DockPanel> FindDockPanels()
        {
            List<DockPanel> res = new List<DockPanel>();
            if (Plus.Space != null)
            {
                DockPanel dp = R.Get(Plus.Space, "dockPanel1") as DockPanel;
                if (dp != null) res.Add(dp);
            }
            return res;
        }

        static void SkinDock(DockPanel dp)
        {
            if (skinned.ContainsKey(dp)) return;
            skinned[dp] = true;

            DockPanelSkin s = dp.Skin;
            dp.DockBackColor = Canvas;

            Grad(s.AutoHideStripSkin.DockStripGradient, Chrome);
            Tab(s.AutoHideStripSkin.TabGradient, PanelAlt, TextDim);

            DockPaneStripGradient doc = s.DockPaneStripSkin.DocumentGradient;
            Grad(doc.DockStripGradient, PanelAlt);
            Tab(doc.ActiveTabGradient, Panel, Text);
            Tab(doc.InactiveTabGradient, PanelAlt, TextDim);

            DockPaneStripToolWindowGradient tw = s.DockPaneStripSkin.ToolWindowGradient;
            Grad(tw.DockStripGradient, PanelAlt);
            Tab(tw.ActiveTabGradient, Panel, Text);
            Tab(tw.InactiveTabGradient, PanelAlt, TextDim);
            Tab(tw.ActiveCaptionGradient, Accent, Color.White);
            Tab(tw.InactiveCaptionGradient, PanelAlt, TextDim);

            dp.Skin = s;
            dp.Invalidate(true);
            foreach (DockPane pane in dp.Panes) pane.Invalidate(true);
        }

        static void Grad(DockPanelGradient g, Color c)
        {
            g.StartColor = c; g.EndColor = c; g.LinearGradientMode = LinearGradientMode.Vertical;
        }

        static void Tab(TabGradient g, Color back, Color text)
        {
            Grad(g, back);
            g.TextColor = text;
        }


        class Colors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return Panel; } }
            public override Color ToolStripGradientMiddle { get { return Panel; } }
            public override Color ToolStripGradientEnd { get { return Panel; } }
            public override Color ToolStripDropDownBackground { get { return Panel; } }
            public override Color ToolStripBorder { get { return Border; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Selection; } }
            public override Color MenuItemPressedGradientEnd { get { return Selection; } }
            public override Color ImageMarginGradientBegin { get { return PanelAlt; } }
            public override Color ImageMarginGradientMiddle { get { return PanelAlt; } }
            public override Color ImageMarginGradientEnd { get { return PanelAlt; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
            public override Color ButtonSelectedHighlight { get { return Hover; } }
            public override Color ButtonSelectedGradientBegin { get { return Hover; } }
            public override Color ButtonSelectedGradientEnd { get { return Hover; } }
            public override Color ButtonSelectedBorder { get { return Border; } }
        }


        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, int thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();

        static HookProc hookProc;
        static IntPtr hook;

        static void EnsureHook()
        {
            if (hook != IntPtr.Zero) return;
            hookProc = CbtProc;
            hook = SetWindowsHookEx(5 /* WH_CBT */, hookProc, IntPtr.Zero, GetCurrentThreadId());
        }

        static IntPtr CbtProc(int code, IntPtr w, IntPtr l)
        {
            if (code == 5 /* HCBT_ACTIVATE */)
            {
                Form f = Control.FromHandle(w) as Form;
                if (f != null) Plus.Guard(delegate { ApplyTree(f); });
            }
            return CallNextHookEx(hook, code, w, l);
        }
    }
}
