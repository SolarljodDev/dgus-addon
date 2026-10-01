using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using BizDraw;
using BizDraw.ConfigInput;
using BizDraw.ConfigShow;
using BizDraw.Controls;
using BizDraw.Core;
using BizDraw.Objects;
using BizDraw.Tools;

namespace DgusPlus
{
    // Инструменты MCP. Все обращения к DGUS идут через Ui() — в потоке интерфейса.
    // Правки проходят через историю отмены DGUS+ (Ctrl+Z в DGUS откатывает и их).
    static class McpTools
    {
        public const string Instructions =
            "DGUS+ — живой доступ к проекту, открытому в редакторе DWIN DGUS (экраны T5L). " +
            "Страница = картинка фона + элементы (display — вывод, input — касания/ввод). " +
            "Координаты — пиксели экрана устройства. VP/SP — hex-строки (\"5000\"), длина VP — в словах. " +
            "id элемента — позиция в списке страницы (0 — самый верхний); после create/duplicate/delete " +
            "id сдвигаются, перечитывайте get_page. Все правки сразу видны в DGUS и отменяются Ctrl+Z, " +
            "но на диск попадают только после save_project — вызывайте его, только когда пользователь попросил сохранить. " +
            "Чтобы увидеть результат, используйте render_page. " +
            "Шрифты: сначала preview_font (смотрите превью и touchesEdge — глифы, упёршиеся в край ячейки), " +
            "подберите font_size/baseline/сдвиги, затем generate_font с id из project_files.freeIds.";

        delegate object Handler(Args a);

        class McpTool
        {
            public string Name, Description, Schema;
            public Handler Run;
            public bool OffUi;   // долгие инструменты (генерация шрифта) — не держать поток интерфейса DGUS
        }

        static readonly List<McpTool> tools = new List<McpTool>();

        static void AddOffUi(string name, string desc, string schemaProps, string required, Handler run)
        {
            Add(name, desc, schemaProps, required, run);
            tools[tools.Count - 1].OffUi = true;
        }

        static void Add(string name, string desc, string schemaProps, string required, Handler run)
        {
            McpTool t = new McpTool();
            t.Name = name;
            t.Description = desc;
            t.Schema = "{\"type\":\"object\",\"properties\":{" + schemaProps + "}" +
                       (required.Length > 0 ? ",\"required\":[" + required + "]" : "") + "}";
            t.Run = run;
            tools.Add(t);
        }

        const string PageP = "\"page\":{\"type\":\"integer\",\"description\":\"Номер страницы (колонка Location в Images View)\"}";
        const string IdP = "\"id\":{\"type\":\"integer\",\"description\":\"id элемента из get_page\"}";
        const string PropsP = "\"props\":{\"type\":\"object\",\"description\":\"Свойства: x, y, w, h, locked, name, vp, sp и любые свойства конфигурации из get_element (имена без учёта регистра)\"}";

