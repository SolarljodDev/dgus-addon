using System;
using System.Drawing;
using System.Windows.Forms;

namespace DgusPlus
{
    // Вкладка «DGUS+» на ленте: отмена/повтор, масштаб, панель VP, настройки.
    static class PlusTab
    {
        static TabPage page;
        static Button undoBtn, redoBtn;
        static Label note;
        static DateTime noteAt;

        // Короткое сообщение на ленте вместо окна DGUS (см. QuietPopups); гаснет через 6 с.
        public static void Note(string text)
        {
            if (note == null || note.IsDisposed) return;
            note.Text = "✓ " + text;
            noteAt = DateTime.Now;
        }

        public static void Attach(Form main)
        {
            TabControl tabs = R.Get(main, "tabControlEx1") as TabControl;
            if (tabs == null || page != null) return;

            page = new TabPage("DGUS+");
            page.Name = "dgusplus";   // тема её не трогает: лента всегда тёмная
            page.BackColor = Theme.RibPage;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.Padding = new Padding(6, 6, 6, 0);
            flow.BackColor = Theme.RibPage;
            flow.WrapContents = false;
            page.Controls.Add(flow);

            undoBtn = AddButton(flow, "↶  Undo", "Ctrl+Z", delegate { Undo.DoUndo(); });
            redoBtn = AddButton(flow, "↷  Redo", "Ctrl+Y  /  Ctrl+Shift+Z", delegate { Undo.DoRedo(); });
            AddSep(flow);
            AddButton(flow, "Fit", "Ctrl+0", delegate { Plus.ZoomFit(); });
            AddButton(flow, "100%", "Ctrl+1", delegate { Plus.SetZoomRaw(1f); });
            AddButton(flow, "VP panel", null, delegate { VpPanel.ShowPanel(); });
            AddSep(flow);
            AddButton(flow, "Font generator", null,
                delegate { Browser.OpenTool("generator.html"); });
            AddButton(flow, "Font editor", null,
                delegate { Browser.OpenTool("editor.html"); });
            AddButton(flow, "Upload to display", "Over UART, no SD card",
                delegate { UploadForm.ShowFor(Plus.MainForm); });
            AddSep(flow);

            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.FlowDirection = FlowDirection.TopDown;
            opts.AutoSize = true;
            opts.BackColor = Theme.RibPage;
            opts.Margin = new Padding(4, 0, 4, 0);
            opts.WrapContents = true;
            opts.Height = 60;
            flow.Controls.Add(opts);

            AddCheck(opts, "First click only selects", Plus.Cfg.ClickToSelect,
                delegate(bool v) { Plus.Cfg.ClickToSelect = v; });
            AddCheck(opts, "Wheel = zoom", Plus.Cfg.WheelZoom,
                delegate(bool v) { Plus.Cfg.WheelZoom = v; });
            AddCheck(opts, "VP in canvas labels", Plus.Cfg.ShowVpLabels,
                delegate(bool v) { Plus.Cfg.ShowVpLabels = v; if (Plus.CurrentDrawArea != null) Plus.CurrentDrawArea.Invalidate(); });
            AddCheck(opts, "Center page", Plus.Cfg.CenterPage,
                delegate(bool v) { Plus.Cfg.CenterPage = v; if (v) Plus.CenterPage(); });

            AddSep(flow);
            Label tl = new Label();
            Loc.Bind(delegate(string s) { tl.Text = s; }, "Theme:");
            tl.AutoSize = true;
            tl.ForeColor = Theme.RibText;
            tl.Font = Theme.UiFont;
            tl.Margin = new Padding(4, 12, 2, 0);
            flow.Controls.Add(tl);
            ComboBox theme = new ComboBox();
            theme.DropDownStyle = ComboBoxStyle.DropDownList;
            string[] themeNames = { "Dark", "Light", "Original" };
            foreach (string n in themeNames) theme.Items.Add(Loc.T(n));
            bool relabel = false;
            Loc.Changed += delegate
            {
                int sel = theme.SelectedIndex;
                relabel = true;
                for (int i = 0; i < themeNames.Length; i++) theme.Items[i] = Loc.T(themeNames[i]);
                theme.SelectedIndex = sel;
                relabel = false;
            };
            theme.SelectedIndex = Plus.Cfg.Theme == "dark" ? 0 : Plus.Cfg.Theme == "light" ? 1 : 2;
            theme.Font = Theme.UiFont;
            theme.Width = 120;
            theme.Margin = new Padding(2, 9, 4, 0);
            theme.SelectedIndexChanged += delegate
            {
                if (relabel) return;
                Plus.Cfg.Theme = theme.SelectedIndex == 0 ? "dark" : theme.SelectedIndex == 1 ? "light" : "original";
                Plus.Cfg.Save();
                Theme.Reapply();
                MessageBox.Show(Loc.T("The theme will be fully applied after restarting DGUS."), "DGUS+");
            };
            flow.Controls.Add(theme);

            AddSep(flow);
            Label mcp = new Label();
            mcp.AutoSize = true;
            mcp.ForeColor = Theme.RibText;
            mcp.Font = Theme.UiFont;
            mcp.Margin = new Padding(4, 12, 4, 0);
            flow.Controls.Add(mcp);

            note = new Label();
            note.AutoSize = true;
            note.ForeColor = Color.FromArgb(46, 158, 79);
            note.Tag = "keep";
            note.Font = Theme.UiFont;
            note.Margin = new Padding(16, 12, 4, 0);
            flow.Controls.Add(note);

            tabs.TabPages.Add(page);
            Theme.StyleRibbon(tabs);

            Timer t = new Timer();
            t.Interval = 300;
            t.Tick += delegate
            {
                if (undoBtn.IsDisposed) { t.Stop(); return; }
                mcp.Text = "MCP: " + McpServer.Status;
                if (note.Text.Length > 0 && (DateTime.Now - noteAt).TotalSeconds > 6) note.Text = "";
                undoBtn.Enabled = Undo.CanUndo;
                redoBtn.Enabled = Undo.CanRedo;
            };
            t.Start();
        }

