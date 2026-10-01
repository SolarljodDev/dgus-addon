using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BizDraw;
using BizDraw.Objects;

namespace DgusPlus
{
    // Файлы DWIN_SET открытого проекта: номера, блоки флеша, размеры и диапазоны шрифтов, ссылки на шрифты из элементов.
    static class ProjectFiles
    {
        public const int BlockSize = 256 * 1024;        // один ID = 256 КБ флеша T5L
        public const int MaxId = 127;
        static readonly int[] Reserved = { 0, 13, 14, 22 };   // 0# шрифт, touch, show, config

        public class FileInfo
        {
            public string Name, Kind;
            public int Id = -1, Blocks, W, H;
            public bool Gray;   // серый 4-бит шрифт (заголовок «DGUS_2»), размеры и диапазоны — из самого файла
            public long Size;
            public List<string[]> Ranges = new List<string[]>();   // {метка, начало, конец}
        }

        public static string DwinSet
        {
            get
            {
                string p = Globel.ProjectPath;
                if (string.IsNullOrEmpty(p)) throw new ArgumentException("No project is open in DGUS.");
                return Path.Combine(p, "DWIN_SET");
            }
        }

        static readonly Regex LeadId = new Regex(@"^(\d+)");
        static readonly Regex Dims = new Regex(@"(?<![0-9])(\d{1,3})[_xX](\d{1,3})(?![0-9])");
        static readonly Regex RangeRe = new Regex(@"([A-Za-z][A-Za-z0-9]*)_([0-9A-Fa-f]{1,6})-([0-9A-Fa-f]{1,6})");