        static McpTools()
        {
            Add("dgus_status", "Состояние DGUS: открытый проект, разрешение, текущая страница, число страниц.",
                "", "", Status);
            Add("list_pages", "Список страниц проекта: номер, файл фона, число элементов.",
                "", "", ListPages);
            Add("add_page", "Добавить страницу проекта из картинки в папке проекта (как «Add» в Images View). Номер — из имени файла (06.png → 6). Элементов на странице нет.",
                PageP + ",\"file\":{\"type\":\"string\",\"description\":\"Имя файла в папке проекта; по умолчанию NN.png\"}", "\"page\"", AddPage);
            Add("get_page", "Все элементы страницы: id, тип, имя, VP-диапазон, SP, прямоугольник, блокировка, конфликты VP.",
                PageP, "\"page\"", GetPage);
            Add("get_element", "Полное описание элемента со всеми свойствами конфигурации (что можно менять через set_element).",
                PageP + "," + IdP, "\"page\",\"id\"", GetElement);
            Add("set_element", "Изменить элемент: положение/размер и любые свойства. Возвращает элемент после правки и список непринятых свойств.",
                PageP + "," + IdP + "," + PropsP, "\"page\",\"id\",\"props\"", SetElement);
            Add("create_element", "Создать элемент так же, как рисованием мышью в DGUS (с настройками по умолчанию). Тип — код из list_element_types (имена не уникальны: два типа называются Text).",
                PageP + ",\"type\":{\"type\":[\"string\",\"integer\"]}," +
                "\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"},\"w\":{\"type\":\"integer\"},\"h\":{\"type\":\"integer\"}," + PropsP,
                "\"page\",\"type\",\"x\",\"y\",\"w\",\"h\"", CreateElement);
            Add("duplicate_element", "Копия элемента со всеми настройками, со смещением и/или на другую страницу, с правкой свойств (например, новый vp).",
                PageP + "," + IdP + ",\"dx\":{\"type\":\"integer\"},\"dy\":{\"type\":\"integer\"}," +
                "\"to_page\":{\"type\":\"integer\"}," + PropsP, "\"page\",\"id\"", DuplicateElement);
            Add("delete_element", "Удалить элемент.", PageP + "," + IdP, "\"page\",\"id\"", DeleteElement);
            Add("select_element", "Открыть страницу в DGUS, выделить элемент и прокрутить к нему — показать пользователю.",
                PageP + "," + IdP, "\"page\",\"id\"", SelectElement);
            Add("render_page", "Картинка страницы (PNG): фон и элементы; labels=true — с рамками и подписями «-Имя, VP» как в редакторе.",
                PageP + ",\"labels\":{\"type\":\"boolean\",\"default\":true},\"scale\":{\"type\":\"number\",\"default\":1}",
                "\"page\"", RenderPage);
            Add("vp_map", "Все занятые VP по проекту (или странице) с длинами и пересечениями диапазонов.",
                "\"page\":{\"type\":\"integer\",\"description\":\"Необязательно: только эта страница\"}", "", VpMap);
            Add("find_free_vp", "Найти свободный диапазон VP нужной длины (в словах), не пересекающийся ни с одним элементом проекта.",
                "\"size\":{\"type\":\"integer\",\"default\":1},\"start\":{\"type\":\"string\",\"default\":\"1000\"}", "", FindFreeVp);
            Add("project_files", "Файлы DWIN_SET: номера (ID) и сколько блоков по 256 КБ занимают, шрифты с размером ячейки и диапазонами, " +
                "свободные номера, какие элементы на какой шрифт ссылаются (Font0_Id/Font1_Id/Lib_Id) и ссылки на несуществующие шрифты.",
                "", "", delegate(Args a) { RequireProject(); return ProjectFiles.Summary(true); });
            const string FontP =
                "\"font\":{\"type\":\"string\",\"description\":\"Путь к .ttf/.otf/.woff или имя установленного шрифта (\\\"Inter Bold\\\"). См. list_system_fonts\"}," +
                "\"w\":{\"type\":\"integer\",\"description\":\"Ширина ячейки, px (Model size X)\"}," +
                "\"h\":{\"type\":\"integer\",\"description\":\"Высота ячейки, px (Model size Y); по умолчанию = w\"}," +
                "\"font_size\":{\"type\":\"number\",\"description\":\"Кегль, px; по умолчанию 0.75·h\"}," +
                "\"baseline\":{\"type\":\"number\",\"description\":\"Базовая линия — строка от верха ячейки; по умолчанию 0.8·h\"}," +
                "\"x_shift\":{\"type\":\"number\"},\"y_shift\":{\"type\":\"number\"}," +
                "\"threshold\":{\"type\":\"integer\",\"description\":\"Порог 0–255, по умолчанию 128 (только для mode=mono)\"}," +
                "\"mode\":{\"type\":\"string\",\"enum\":[\"mono\",\"gray\"],\"description\":\"mono — чёрно-белый 1 бит (по умолчанию); gray — серый 4 бита, 16 уровней, Unicode-таблица (~320 КБ) + глифы; такой шрифт выводит элемент Text II (тип 122) с настоящими кодами Unicode, ширина глифов пропорциональная\"}," +
                "\"gamma\":{\"type\":\"number\",\"description\":\"Только для mode=gray: 0.3–3, по умолчанию 1; меньше — текст плотнее, больше — тоньше\"}," +
                "\"space\":{\"type\":\"integer\",\"description\":\"Только для mode=gray: ширина пробела, px; по умолчанию треть кегля\"}," +
                "\"weight\":{\"type\":\"integer\",\"description\":\"Вес 1–1000 для вариативных шрифтов (400 Regular, 700 Bold)\"}," +
                "\"ranges\":{\"type\":\"array\",\"description\":\"Диапазоны по порядку: \\\"ascii\\\", \\\"cyrillic\\\" (0400–04FF), \\\"cyrillic-basic\\\" (0401, 0410–044F, 0451), \\\"latin1\\\", \\\"digits\\\", \\\"0020-007E\\\" или {label,start,end}. По умолчанию [\\\"ascii\\\"]\",\"items\":{}}," +
                "\"sample\":{\"type\":\"string\",\"description\":\"Строка для превью\"}";
            AddOffUi("preview_font", "Пробная растеризация шрифта тем же кодом, что font-generator, без сохранения: " +
                "картинка (проба строки + все глифы), размер файла, глифы, которых, возможно, нет в шрифте. " +
                "Используйте для подбора font_size/baseline/сдвигов перед generate_font.",
                FontP, "\"font\",\"w\"", delegate(Args a) { return FontTool(a, false); });
            AddOffUi("generate_font", "Сгенерировать .bin-шрифт (как font-generator) и сохранить в DWIN_SET проекта под номером id " +
                "(с проверкой занятых номеров и резервной копией). Имя файла: <id>_<шрифт>_<w>_<h>_<диапазоны>.bin.",
                FontP + ",\"id\":{\"type\":\"integer\",\"description\":\"Номер шрифта (свободные — project_files)\"}," +
                "\"name\":{\"type\":\"string\",\"description\":\"Своё имя файла без номера и .bin\"}," +
                "\"replace\":{\"type\":\"string\",\"description\":\"Имя существующего файла шрифта, который заменить\"}," +
                "\"overwrite\":{\"type\":\"boolean\"}", "\"font\",\"w\",\"id\"", delegate(Args a) { return FontTool(a, true); });
            AddOffUi("list_system_fonts", "Установленные в Windows шрифты (имя и файл) — для параметра font.",
                "\"filter\":{\"type\":\"string\"}", "", ListSystemFonts);
            Add("generate_config", "Кнопка Generate DGUS: пересобрать 13TouchFile.bin/14ShowFile.bin/22_Config.bin из страниц проекта. " +
                "Нужно после правок элементов, перед заливкой в дисплей. Сообщения DGUS возвращаются текстом.",
                "", "", GenerateConfig);
            AddOffUi("upload_to_display", "Залить файлы DWIN_SET в дисплей по UART (без SD-карты) — в фоне; ход — upload_status. " +
                "Работает и через контроллер между ПК и дисплеем, если его прошивка сама пересылает кадры DWIN как есть. " +
                "Заливаются .bin/.icl/.hzk/.dzk с номером в имени. После правок страниц сначала generate_config. " +
                "В конце дисплей перезагружается.",
                "\"files\":{\"type\":\"array\",\"description\":\"Имена файлов или номера (\\\"24\\\", \\\"14ShowFile.bin\\\"); [\\\"all\\\"] — все\",\"items\":{}}," +
                "\"port\":{\"type\":\"string\",\"description\":\"COM-порт; по умолчанию последний. Список — upload_status\"}," +
                "\"baud\":{\"type\":\"integer\",\"description\":\"По умолчанию последняя использованная (сначала 115200)\"}",
                "\"files\"", UploadToDisplay);
            AddOffUi("upload_status", "Ход заливки в дисплей (upload_to_display): состояние, процент, текущий файл, журнал; плюс список COM-портов.",
                "", "", UploadStatus);
            Add("list_element_types", "Типы элементов для create_element: код, имя DGUS, ввод или вывод.",
                "", "", ListTypes);
            Add("undo", "Отменить последнее изменение на странице (как Ctrl+Z).", PageP, "", delegate(Args a) { return UndoRedo(a, true); });
            Add("redo", "Повторить отменённое изменение на странице (как Ctrl+Y).", PageP, "", delegate(Args a) { return UndoRedo(a, false); });
            Add("save_project", "Сохранить проект DGUS на диск (как кнопка Save). Только по явной просьбе пользователя.",
                "", "", SaveProject);
        }

