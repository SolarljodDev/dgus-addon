using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using BizDraw;
using BizDraw.Controls;
using BizDraw.Core;

namespace DgusPlus
{
    // Точка сборки надстройки: находит главное окно DGUS, держит ссылки на
    // рабочую область и раз в 200 мс опрашивает состояние (смена страницы, правки).
    static class Plus
    {
        public static Settings Cfg;
        public static Form MainForm;
        public static TDocumentSpace Space;

        static Timer timer;
        static DrawArea hookedArea;
        static bool centering;

        public static void Install()
        {
            Cfg = Settings.Load();
            Language.Prepare();
            // Сохранённые страницы помнят имя сборки, которая их писала (DgusPlus, DgusPlus.new, …) — читаем любой из них.
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                return e.Name.StartsWith("DgusPlus") ? Assembly.GetExecutingAssembly() : null;
            };
            Application.AddMessageFilter(new InputFilter());
            Application.Idle += FirstIdle;
        }

        // Таймер создаём только после старта цикла сообщений, чтобы не создать окно
        // раньше, чем DGUS вызовет Application.EnableVisualStyles().
        static void FirstIdle(object sender, EventArgs e)
        {
            Application.Idle -= FirstIdle;
            timer = new Timer();
            timer.Interval = 200;
            timer.Tick += delegate { Guard(Poll); };
            timer.Start();
        }

        public delegate object UiCall();

        // Выполнить в потоке интерфейса DGUS (из потоков HTTP-сервера).
        public static object OnUi(UiCall call)
        {
            Form f = MainForm;
            if (f == null || f.IsDisposed) throw new ArgumentException("DGUS is not running yet.");
            return f.InvokeRequired ? f.Invoke(call) : call();
        }

        public static void Guard(MethodInvoker a)
        {
            try { a(); }
            catch (Exception ex) { Log(ex); }
        }

        static void Poll()
        {
            if (MainForm == null || MainForm.IsDisposed)
            {
                MainForm = null;
                Space = null;
                foreach (Form f in Application.OpenForms)
                {
                    if (f.GetType().FullName == "BizDrawClient.Form1")
                    {
                        MainForm = f;
                        Space = (TDocumentSpace)R.Get(f, "tDocumentSpace1");
                        // DGUSPLUS_PORT — для второго (тестового) экземпляра рядом с рабочим.
                        int port = Cfg.McpPort;
                        string envPort = Environment.GetEnvironmentVariable("DGUSPLUS_PORT");
                        if (!string.IsNullOrEmpty(envPort)) int.TryParse(envPort, out port);
                        if (Cfg.McpEnabled && McpServer.Port == 0) McpServer.Start(port);
                        Language.Attach(f);
                        PlusTab.Attach(f);
                        FrameFix.Install(f);
                        QuietPopups.Install();
                        // Холст пересоздаётся при открытии проекта. Ловим его в момент добавления
                        // на панель — до первой отрисовки, иначе он мелькает в углу и «прыгает».
                        Panel p5 = ScrollPanel;
                        if (p5 != null)
                            p5.ControlAdded += delegate(object s, ControlEventArgs e)
                            {
                                DocumentArea ed = e.Control as DocumentArea;
                                if (ed != null) Guard(delegate { HookArea(ed.drawArea1); });
                            };
                        break;
                    }
                }
            }
            Theme.Poll();
            if (Space == null) return;
            WelcomeLinks.Poll(MainForm);

            HookArea(CurrentDrawArea);

            // Только что открыт проект — сразу показываем холст, а не Welcome.
            string path = Globel.ProjectPath;
            if (!string.IsNullOrEmpty(path) && path != openedPath && CurrentDoc != null)
            {
                openedPath = path;
                Space.m_Design.Activate();
            }

            Undo.Poll();
            Arrange.Poll();
            VpPanel.Poll();
            if (!dumped && Environment.GetEnvironmentVariable("DGUSPLUS_DUMP") != null &&
                (DateTime.Now - started).TotalSeconds > 8) { dumped = true; Dump(); }
        }

        static string openedPath;
        static void HookArea(DrawArea da)
        {
            if (da == null || da == hookedArea) return;
            hookedArea = da;
            da.Paint += Overlay.OnPaint;
            Arrange.Attach(da);
            Coords.Attach(da);
            da.SizeChanged += delegate { CenterPage(); };
            da.LocationChanged += delegate { CenterPage(); };
            Control ed = da.Parent;
            if (ed != null) ed.SizeChanged += delegate { CenterPage(); };
            CenterPage();
        }

        static bool dumped;
        static readonly DateTime started = DateTime.Now;