        public static List<FileInfo> Scan()
        {
            List<FileInfo> res = new List<FileInfo>();
            string dir = DwinSet;
            if (!Directory.Exists(dir)) return res;
            foreach (string path in Directory.GetFiles(dir))
            {
                FileInfo f = new FileInfo();
                f.Name = Path.GetFileName(path);
                f.Size = new System.IO.FileInfo(path).Length;
                f.Blocks = (int)Math.Max(1, (f.Size + BlockSize - 1) / BlockSize);
                Match m = LeadId.Match(f.Name);
                if (m.Success) f.Id = int.Parse(m.Groups[1].Value);
                f.Kind = KindOf(f.Name);
                if (f.Kind == "font") { ParseFontName(f); ReadGrayHeader(path, f); }
                res.Add(f);
            }
            res.Sort(delegate(FileInfo a, FileInfo b)
            {
                int c = a.Id.CompareTo(b.Id);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return res;
        }

        public static int IdOf(string name)
        {
            Match m = LeadId.Match(name);
            if (!m.Success) throw new ArgumentException("The name " + name + " has no file number.");
            return int.Parse(m.Groups[1].Value);
        }

        static string KindOf(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            string lower = name.ToLowerInvariant();
            if (lower.StartsWith("13touchfile")) return "touch";
            if (lower.StartsWith("14showfile")) return "show";
            if (lower.StartsWith("22_config")) return "config";
            if (ext == ".icl") return "icons";
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp") return "page";
            if (ext == ".hzk" || ext == ".dzk" || ext == ".bin") return "font";
            if (ext == ".lib") return "lib";
            return "other";
        }

        // «23_Roboto_wght700_64_64_ASCII_0-007E_Cyrillic_0400-04FF.bin»:
        // первая пара чисел 4…256 — размер ячейки, дальше — склеенные диапазоны.
        static void ParseFontName(FileInfo f)
        {
            string stem = Path.GetFileNameWithoutExtension(f.Name);
            // Пары перекрываются («wght700_64_64»: сначала 700_64, потом 64_64) — ищем со сдвигом на 1.
            for (Match m = Dims.Match(stem); m.Success; m = Dims.Match(stem, m.Index + 1))
            {
                int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value);
                if (w >= 4 && w <= 256 && h >= 4 && h <= 256) { f.W = w; f.H = h; break; }
            }
            foreach (Match m in RangeRe.Matches(stem))
                f.Ranges.Add(new string[] { m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value.ToUpperInvariant() });
        }

        // Серый шрифт (fontbit4 / font-generator в режиме «Серый»): "DGUS_2" в начале, 4 бита на
        // пиксель, размер ячейки в 0x14/0x15, таблица по кодам 0x20…0xFFFF с 0x1A0 (5 байт на код,
        // байт флага 0xF0 — глиф есть). Имя файла для таких шрифтов ничего не гарантирует.
        static void ReadGrayHeader(string path, FileInfo f)
        {
            try
            {
                byte[] b = new byte[0x1A0 + 65504 * 5];
                int n;
                using (FileStream fs = File.OpenRead(path)) n = fs.Read(b, 0, b.Length);
                if (n < 0x1A0 || b[0] != 'D' || b[1] != 'G' || b[2] != 'U' || b[3] != 'S' || b[4] != '_' || b[5] != '2') return;
                f.Gray = true;
                f.W = b[0x14]; f.H = b[0x15];
                f.Ranges.Clear();
                int start = -1;
                int recs = Math.Min(65504, (n - 0x1A0) / 5);
                for (int i = 0; i <= recs; i++)
                {
                    bool has = i < recs && b[0x1A0 + i * 5 + 1] == 0xF0;
                    if (has && start < 0) start = i;
                    if (!has && start >= 0)
                    {
                        f.Ranges.Add(new string[] { "U", (start + 0x20).ToString("X4"), (i - 1 + 0x20).ToString("X4") });
                        start = -1;
                    }
                }
            }
            catch { }
        }

        // Занятые номера: файл с ID N и размером на K блоков занимает N…N+K-1.
        public static Dictionary<int, string> UsedIds(List<FileInfo> files)
        {
            Dictionary<int, string> used = new Dictionary<int, string>();
            foreach (FileInfo f in files)
            {
                if (f.Id < 0 || f.Kind == "page") continue;
                for (int i = 0; i < f.Blocks; i++) used[f.Id + i] = f.Name;
            }
            return used;
        }

        public static List<int> FreeIds(List<FileInfo> files, int needBlocks)
        {
            Dictionary<int, string> used = UsedIds(files);
            List<int> free = new List<int>();
            for (int id = 1; id + needBlocks - 1 <= MaxId; id++)
            {
                bool ok = true;
                for (int k = 0; k < needBlocks && ok; k++)
                    if (used.ContainsKey(id + k) || Array.IndexOf(Reserved, id + k) >= 0) ok = false;
                if (ok) free.Add(id);
            }
            return free;
        }

        static readonly string[] FontProps = { "Font1_Id", "Font0_Id", "Lib_Id" };
        static readonly string[] FontPropsII = { "Font_ID" };

        // Кто ссылается на какой шрифт: {id: [{page, id, type, name, prop}]}. Только в UI-потоке.
        public static Dictionary<int, List<object>> FontUsage()
        {
            Dictionary<int, List<object>> use = new Dictionary<int, List<object>>();
            foreach (PageRef p in Pages.All())
            {
                if (p.Doc == null) continue;
                for (int i = 0; i < p.Doc.Items.Count; i++)
                {
                    DrawRectangle r = p.Doc.Items[i] as DrawRectangle;
                    if (r == null || r.ConfigObject == null) continue;
                    // Text II (серые шрифты) ссылается на шрифт одним полем Font_ID; Font0/Font1 у него не используются.
                    bool textII = R.Get(r.ConfigObject, "Font_ID") != null;
                    foreach (string prop in textII ? FontPropsII : FontProps)
                    {
                        object v = R.Get(r.ConfigObject, prop);
                        if (v == null) continue;
                        int fid = Convert.ToInt32(v);
                        List<object> list;
                        if (!use.TryGetValue(fid, out list)) use[fid] = list = new List<object>();
                        Dictionary<string, object> e = new Dictionary<string, object>();
                        e["page"] = p.Number;
                        e["id"] = i;
                        e["type"] = VpModel.TypeOf(r);
                        e["name"] = R.Get(r.ConfigObject, "Var_Name") as string ?? "";
                        e["prop"] = prop;
                        list.Add(e);
                    }
                }
            }
            return use;
        }

        public static Dictionary<string, object> Summary(bool withUsage)
        {
            List<FileInfo> files = Scan();
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["project"] = Globel.ProjectPath;
            r["dwinSet"] = DwinSet;
            List<object> fl = new List<object>();
            foreach (FileInfo f in files)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["name"] = f.Name;
                d["kind"] = f.Kind;
                d["size"] = f.Size;
                if (f.Id >= 0) d["id"] = f.Id;
                if (f.Kind != "page") d["blocks"] = f.Blocks;
                if (f.W > 0) { d["w"] = f.W; d["h"] = f.H; }
                if (f.Gray) d["gray"] = true;
                if (f.Ranges.Count > 0)
                {
                    List<object> rs = new List<object>();
                    foreach (string[] x in f.Ranges)
                    {
                        Dictionary<string, object> rr = new Dictionary<string, object>();
                        rr["label"] = x[0]; rr["start"] = x[1]; rr["end"] = x[2];
                        rs.Add(rr);
                    }
                    d["ranges"] = rs;
                }
                fl.Add(d);
            }
            r["files"] = fl;
            List<object> used = new List<object>();
            foreach (KeyValuePair<int, string> kv in UsedIds(files))
            {
                Dictionary<string, object> u = new Dictionary<string, object>();
                u["id"] = kv.Key; u["file"] = kv.Value;
                used.Add(u);
            }
            r["usedIds"] = used;
            r["reservedIds"] = Reserved;
            r["freeIds"] = FreeIds(files, 1);

            if (withUsage)
            {
                Dictionary<int, List<object>> usage = FontUsage();
                Dictionary<int, string> ids = UsedIds(files);
                Dictionary<string, object> byFont = new Dictionary<string, object>();
                List<object> missing = new List<object>();
                foreach (KeyValuePair<int, List<object>> kv in usage)
                {
                    byFont[kv.Key.ToString()] = kv.Value;
                    // 0 — встроенный ASCII-шрифт дисплея, его файла может и не быть.
                    if (kv.Key != 0 && !ids.ContainsKey(kv.Key))
                    {
                        Dictionary<string, object> m = new Dictionary<string, object>();
                        m["fontId"] = kv.Key;
                        m["usedBy"] = kv.Value;
                        missing.Add(m);
                    }
                }
                r["fontUsage"] = byFont;
                r["missingFonts"] = missing;
            }
            return r;
        }


