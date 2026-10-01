using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using BizDraw;
using BizDraw.Core;
using BizDraw.Objects;

namespace DgusPlus
{
    class PageRef
    {
        public int Number;          // номер страницы, как в колонке Location (00, 01, …)
        public string File;
        public Document Doc;
        public DataGridViewRow Row;
    }

    // Страницы проекта живут в строках списка Images View: [номер, файл картинки, Document].
    static class Pages
    {
        static DataGridView Grid
        {
            get { return Plus.Space == null ? null : Plus.Space.m_taskList.dgvPictureList; }
        }

        public static List<PageRef> All()
        {
            List<PageRef> res = new List<PageRef>();
            DataGridView g = Grid;
            if (g == null) return res;
            foreach (DataGridViewRow row in g.Rows)
            {
                if (row.IsNewRow || row.Cells.Count < 3) continue;
                int n;
                if (row.Cells[0].Value == null || !int.TryParse(row.Cells[0].Value.ToString(), out n)) continue;
                PageRef p = new PageRef();
                p.Number = n;
                p.File = row.Cells[1].Value == null ? "" : row.Cells[1].Value.ToString();
                p.Doc = row.Cells[2].Value as Document;
                p.Row = row;
                res.Add(p);
            }
            return res;
        }

        public static PageRef Get(int number)
        {
            foreach (PageRef p in All()) if (p.Number == number) return p;
            throw new ArgumentException("No page " + number + ". See list_pages.");
        }

        // Новая страница из картинки в папке проекта (так же, как «Add» в Images View): строка списка + пустой Document.
        public static PageRef Add(int number, string file)
        {
            DataGridView g = Grid;
            if (g == null) throw new InvalidOperationException("No project is open.");
            foreach (PageRef q in All())
                if (q.Number == number) throw new ArgumentException("Page " + number + " already exists.");
            string full = Plus.Space.filepath + Globel.ProjectPathName + file;
            if (!File.Exists(full)) throw new ArgumentException("Background file not found: " + full);
            Document cur = Plus.CurrentDoc;
            Document d = new Document();
            d.filename = "";
            d.FilePath = Plus.Space.filepath;
            d.Width = cur.Width;
            d.Height = cur.Height;
            d.backImagePath = full;
            int idx = g.Rows.Add();
            DataGridViewRow row = g.Rows[idx];
            row.Cells[0].Value = number.ToString("00");
            row.Cells[1].Value = file;
            row.Cells[2].Value = d;
            object tl = Plus.Space.m_taskList;
            List<int> ids = R.Get(tl, "idList") as List<int>;
            Dictionary<int, string> dic = R.Get(tl, "idDic") as Dictionary<int, string>;
            if (ids != null && !ids.Contains(number)) ids.Add(number);
            if (dic != null && !dic.ContainsKey(number)) dic.Add(number, file);
            PageRef p = new PageRef();
            p.Number = number; p.File = file; p.Doc = d; p.Row = row;
            return p;
        }

        public static PageRef Current
        {
            get
            {
                Document d = Plus.CurrentDoc;
                foreach (PageRef p in All()) if (p.Doc == d) return p;
                return null;
            }
        }

        // Открыть страницу на холсте так же, как клик по строке в Images View.
        public static void Show(PageRef p)
        {
            if (p.Doc == Plus.CurrentDoc) return;
            Plus.Space.m_Design.Activate();
            DataGridView g = Grid;
            g.ClearSelection();
            g.CurrentCell = p.Row.Cells[0];
            p.Row.Selected = true;
            // SelectionChanged у скрытого/свёрнутого списка может не прийти — зовём обработчик DGUS сами.
            if (Plus.CurrentDoc != p.Doc) Plus.Space.dgvPictureList_SelectionChanged(g, EventArgs.Empty);
            if (Plus.CurrentDoc != p.Doc)
                throw new InvalidOperationException("Could not open page " + p.Number + " in DGUS.");
        }

        public static Size Resolution
        {
            get
            {
                return new Size(R.GetInt(Plus.Space, "intWidth", 800), R.GetInt(Plus.Space, "intHeight", 480));
            }
        }

        public static string BackgroundPath(PageRef p)
        {
            return Plus.Space.filepath + Globel.ProjectPathName + p.File;
        }

        // Рендер страницы без вывода на экран: фон + элементы (+ подписи с VP).
        public static byte[] RenderPng(PageRef p, bool labels, float scale)
        {
            Size res = Resolution;
            int w = Math.Max(1, (int)(res.Width * scale)), h = Math.Max(1, (int)(res.Height * scale));
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    string bg = BackgroundPath(p);
                    if (File.Exists(bg))
                        using (Image img = Image.FromFile(bg)) g.DrawImage(img, 0, 0, w, h);
                    g.ScaleTransform(scale, scale);

                    Document doc = p.Doc;
                    bool oldBtn = doc.TempDrawBtn;
                    bool oldShowText = Globel.ShowText;
                    int oldShow = doc.Showtype;
                    List<DrawObject> selected = doc.GetSelectedObjects();
                    try
                    {
                        // TempDrawBtn=false у DGUS — это «только рамки, без картинок», поэтому его
                        // не трогаем; подписи «-Имя» прячет Globel.ShowText.
                        doc.TempDrawBtn = true;
                        Globel.ShowText = labels && oldShowText;
                        doc.Showtype = 0;
                        foreach (DrawObject o in selected) o.SetSelectedState(false, false);   // без рамок выделения, без событий
                        doc.Items.Draw(g);
                        if (labels && Globel.ShowText)
                            foreach (VpEntry e in VpModel.Collect(doc)) if (!e.IsAux) Overlay.DrawVpLabel(g, e);
                    }
                    finally
                    {
                        doc.TempDrawBtn = oldBtn;
                        Globel.ShowText = oldShowText;
                        doc.Showtype = oldShow;
                        foreach (DrawObject o in selected) o.SetSelectedState(true, false);
                    }
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }
    }
}