        static Button AddButton(FlowLayoutPanel flow, string text, string tip, EventHandler click)
        {
            Button b = new RibbonButton();
            Loc.Bind(delegate(string s) { b.Text = s; b.Invalidate(); }, text);
            b.AutoSize = true;
            b.MinimumSize = new Size(80, 40);
            b.Font = Theme.UiFont;
            b.Margin = new Padding(3, 4, 3, 0);
            b.Click += delegate(object s, EventArgs e) { Plus.Guard(delegate { click(s, e); }); };
            if (tip != null)
            {
                ToolTip tt = new ToolTip();
                Loc.Bind(delegate(string s) { tt.SetToolTip(b, s); }, tip);
            }
            flow.Controls.Add(b);
            return b;
        }

        delegate void BoolHandler(bool v);

        static void AddCheck(FlowLayoutPanel flow, string text, bool value, BoolHandler changed)
        {
            CheckBox c = new CheckBox();
            Loc.Bind(delegate(string s) { c.Text = s; }, text);
            c.Checked = value;
            c.AutoSize = true;
            c.ForeColor = Theme.RibText;
            c.Font = Theme.UiFont;
            c.Margin = new Padding(3, 2, 12, 0);
            c.CheckedChanged += delegate { changed(c.Checked); Plus.Cfg.Save(); };
            flow.Controls.Add(c);
        }

        public static void Restyle()
        {
            if (page == null || page.IsDisposed) return;
            page.BackColor = Theme.RibPage;
            RestyleTree(page);
        }

        static void RestyleTree(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is RibbonButton) { c.Invalidate(); continue; }
                if (c is ComboBox) continue;
                if (c is Panel && c.Width == 1) c.BackColor = Theme.RibBorder;
                else if (c is Label || c is CheckBox) { if (!"keep".Equals(c.Tag)) c.ForeColor = Theme.RibText; }
                else c.BackColor = Theme.RibPage;
                RestyleTree(c);
            }
        }

        // Плоская кнопка ленты. Рисуем сами: у Flat-кнопки системный цвет надписи в disabled
        // (чёрный) не читается на тёмной кнопке.
        class RibbonButton : Button
        {
            bool hover, down;

            public RibbonButton() { FlatStyle = FlatStyle.Flat; SetStyle(ControlStyles.UserPaint, true); }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Color bg = !Enabled ? Theme.RibBtn : down ? Theme.RibDown : hover ? Theme.RibHover : Theme.RibBtn;
                e.Graphics.Clear(bg);
                using (Pen pen = new Pen(Theme.RibBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                    Enabled ? Theme.RibText : Theme.RibDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        static void AddSep(FlowLayoutPanel flow)
        {
            Panel p = new Panel();
            p.Width = 1;
            p.Height = 44;
            p.BackColor = Theme.RibBorder;
            p.Margin = new Padding(8, 4, 8, 0);
            flow.Controls.Add(p);
        }
    }
}