        // Запись шрифта в DWIN_SET. Старую версию кладём в <проект>\DgusPlus_backup, не в DWIN_SET
        // (всё из DWIN_SET уходит в дисплей).
        // replace — имя старого файла шрифта с тем же номером, но другим именем (например, другие
        // диапазоны): он уходит в резервную копию и удаляется. Сначала все проверки, потом изменения.
        public static string Save(string name, byte[] data, bool overwrite, string replace)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Invalid file name.");
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext != ".bin" && ext != ".dzk" && ext != ".hzk")
                throw new ArgumentException("Only fonts can be saved (.bin/.dzk/.hzk).");
            if (!LeadId.IsMatch(name))
                throw new ArgumentException("The name must start with the font number, e.g. 24_….bin.");
            if (string.Equals(replace, name, StringComparison.OrdinalIgnoreCase)) replace = null;
            if (!string.IsNullOrEmpty(replace))
            {
                if (Path.GetFileName(replace) != replace) throw new ArgumentException("Invalid name of the file to replace.");
                if (KindOf(replace) != "font") throw new ArgumentException("Only a font can be replaced.");
            }

            string dir = DwinSet;
            string path = Path.Combine(dir, name);

            // Номер (и следующие, если файл больше 256 КБ) не должен быть занят другим файлом.
            int id = int.Parse(LeadId.Match(name).Groups[1].Value);
            int blocks = (int)Math.Max(1, (data.Length + BlockSize - 1) / BlockSize);
            if (Array.IndexOf(Reserved, id) >= 0)
                throw new ArgumentException("ID " + id + " is reserved by DGUS (0, 13, 14, 22).");
            foreach (FileInfo f in Scan())
            {
                if (f.Kind == "page" || f.Id < 0) continue;
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (replace != null && string.Equals(f.Name, replace, StringComparison.OrdinalIgnoreCase)) continue;
                if (f.Id < id + blocks && id < f.Id + f.Blocks)
                    throw new ArgumentException("ID " + id + (blocks > 1 ? "…" + (id + blocks - 1) : "") +
                                                " is already taken by " + f.Name + ".");
            }
            if (File.Exists(path) && !overwrite) throw new IOException("exists");

            string backup = null;
            if (replace != null && File.Exists(Path.Combine(dir, replace)))
            {
                backup = Backup(Path.Combine(dir, replace));
                File.Delete(Path.Combine(dir, replace));
            }
            if (File.Exists(path)) backup = Backup(path);
            File.WriteAllBytes(path, data);
            return backup;
        }

        static string Backup(string path)
        {
            string bdir = Path.Combine(Globel.ProjectPath, "DgusPlus_backup");
            Directory.CreateDirectory(bdir);
            string stem = Path.GetFileNameWithoutExtension(path) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dst = Path.Combine(bdir, stem + Path.GetExtension(path));
            // Две копии за одну секунду не должны затирать друг друга.
            for (int n = 2; File.Exists(dst); n++)
                dst = Path.Combine(bdir, stem + "-" + n + Path.GetExtension(path));
            File.Copy(path, dst, false);
            return dst;
        }
    }
}