        public static List<object> Definitions()
        {
            List<object> res = new List<object>();
            foreach (McpTool t in tools)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["name"] = t.Name;
                d["description"] = t.Description;
                d["inputSchema"] = McpServer.Json.DeserializeObject(t.Schema);
                res.Add(d);
            }
            return res;
        }

        public static Dictionary<string, object> Call(string name, Dictionary<string, object> args)
        {
            McpTool tool = null;
            foreach (McpTool t in tools) if (t.Name == name) tool = t;
            if (tool == null) throw new ArgumentException("Нет инструмента " + name);

            object result = null;
            string error = null;
            try { result = tool.OffUi ? tool.Run(new Args(args)) : Ui(tool.Run, new Args(args)); }
            catch (Exception ex)
            {
                Exception e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = e.Message;
                if (!(e is ArgumentException)) Plus.Log(e);
            }

            List<object> content = new List<object>();
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (error != null)
            {
                content.Add(Text(error));
                r["isError"] = true;
            }
            else if (result is ImageResult)
            {
                ImageResult img = (ImageResult)result;
                Dictionary<string, object> c = new Dictionary<string, object>();
                c["type"] = "image";
                c["data"] = System.Convert.ToBase64String(img.Png);
                c["mimeType"] = "image/png";
                content.Add(c);
                content.Add(Text(img.Caption));
            }
            else content.Add(Text(McpServer.Json.Serialize(result)));
            r["content"] = content;
            return r;
        }

        static Dictionary<string, object> Text(string s)
        {
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["type"] = "text";
            c["text"] = s;
            return c;
        }

        class ImageResult { public byte[] Png; public string Caption; }

        static object Ui(Handler h, Args a)
        {
            Form f = Plus.MainForm;
            if (f == null || f.IsDisposed || Plus.Space == null) throw new ArgumentException("DGUS ещё не запущен.");
            if (!f.InvokeRequired) return h(a);
            return f.Invoke(h, a);
        }

        static void RequireProject()
        {
            if (Plus.CurrentDoc == null || Pages.All().Count == 0)
                throw new ArgumentException("В DGUS не открыт проект.");
        }


        class Args
        {
            readonly Dictionary<string, object> d;
            public Args(Dictionary<string, object> d) { this.d = d ?? new Dictionary<string, object>(); }
            public bool Has(string k) { return d.ContainsKey(k) && d[k] != null; }
            public int Int(string k)
            {
                if (!Has(k)) throw new ArgumentException("Нужен параметр " + k);
                return System.Convert.ToInt32(d[k], CultureInfo.InvariantCulture);
            }
            public int Int(string k, int dflt) { return Has(k) ? Int(k) : dflt; }
            public object Raw(string k) { return Has(k) ? d[k] : null; }
            public string Str(string k) { return Has(k) ? System.Convert.ToString(d[k], CultureInfo.InvariantCulture) : null; }
            public double Dbl(string k, double dflt) { return Has(k) ? System.Convert.ToDouble(d[k], CultureInfo.InvariantCulture) : dflt; }
            public bool Bool(string k, bool dflt) { return Has(k) ? System.Convert.ToBoolean(d[k]) : dflt; }
            public Dictionary<string, object> Obj(string k) { return Has(k) ? d[k] as Dictionary<string, object> : null; }
        }


        static Dictionary<string, object> Summary(Document doc, int id, List<VpEntry> vps)
        {
            DrawRectangle r = (DrawRectangle)doc.Items[id];
            object cfg = r.ConfigObject;
            Dictionary<string, object> e = new Dictionary<string, object>();
            e["id"] = id;
            e["type"] = VpModel.TypeOf(r);
            e["typeCode"] = r.f13Type;
            e["kind"] = r.f13Type < 100 ? "input" : "display";
            e["name"] = cfg == null ? "" : (R.Get(cfg, "Var_Name") as string ?? "");
            foreach (VpEntry v in vps)
            {
                if (v.Obj != r) continue;
                string conflict = v.Conflict == VpConflict.None ? null :
                    (v.Conflict == VpConflict.Overlap ? "overlap with " : "shared with ") + v.ConflictWith;
                if (v.IsAux)
                {
                    string k = v.Kind == "SP" ? "sp" : "ap";
                    e[k] = v.RangeText;
                    if (conflict != null) e[k + "Conflict"] = conflict;
                    continue;
                }
                e["vp"] = v.VpText;
                e["vpWords"] = v.Len;
                if (conflict != null) e["vpConflict"] = conflict;
            }
            ShowBase sb = cfg as ShowBase;
            if (!e.ContainsKey("sp") && sb != null && !string.IsNullOrEmpty(sb.VarDescAddress) &&
                sb.VarDescAddress.ToUpperInvariant() != "FFFF")
                e["sp"] = sb.VarDescAddress;
            e["x"] = r.Rectangle.X; e["y"] = r.Rectangle.Y;
            e["w"] = r.Rectangle.Width; e["h"] = r.Rectangle.Height;
            if (r.bLocked) e["locked"] = true;
            return e;
        }

        static Dictionary<string, object> Full(Document doc, int id)
        {
            Dictionary<string, object> e = Summary(doc, id, VpModel.Collect(doc));
            object cfg = ((DrawRectangle)doc.Items[id]).ConfigObject;
            Dictionary<string, object> props = new Dictionary<string, object>();
            List<object> ro = new List<object>();
            if (cfg != null)
            {
                foreach (PropertyInfo p in cfg.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (p.GetIndexParameters().Length > 0 || !p.CanRead || !Simple(p.PropertyType)) continue;
                    object v;
                    try { v = p.GetValue(cfg, null); }
                    catch { continue; }
                    props[p.Name] = ToJson(v);
                    if (!p.CanWrite || p.GetSetMethod() == null) ro.Add(p.Name);
                }
            }
            e["config"] = props;
            e["readOnly"] = ro;
            return e;
        }

        static bool Simple(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);
        }

        static object ToJson(object v)
        {
            if (v == null) return null;
            if (v.GetType().IsEnum) return v.ToString();
            if (v is byte || v is sbyte || v is short || v is ushort || v is uint) return System.Convert.ToInt64(v);
            if (v is char) return v.ToString();
            return v;
        }

        static Document DocOf(Args a, out PageRef p)
        {
            RequireProject();
            p = Pages.Get(a.Int("page"));
            if (p.Doc == null) throw new ArgumentException("Страница " + p.Number + " пуста.");
            return p.Doc;
        }

        static DrawRectangle Elem(Document doc, int id)
        {
            if (id < 0 || id >= doc.Items.Count)
                throw new ArgumentException("Нет элемента id=" + id + " (на странице " + doc.Items.Count + " элементов).");
            DrawRectangle r = doc.Items[id] as DrawRectangle;
            if (r == null) throw new ArgumentException("Элемент id=" + id + " не является элементом DGUS.");
            return r;
        }


        static List<object> ApplyProps(DrawRectangle r, Dictionary<string, object> props)
        {
            List<object> rejected = new List<object>();
            if (props == null) return rejected;
            Rectangle rc = r.Rectangle;
            object cfg = r.ConfigObject;
            foreach (KeyValuePair<string, object> kv in props)
            {
                string k = kv.Key.ToLowerInvariant();
                try
                {
                    switch (k)
                    {
                        case "x": rc.X = System.Convert.ToInt32(kv.Value); continue;
                        case "y": rc.Y = System.Convert.ToInt32(kv.Value); continue;
                        case "w": case "width": rc.Width = System.Convert.ToInt32(kv.Value); continue;
                        case "h": case "height": rc.Height = System.Convert.ToInt32(kv.Value); continue;
                        case "locked": r.bLocked = System.Convert.ToBoolean(kv.Value); continue;
                        case "name": SetProp(cfg, "Var_Name", System.Convert.ToString(kv.Value)); continue;
                        case "vp": SetProp(cfg, "VarStrPoint", Hex(kv.Value)); continue;
                        case "sp": SetProp(cfg, "VarDescAddress", Hex(kv.Value)); continue;
                    }
                    SetProp(cfg, kv.Key, kv.Value);
                }
                catch (Exception ex)
                {
                    Exception e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    rejected.Add(kv.Key + ": " + e.Message);
                }
            }
            if (rc.Width < 1 || rc.Height < 1) { rejected.Add("w/h: должны быть > 0"); rc = r.Rectangle; }
            r.SetRectangle(rc.X, rc.Y, rc.Width, rc.Height);
            // Координаты внутри конфигурации DGUS пересчитывает из прямоугольника при генерации
            // (ControlList → SetPosition); делаем то же сразу, чтобы get_element не врал.
            IPositionFace pf = cfg as IPositionFace;
            if (pf != null) pf.SetPosition(r.Rectangle);
            return rejected;
        }

        // VP/SP — только hex-строкой: число 5000 неоднозначно (0x5000 или 5000?).
        static string Hex(object v)
        {
            string s = v as string;
            if (s == null) throw new ArgumentException("укажите hex-строкой, например \"5000\"");
            s = s.Trim();
            if (s.StartsWith("0x") || s.StartsWith("0X")) s = s.Substring(2);
            ushort n;
            if (!ushort.TryParse(s, NumberStyles.HexNumber, null, out n)) throw new ArgumentException("не hex: " + v);
            return n.ToString("X4");
        }

        static void SetProp(object cfg, string name, object value)
        {
            if (cfg == null) throw new ArgumentException("у элемента нет конфигурации");
            PropertyInfo p = cfg.GetType().GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p == null || !p.CanWrite || p.GetSetMethod() == null || p.GetIndexParameters().Length > 0)
                throw new ArgumentException("нет изменяемого свойства (см. get_element → config)");
            p.SetValue(cfg, ConvertTo(value, p.PropertyType), null);
        }

        static object ConvertTo(object v, Type t)
        {
            if (v == null) return null;
            if (t == typeof(string)) return System.Convert.ToString(v, CultureInfo.InvariantCulture);
            if (t.IsEnum)
            {
                string s = v as string;
                return s != null ? Enum.Parse(t, s, true) : Enum.ToObject(t, System.Convert.ToInt64(v));
            }
            if (t == typeof(bool)) return System.Convert.ToBoolean(v, CultureInfo.InvariantCulture);
            string str = v as string;
            if (str != null && (str.StartsWith("0x") || str.StartsWith("0X")))
                v = long.Parse(str.Substring(2), NumberStyles.HexNumber);
            return System.Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
        }

        static int Convert32(object v) { return System.Convert.ToInt32(v, CultureInfo.InvariantCulture); }

        static void Refresh(Document doc, DrawRectangle touched)
        {
            doc.SetDirtyFlag(true);
            VpPanel.MarkDirty();
            if (doc != Plus.CurrentDoc) return;
            if (touched != null && touched.Selected)
            {
                // Панель свойств привязана к объекту — пересоздаём её.
                touched.SetSelectedState(false);
                touched.SetSelectedState(true);
            }
            Plus.CurrentDrawArea.Refresh();
        }


        static object Status(Args a)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["project"] = Globel.ProjectPath ?? "";
            bool open = Plus.CurrentDoc != null && Pages.All().Count > 0;
            r["projectOpen"] = open;
            if (!open) return r;
            Size res = Pages.Resolution;
            r["resolution"] = res.Width + "x" + res.Height;
            PageRef cur = Pages.Current;
            r["currentPage"] = cur == null ? (object)null : cur.Number;
            r["pages"] = Pages.All().Count;
            r["zoom"] = Plus.CurrentDrawArea.Zoom;
            return r;
        }

        static object ListPages(Args a)
        {
            RequireProject();
            List<object> list = new List<object>();
            foreach (PageRef p in Pages.All())
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["page"] = p.Number;
                d["file"] = p.File;
                d["elements"] = p.Doc == null ? 0 : p.Doc.Items.Count;
                list.Add(d);
            }
            return list;
        }

        static object AddPage(Args a)
        {
            RequireProject();
            int n = a.Int("page");
            string file = a.Str("file") ?? (n.ToString("00") + ".png");
            PageRef p = Pages.Add(n, file);
            Plus.Space.m_Design.Activate();
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["page"] = p.Number;
            r["file"] = p.File;
            r["pages"] = Pages.All().Count;
            return r;
        }

        static object GetPage(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            List<VpEntry> vps = VpModel.Collect(doc);
            List<object> items = new List<object>();
            for (int i = 0; i < doc.Items.Count; i++)
                if (doc.Items[i] is DrawRectangle) items.Add(Summary(doc, i, vps));
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["page"] = p.Number;
            r["file"] = p.File;
            Size res = Pages.Resolution;
            r["resolution"] = res.Width + "x" + res.Height;
            r["elements"] = items;
            return r;
        }

        static object GetElement(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            int id = a.Int("id");
            Elem(doc, id);
            return Full(doc, id);
        }

        static object SetElement(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            int id = a.Int("id");
            DrawRectangle r = Elem(doc, id);
            Undo.BeforeEdit(doc);
            List<object> rejected = ApplyProps(r, a.Obj("props"));
            Undo.AfterEdit(doc);
            Refresh(doc, r);
            Dictionary<string, object> res = Full(doc, id);
            if (rejected.Count > 0) res["rejected"] = rejected;
            return res;
        }

        static readonly object[] TypeMap = {
            1, "VarInput", 2, "PopMenu", 3, "IncManager", 4, "DragManager", 6, "KeyCodeReturn",
            7, "KeyCodeReturn", 8, "TextInput", 9, "GBKInput", 10, "ParamCofig", 11, "Key_DO_Ret",
            12, "Rotateadjust", 13, "SwipeAdjust", 14, "SwipePage", 15, "ICONDragManager", 16, "BitButton",
            100, "IconShow", 101, "AnimateShow", 102, "SliderShow", 103, "ArtTextShow", 104, "ImageAnimateShow",
            105, "ICONRotate", 106, "DataTextShow", 107, "TextShow", 108, "RtcTextShow", 109, "Clock_Dis",
            110, "ChartShow", 112, "BaseGraphs", 113, "BaseApply", 114, "bitIcon", 115, "TimeDisplay",
            116, "ScrTextShow", 117, "Two_Dimension", 118, "ZoneScrolling", 119, "Data_Window",
            120, "ZoneLightSetting", 121, "AuxiliaryDisplay", 122, "TextShowII", 123, "IconPageTrans",
            124, "VariableJpeg", 125, "BatchIconShow", 126, "Roller_character", 127, "GTFShow",
            128, "CameraShow", 129, "VideoShow", 130, "DW_ProcessBar", 200, "MultiLanguage", 202, "RotaryKnob" };

        static string TypeName(int code, string cls)
        {
            if (code == 6) return "Basic Touch";
            if (code == 7) return "Return Key code";
            Type t = typeof(ShowBase).Assembly.GetType((code < 100 ? "BizDraw.ConfigInput." : "BizDraw.ConfigShow.") + cls);
            try
            {
                string d = t == null ? null : R.Get(Activator.CreateInstance(t), "Desc") as string;
                return string.IsNullOrEmpty(d) ? cls : d.Trim();
            }
            catch { return cls; }
        }

        static object ListTypes(Args a)
        {
            List<object> list = new List<object>();
            for (int i = 0; i < TypeMap.Length; i += 2)
            {
                int code = (int)TypeMap[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["code"] = code;
                d["type"] = TypeName(code, (string)TypeMap[i + 1]);
                d["class"] = TypeMap[i + 1];
                d["kind"] = code < 100 ? "input" : "display";
                list.Add(d);
            }
            return list;
        }

        static int ResolveType(object t)
        {
            if (!(t is string)) return Convert32(t);
            string s = ((string)t).Trim();
            int n;
            if (int.TryParse(s, out n)) return n;
            for (int i = 0; i < TypeMap.Length; i += 2)
            {
                int code = (int)TypeMap[i];
                if (string.Equals(s, (string)TypeMap[i + 1], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, TypeName(code, (string)TypeMap[i + 1]), StringComparison.OrdinalIgnoreCase))
                    return code;
            }
            throw new ArgumentException("Неизвестный тип «" + s + "». См. list_element_types.");
        }

        static object CreateElement(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            int code = ResolveType(a.Raw("type"));
            bool known = false;
            for (int i = 0; i < TypeMap.Length; i += 2) if ((int)TypeMap[i] == code) known = true;
            if (!known) throw new ArgumentException("Тип " + code + " нельзя создать. См. list_element_types.");
            int x = a.Int("x"), y = a.Int("y"), w = a.Int("w"), h = a.Int("h");
            if (w < 1 || h < 1) throw new ArgumentException("w и h должны быть > 0");

            Pages.Show(p);
            DrawArea da = Plus.CurrentDrawArea;
            Undo.BeforeEdit(doc);
            Tool oldTool = da.ActiveTool;
            int oldType = da.VarType;
            try
            {
                // Ровно то, что происходит при рисовании элемента мышью.
                da.VarType = code;
                ToolRectangle tool = new ToolRectangle();
                da.ActiveTool = tool;
                tool.OnMouseDown(da, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
                tool.OnMouseMove(da, new MouseEventArgs(MouseButtons.Left, 1, x + w, y + h, 0));
                tool.OnMouseUp(da, new MouseEventArgs(MouseButtons.Left, 1, x + w, y + h, 0));
            }
            finally
            {
                da.ActiveTool = oldTool is ToolPointer ? oldTool : new ToolPointer();
                da.VarType = oldType;
            }
            DrawRectangle r = (DrawRectangle)doc.Items[0];
            object cfg = r.ConfigObject;
            if (cfg != null && string.IsNullOrEmpty(R.Get(cfg, "Var_Name") as string))
                R.Set(cfg, "Var_Name", TypeName(code, cfg.GetType().Name));
            r.SetRectangle(x, y, w, h);
            List<object> rejected = ApplyProps(r, a.Obj("props"));
            Undo.AfterEdit(doc);
            Refresh(doc, r);
            Dictionary<string, object> res = Full(doc, 0);
            res["note"] = "Новый элемент — id 0; id остальных элементов страницы сдвинулись на +1.";
            if (rejected.Count > 0) res["rejected"] = rejected;
            return res;
        }

        static object DuplicateElement(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            DrawRectangle src = Elem(doc, a.Int("id"));
            PageRef tp = a.Has("to_page") ? Pages.Get(a.Int("to_page")) : p;
            if (tp.Doc == null) throw new ArgumentException("Страница " + tp.Number + " пуста.");
            Document target = tp.Doc;

            DrawRectangle copy = DeepCopy(src);
            Rectangle rc = copy.Rectangle;
            copy.SetRectangle(rc.X + a.Int("dx", 0), rc.Y + a.Int("dy", 0), rc.Width, rc.Height);
            copy.ScreenSize = src.ScreenSize;
            int same = 0;
            foreach (DrawObject o in target.Items)
                if ((((DrawRectangle)o).f13Type >= 100) == (copy.f13Type >= 100)) same++;
            copy.nBianhao = same;

            Undo.BeforeEdit(target);
            target.Items.Add(copy);
            List<object> rejected = ApplyProps(copy, a.Obj("props"));
            Undo.AfterEdit(target);
            Refresh(target, null);
            Dictionary<string, object> res = Full(target, 0);
            res["page"] = tp.Number;
            res["note"] = "Копия — id 0 на странице " + tp.Number + "; id остальных элементов этой страницы сдвинулись на +1.";
            if (rejected.Count > 0) res["rejected"] = rejected;
            return res;
        }

        static DrawRectangle DeepCopy(DrawRectangle r)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, r);
                ms.Position = 0;
                return (DrawRectangle)bf.Deserialize(ms);
            }
        }

        static object DeleteElement(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            int id = a.Int("id");
            DrawRectangle r = Elem(doc, id);
            Dictionary<string, object> gone = Summary(doc, id, VpModel.Collect(doc));
            Undo.BeforeEdit(doc);
            if (doc == Plus.CurrentDoc && r.Selected) { Plus.Space.CloseVarFrm(); doc.Items.UnselectAll(); }
            doc.Items.RemoveAt(id);
            Undo.AfterEdit(doc);
            Refresh(doc, null);
            Dictionary<string, object> res = new Dictionary<string, object>();
            res["deleted"] = gone;
            res["note"] = "id элементов после " + id + " сдвинулись на -1.";
            return res;
        }

        static object SelectElement(Args a)
        {
            PageRef p;
            Document doc = DocOf(a, out p);
            DrawRectangle r = Elem(doc, a.Int("id"));
            Pages.Show(p);
            doc.Items.UnselectAll();
            r.SetSelectedState(true);
            Plus.ScrollTo(r.Rectangle);
            Plus.CurrentDrawArea.Refresh();
            return "ok";
        }

        static object RenderPage(Args a)
        {
            PageRef p;
            DocOf(a, out p);
            bool labels = !a.Has("labels") || System.Convert.ToBoolean(a.Raw("labels"));
            float scale = a.Has("scale") ? (float)System.Convert.ToDouble(a.Raw("scale"), CultureInfo.InvariantCulture) : 1f;
            scale = Math.Max(0.1f, Math.Min(2f, scale));
            ImageResult img = new ImageResult();
            img.Png = Pages.RenderPng(p, labels, scale);
            Size res = Pages.Resolution;
            img.Caption = "Страница " + p.Number + " (" + p.File + "), " + res.Width + "x" + res.Height +
                          (scale != 1f ? ", масштаб " + scale : "");
            return img;
        }

        static object VpMap(Args a)
        {
            RequireProject();
            List<object> list = new List<object>();
            List<PageRef> pages = a.Has("page") ? new List<PageRef>(new PageRef[] { Pages.Get(a.Int("page")) }) : Pages.All();
            List<KeyValuePair<PageRef, VpEntry>> all = new List<KeyValuePair<PageRef, VpEntry>>();
            foreach (PageRef p in pages)
                if (p.Doc != null)
                    foreach (VpEntry e in VpModel.Collect(p.Doc)) all.Add(new KeyValuePair<PageRef, VpEntry>(p, e));
            all.Sort(delegate(KeyValuePair<PageRef, VpEntry> x, KeyValuePair<PageRef, VpEntry> y)
            {
                int c = x.Value.Vp.CompareTo(y.Value.Vp);
                return c != 0 ? c : x.Key.Number.CompareTo(y.Key.Number);
            });
            for (int i = 0; i < all.Count; i++)
            {
                VpEntry e = all[i].Value;
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["vp"] = e.RangeText;
                d["words"] = e.Len;
                d["page"] = all[i].Key.Number;
                d["id"] = all[i].Key.Doc.Items.IndexOf(e.Obj);
                d["type"] = e.Type;
                if (e.Name.Length > 0) d["name"] = e.Name;
                d["kind"] = e.IsInput ? "input" : "display";
                // Пересечения по всему проекту: одинаковый адрес+длина — общая переменная, иначе ошибка.
                List<object> overlaps = new List<object>();
                for (int j = 0; j < all.Count; j++)
                {
                    if (j == i) continue;
                    VpEntry o = all[j].Value;
                    bool hit = o.Vp < e.Vp + e.Len && e.Vp < o.Vp + o.Len;
                    if (hit && !(o.Vp == e.Vp && o.Len == e.Len))
                        overlaps.Add(o.RangeText + " " + o.Type + " (стр. " + all[j].Key.Number + ")");
                }
                if (overlaps.Count > 0) d["overlaps"] = overlaps;
                list.Add(d);
            }
            return list;
        }

        static object FindFreeVp(Args a)
        {
            RequireProject();
            int size = Math.Max(1, a.Int("size", 1));
            int start = a.Has("start") ? int.Parse(Hex(a.Raw("start")), NumberStyles.HexNumber) : 0x1000;
            List<int[]> used = new List<int[]>();
            foreach (PageRef p in Pages.All())
                if (p.Doc != null)
                    foreach (VpEntry e in VpModel.Collect(p.Doc)) used.Add(new int[] { e.Vp, e.Vp + e.Len });
            used.Sort(delegate(int[] x, int[] y) { return x[0].CompareTo(y[0]); });
            int cand = start;
            foreach (int[] u in used)
            {
                if (u[1] <= cand) continue;
                if (u[0] >= cand + size) break;
                cand = u[1];
            }
            if (cand + size > 0x10000) throw new ArgumentException("Нет свободного диапазона такой длины после " + start.ToString("X4"));
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["vp"] = cand.ToString("X4");
            r["end"] = (cand + size - 1).ToString("X4");
            r["note"] = "Проверены все страницы проекта. Длины переменных оценены по типам элементов.";
            return r;
        }

        static object UndoRedo(Args a, bool undo)
        {
            RequireProject();
            if (a.Has("page")) Pages.Show(Pages.Get(a.Int("page")));
            if (undo) Undo.DoUndo(); else Undo.DoRedo();
            PageRef cur = Pages.Current;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["page"] = cur == null ? (object)null : cur.Number;
            r["canUndo"] = Undo.CanUndo;
            r["canRedo"] = Undo.CanRedo;
            return r;
        }


        static object ListSystemFonts(Args a)
        {
            string f = (a.Str("filter") ?? "").ToLowerInvariant();
            List<object> res = new List<object>();
            foreach (FontJobs.SystemFont s in FontJobs.Installed())
            {
                if (f.Length > 0 && s.Name.ToLowerInvariant().IndexOf(f) < 0) continue;
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["name"] = s.Name;
                d["file"] = s.File;
                res.Add(d);
            }
            return res;
        }

        static readonly Dictionary<string, string[][]> RangePresets = new Dictionary<string, string[][]>();

        static string[][] Preset(string key)
        {
            if (RangePresets.Count == 0)
            {
                RangePresets["ascii"] = new string[][] { new string[] { "ASCII", "0020", "007E" } };
                RangePresets["cyrillic"] = new string[][] { new string[] { "Cyrillic", "0400", "04FF" } };
                RangePresets["cyrillic-basic"] = new string[][] {
                    new string[] { "Yo", "0401", "0401" }, new string[] { "Cyr", "0410", "044F" }, new string[] { "yo", "0451", "0451" } };
                RangePresets["latin1"] = new string[][] { new string[] { "Latin1", "00A0", "00FF" } };
                RangePresets["digits"] = new string[][] { new string[] { "Digits", "0030", "0039" } };
            }
            string[][] r;
            return RangePresets.TryGetValue(key.ToLowerInvariant(), out r) ? r : null;
        }

        static List<object> ParseRanges(object raw)
        {
            List<object> res = new List<object>();
            IEnumerable items = raw as IEnumerable;
            if (raw == null || raw is string) items = new object[] { raw ?? "ascii" };
            foreach (object it in items)
            {
                Dictionary<string, object> d = it as Dictionary<string, object>;
                string label, start, end;
                if (d != null)
                {
                    label = d.ContainsKey("label") ? System.Convert.ToString(d["label"]) : "Range";
                    start = System.Convert.ToString(d["start"]);
                    end = System.Convert.ToString(d["end"]);
                    res.Add(Range(label, start, end));
                    continue;
                }
                string s = System.Convert.ToString(it).Trim();
                string[][] preset = Preset(s);
                if (preset != null) { foreach (string[] p in preset) res.Add(Range(p[0], p[1], p[2])); continue; }
                string[] se = s.Replace("U+", "").Replace("u+", "").Split('-');
                if (se.Length != 2) throw new ArgumentException("Не понял диапазон «" + s + "». Пример: \"0400-04FF\" или \"cyrillic\".");
                label = "U" + se[0].Trim().ToUpperInvariant();
                res.Add(Range(label, se[0], se[1]));
            }
            if (res.Count == 0) res.Add(Range("ASCII", "0020", "007E"));
            return res;
        }

        static Dictionary<string, object> Range(string label, string start, string end)
        {
            int s = int.Parse(start.Trim(), NumberStyles.HexNumber), e = int.Parse(end.Trim(), NumberStyles.HexNumber);
            if (e < s) throw new ArgumentException("Диапазон " + start + "-" + end + ": конец меньше начала.");
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["label"] = label;
            d["start"] = s.ToString("X4");
            d["end"] = e.ToString("X4");
            return d;
        }

        // Строка для превью: кириллица, если она есть в диапазонах, иначе латиница; плюс цифры.
        static string DefaultSample(List<object> ranges)
        {
            bool cyr = false, lat = false;
            foreach (Dictionary<string, object> r in ranges)
            {
                int s = int.Parse((string)r["start"], NumberStyles.HexNumber), e = int.Parse((string)r["end"], NumberStyles.HexNumber);
                if (s <= 0x0436 && e >= 0x0430) cyr = true;
                if (s <= 0x61 && e >= 0x61) lat = true;
            }
            return (cyr ? "Съешь ещё мягких булок, Жёлтый ЩЫ" : "") + (cyr && lat ? " " : "") + (lat ? "Quick Brown Fox jumps" : "") + " 0123456789";
        }

        static object FontTool(Args a, bool save)
        {
            string fontFile = FontJobs.Resolve(a.Str("font"));
            int w = a.Int("w"), h = a.Int("h", w);
            if (w < 4 || h < 4 || w > 256 || h > 256) throw new ArgumentException("w и h — от 4 до 256.");
            int id = 0;
            if (save)
            {
                id = a.Int("id");
                if (string.IsNullOrEmpty(Globel.ProjectPath)) throw new ArgumentException("В DGUS не открыт проект.");
            }

            Dictionary<string, object> p = new Dictionary<string, object>();
            p["w"] = w; p["h"] = h;
            p["fontSize"] = a.Dbl("font_size", Math.Round(h * 0.75));
            p["baseline"] = a.Dbl("baseline", Math.Round(h * 0.8));
            p["xShift"] = a.Dbl("x_shift", 0);
            p["yShift"] = a.Dbl("y_shift", 0);
            p["threshold"] = a.Int("threshold", 128);
            string mode = (a.Str("mode") ?? "mono").ToLowerInvariant();
            if (mode != "mono" && mode != "gray") throw new ArgumentException("mode: mono или gray.");
            p["mode"] = mode;
            if (a.Has("gamma")) p["gamma"] = a.Dbl("gamma", 1);
            if (a.Has("space")) p["space"] = a.Int("space");
            if (a.Has("weight")) p["weight"] = a.Int("weight");
            p["ranges"] = ParseRanges(a.Raw("ranges"));
            p["fontName"] = Path.GetFileNameWithoutExtension(fontFile);
            p["sample"] = a.Str("sample") ?? DefaultSample((List<object>)p["ranges"]);

            Dictionary<string, object> res = FontJobs.Run(p, File.ReadAllBytes(fontFile), a.Int("timeout", 180));
            if (res == null || !(res.ContainsKey("ok") && System.Convert.ToBoolean(res["ok"])))
                throw new ArgumentException("Генератор: " + (res != null && res.ContainsKey("error") ? res["error"] : "нет ответа"));

            byte[] bin = System.Convert.FromBase64String((string)res["bin"]);
            Dictionary<string, object> sum = new Dictionary<string, object>();
            sum["font"] = fontFile;
            sum["cell"] = w + "x" + h;
            sum["mode"] = mode;
            sum["fontSize"] = p["fontSize"];
            sum["baseline"] = p["baseline"];
            sum["glyphs"] = res["glyphs"];
            sum["bytes"] = bin.Length;
            sum["idsNeeded"] = Math.Max(1, (bin.Length + ProjectFiles.BlockSize - 1) / ProjectFiles.BlockSize);
            if (res.ContainsKey("variableWeight")) sum["variableWeight"] = res["variableWeight"];
            foreach (string k in new string[] { "maybeMissing", "empty", "touchesEdge" })
                if (res.ContainsKey(k)) sum[k] = res[k];

            string name = (a.Str("name") ?? (string)res["name"]).Trim();
            if (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            name = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+_", "");
            if (save)
            {
                string file = id + "_" + name + ".bin";
                string backup;
                try { backup = ProjectFiles.Save(file, bin, a.Bool("overwrite", false), a.Str("replace")); }
                catch (IOException ex)
                {
                    if (ex.Message != "exists") throw;
                    throw new ArgumentException(file + " уже есть в DWIN_SET. Передайте overwrite=true, чтобы заменить (старый — в DgusPlus_backup).");
                }
                sum["saved"] = Path.Combine(ProjectFiles.DwinSet, file);
                if (backup != null) sum["backup"] = backup;
            }
            else sum["fileName"] = "<id>_" + name + ".bin";

            ImageResult img = new ImageResult();
            img.Png = System.Convert.FromBase64String((string)res["preview"]);
            img.Caption = McpServer.Json.Serialize(sum);
            return img;
        }


        static object GenerateConfig(Args a)
        {
            RequireProject();
            MethodInfo m = Plus.MainForm.GetType().GetMethod("toolStripButton2_Click",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new ArgumentException("Не найден обработчик кнопки Generate.");
            List<string> msgs;
            using (DialogCatcher dc = new DialogCatcher())
            {
                m.Invoke(Plus.MainForm, new object[] { null, EventArgs.Empty });
                msgs = dc.Messages;
            }
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["messages"] = msgs;
            List<object> files = new List<object>();
            foreach (string n in new string[] { "13TouchFile.bin", "14ShowFile.bin", "22_Config.bin" })
            {
                string p = Path.Combine(ProjectFiles.DwinSet, n);
                if (!File.Exists(p)) continue;
                Dictionary<string, object> f = new Dictionary<string, object>();
                f["name"] = n;
                f["modified"] = File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss");
                files.Add(f);
            }
            r["files"] = files;
            return r;
        }

        static object UploadToDisplay(Args a)
        {
            if (string.IsNullOrEmpty(Globel.ProjectPath)) throw new ArgumentException("В DGUS не открыт проект.");
            string dir = ProjectFiles.DwinSet;
            List<string> all = new List<string>();
            foreach (string p in Directory.GetFiles(dir))
                if (DisplayUpload.IsUploadable(Path.GetFileName(p))) all.Add(p);
            all.Sort(delegate(string x, string y) { return ProjectFiles.IdOf(Path.GetFileName(x)).CompareTo(ProjectFiles.IdOf(Path.GetFileName(y))); });

            List<string> pick = new List<string>();
            IEnumerable want = a.Raw("files") as IEnumerable;
            if (want == null || a.Raw("files") is string) want = new object[] { a.Str("files") };
            foreach (object o in want)
            {
                string w = System.Convert.ToString(o).Trim();
                if (w.Equals("all", StringComparison.OrdinalIgnoreCase)) { pick = new List<string>(all); break; }
                string hit = null;
                foreach (string p in all)
                {
                    string n = Path.GetFileName(p);
                    int num;
                    if (string.Equals(n, w, StringComparison.OrdinalIgnoreCase) ||
                        (int.TryParse(w, out num) && ProjectFiles.IdOf(n) == num)) { hit = p; break; }
                }
                if (hit == null)
                {
                    List<string> names = new List<string>();
                    foreach (string p in all) names.Add(Path.GetFileName(p));
                    throw new ArgumentException("Нет файла «" + w + "» для заливки. Есть: " + string.Join(", ", names.ToArray()));
                }
                if (!pick.Contains(hit)) pick.Add(hit);
            }

            string port = a.Str("port") ?? Plus.Cfg.DisplayPort;
            if (string.IsNullOrEmpty(port))
                throw new ArgumentException("Укажите COM-порт (port). Доступны: " + string.Join(", ", System.IO.Ports.SerialPort.GetPortNames()));
            Plus.Cfg.DisplayPort = port;
            int baud = a.Int("baud", Plus.Cfg.DisplayBaud);
            Plus.Cfg.DisplayBaud = baud;
            Plus.Cfg.Save();

            DisplayUpload.Job job = DisplayUpload.Start(port, baud, pick);
            Dictionary<string, object> r = new Dictionary<string, object>();
            List<string> names2 = new List<string>();
            foreach (string p in pick) names2.Add(Path.GetFileName(p));
            r["started"] = names2;
            r["port"] = port;
            r["baud"] = baud;
            r["bytes"] = job.BytesTotal;
            r["note"] = "Идёт в фоне — проверяйте upload_status.";
            return r;
        }

        static object UploadStatus(Args a)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["ports"] = System.IO.Ports.SerialPort.GetPortNames();
            r["lastPort"] = Plus.Cfg.DisplayPort;
            DisplayUpload.Job j = DisplayUpload.Current;
            if (j == null) { r["state"] = "idle"; return r; }
            r["state"] = j.State;
            r["percent"] = j.BytesTotal == 0 ? 0 : (int)(100 * j.BytesDone / j.BytesTotal);
            if (j.CurrentFile != null) r["file"] = j.CurrentFile;
            if (j.Error != null) r["error"] = j.Error;
            DateTime end = j.State == "running" ? DateTime.Now : j.Finished;
            r["elapsedSeconds"] = (int)(end - j.Started).TotalSeconds;
            lock (j.Log)
            {
                int from = Math.Max(0, j.Log.Count - 15);
                r["log"] = j.Log.GetRange(from, j.Log.Count - from);
            }
            return r;
        }

        static object SaveProject(Args a)
        {
            RequireProject();
            MethodInfo m = Plus.MainForm.GetType().GetMethod("保存SToolStripButton_Click",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new ArgumentException("Не найден обработчик кнопки Save.");
            m.Invoke(Plus.MainForm, new object[] { null, EventArgs.Empty });
            return "Проект сохранён: " + Globel.ProjectPath;
        }
    }
}