        // Отладка раскладки: DGUSPLUS_DUMP=1 -> дерево контролов в DgusPlus.log.
        static void Dump()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("LAYOUT DUMP\n");
            DumpTree(MainForm, 0, sb);
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DgusPlus.log"), sb.ToString());
        }

        static void DumpTree(Control c, int depth, System.Text.StringBuilder sb)
        {
            if (depth > 7 || c is DrawArea) return;
            sb.Append(new string(' ', depth * 2)).Append(c.GetType().Name).Append(" '").Append(c.Name)
              .Append("' ").Append(c.Bounds).Append(c.Visible ? "" : " HIDDEN").Append(" dock=").Append(c.Dock)
              .Append(" font=").Append(c.Font.Name).Append(' ').Append(c.Font.Size).Append('\n');
            if (c.GetType().Name == "MyButton") return;
            foreach (Control ch in c.Controls) DumpTree(ch, depth + 1, sb);
        }

        public static DrawArea CurrentDrawArea
        {
            get
            {
                if (Space == null || Space.tempeditor == null) return null;
                return Space.tempeditor.drawArea1;
            }
        }

        public static Document CurrentDoc
        {
            get
            {
                DrawArea da = CurrentDrawArea;
                return da == null ? null : da.Document;
            }
        }

        public static Panel ScrollPanel
        {
            get { return Space == null ? null : (Panel)R.Get(Space.m_Design, "panel5"); }
        }

        public static bool ProjectOpen
        {
            get
            {
                DrawArea da = CurrentDrawArea;
                return da != null && da.Document != null && ScrollPanel != null && ScrollPanel.Visible;
            }
        }


        static readonly float[] ZoomSteps = {
            0.1f, 0.125f, 0.167f, 0.25f, 0.333f, 0.5f, 0.667f, 0.75f, 1f,
            1.25f, 1.5f, 2f, 2.5f, 3f, 4f, 5f };

        public static float NextZoom(float cur, bool up)
        {
            if (up)
            {
                foreach (float z in ZoomSteps) if (z > cur * 1.01f) return z;
                return ZoomSteps[ZoomSteps.Length - 1];
            }
            for (int i = ZoomSteps.Length - 1; i >= 0; i--)
                if (ZoomSteps[i] < cur * 0.99f) return ZoomSteps[i];
            return ZoomSteps[0];
        }

        // Меняет масштаб так, чтобы точка страницы под screenPt осталась под курсором.
        public static void ZoomAt(float nz, Point screenPt)
        {
            DrawArea da = CurrentDrawArea;
            Panel p = ScrollPanel;
            if (da == null || p == null || Space.IntiRun) return;

            float old = da.Zoom;
            Point inDa = da.PointToClient(screenPt);
            PointF docPt = new PointF(inDa.X / old, inDa.Y / old);
            Point inPanel = p.PointToClient(screenPt);

            SetZoomRaw(nz);

            Point daInPanel = p.PointToClient(da.PointToScreen(Point.Empty));
            int qx = daInPanel.X + (int)Math.Round(docPt.X * nz);
            int qy = daInPanel.Y + (int)Math.Round(docPt.Y * nz);
            Point scroll = new Point(-p.AutoScrollPosition.X, -p.AutoScrollPosition.Y);
            p.AutoScrollPosition = new Point(Math.Max(0, scroll.X + qx - inPanel.X),
                                             Math.Max(0, scroll.Y + qy - inPanel.Y));
            da.Invalidate();
        }

        public static void ZoomFit()
        {
            DrawArea da = CurrentDrawArea;
            Panel p = ScrollPanel;
            if (da == null || p == null || da.Document == null) return;
            float zx = (p.ClientSize.Width - 40f) / Math.Max(1, da.Document.Width);
            float zy = (p.ClientSize.Height - 40f) / Math.Max(1, da.Document.Height);
            SetZoomRaw(Math.Max(0.1f, Math.Min(5f, Math.Min(zx, zy))));
        }

        public static void SetZoomRaw(float nz)
        {
            Globel.EditorZoom = nz;
            Globel.TmpZoom = nz.ToString("0%");
            // Поле масштаба на панели инструментов: меняем текст, его обработчик
            // сам пересчитает размеры (что нам и нужно ниже).
            ComboBox cb = MainForm == null ? null : R.Get(MainForm, "comboBox2") as ComboBox;
            if (cb != null && cb.Text != Globel.TmpZoom) cb.Text = Globel.TmpZoom;
            Space.tabPage4_SizeChanged(null, null);
            CenterPage();
        }

        // DGUS прижимает страницу к левому верхнему углу; ставим её по центру,
        // если она меньше видимой области.
        public static void CenterPage()
        {
            if (centering || !Cfg.CenterPage) return;
            DrawArea da = CurrentDrawArea;
            Panel p = ScrollPanel;
            if (da == null || p == null) return;
            centering = true;
            try
            {
                Control ed = da.Parent ?? Space.tempeditor;
                int w = Math.Max(p.ClientSize.Width, da.Width + 80);
                int h = Math.Max(p.ClientSize.Height, da.Height + 80);
                if (ed.Width != w || ed.Height != h) ed.Size = new Size(w, h);
                Point loc = new Point(Math.Max(20, (ed.ClientSize.Width - da.Width) / 2),
                                      Math.Max(20, (ed.ClientSize.Height - da.Height) / 2));
                if (da.Location != loc)
                {
                    da.Location = loc;
                    ed.Invalidate();   // тень под страницей рисует подложка — стереть старую
                }
            }
            finally { centering = false; }
        }

        public static void ScrollTo(Rectangle r)
        {
            DrawArea da = CurrentDrawArea;
            Panel p = ScrollPanel;
            if (da == null || p == null) return;
            Point center = new Point((int)((r.X + r.Width / 2) * da.Zoom), (int)((r.Y + r.Height / 2) * da.Zoom));
            Point inPanel = p.PointToClient(da.PointToScreen(center));
            Point scroll = new Point(-p.AutoScrollPosition.X, -p.AutoScrollPosition.Y);
            p.AutoScrollPosition = new Point(Math.Max(0, scroll.X + inPanel.X - p.ClientSize.Width / 2),
                                             Math.Max(0, scroll.Y + inPanel.Y - p.ClientSize.Height / 2));
        }

        // То же, что кнопка Save на ленте.
        public static void SaveProject()
        {
            R.Call(MainForm, "保存SToolStripButton_Click", null, EventArgs.Empty);
        }


        public static void Log(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DgusPlus.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}
