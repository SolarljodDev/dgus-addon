using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using BizDraw.ConfigInput;
using BizDraw.ConfigShow;
using BizDraw.Core;
using BizDraw.Objects;

namespace DgusPlus
{
    enum VpConflict { None, Shared, Overlap }

    class VpEntry
    {
        public DrawRectangle Obj;
        public int Vp;
        public int Len;          // в словах
        public string Type;
        public string Name;      // пусто, если совпадает с типом
        public string Label;     // Var_Name как есть — для подписи на холсте
        public bool IsInput;
        public bool IsAux;       // служебный адрес (AP или SP), а не основной VP
        public string Kind;      // "AP" / "SP" у служебных
        public VpConflict Conflict;
        public string ConflictWith = "";

        public string VpText { get { return Vp.ToString("X4"); } }
        public string RangeText
        {
            get { return Len <= 1 ? VpText : VpText + "–" + (Vp + Len - 1).ToString("X4"); }
        }
    }

    // Сбор VP со страницы: адрес, длина в словах, пересечения диапазонов.
    static class VpModel
    {
        static readonly Dictionary<Type, string> typeNames = new Dictionary<Type, string>();

        // Название типа, как в DGUS («VAR Icon», «Text»…). Desc самого элемента брать нельзя:
        // у части классов (TextShow) он возвращает имя элемента. Берём Desc чистого экземпляра.
        public static string TypeOf(DrawRectangle r)
        {
            if (r.f13Type == 6) return "Basic Touch";
            if (r.f13Type == 7) return "Return Key code";
            object cfg = r.ConfigObject;
            if (cfg == null) return "?";
            Type t = cfg.GetType();
            string name;
            if (!typeNames.TryGetValue(t, out name))
            {
                try { name = R.Get(Activator.CreateInstance(t), "Desc") as string; }
                catch { name = null; }
                name = string.IsNullOrEmpty(name) ? t.Name : name.Trim();
                typeNames[t] = name;
            }
            return name;
        }

        public static List<VpEntry> Collect(Document doc)
        {
            List<VpEntry> list = new List<VpEntry>();
            if (doc == null) return list;
            foreach (DrawObject o in doc.Items)
            {
                DrawRectangle r = o as DrawRectangle;
                if (r == null || r.ConfigObject == null) continue;
                object cfg = r.ConfigObject;
                string vpStr = null, name = null;
                ShowBase sb = cfg as ShowBase;
                InputBase ib = cfg as InputBase;
                if (sb != null) { vpStr = sb.VarStrPoint; name = sb.Var_Name; }
                else if (ib != null) { vpStr = ib.VarStrPoint; name = ib.Var_Name; }
                else continue;

                int vp;
                if (string.IsNullOrEmpty(vpStr) ||
                    !int.TryParse(vpStr, NumberStyles.HexNumber, null, out vp)) continue;

                VpEntry e = new VpEntry();
                e.Obj = r;
                e.Vp = vp;
                e.IsInput = ib != null;
                string cls = cfg.GetType().Name;
                e.Type = TypeOf(r);
                e.Label = name ?? "";
                // Имя показываем, только если его поменяли относительно типа.
                e.Name = string.Equals(e.Label.Trim(), e.Type, StringComparison.OrdinalIgnoreCase) ? "" : e.Label;
                e.Len = LengthWords(cfg, cls);
                list.Add(e);

                // Служебные адреса элемента: AP (VP_AUX — вспомогательный адрес, у бит-иконок, 2 слова)
                // и SP (Desc_Point — описательный указатель, DGUS считает его 10 словами).
                // FFFF значит «не задан» — такие не показываем; заданные — приглушёнными строками.
                int aux;
                string auxStr = R.Get(cfg, "VarStrAUX") as string;
                if (!string.IsNullOrEmpty(auxStr) &&
                    int.TryParse(auxStr, NumberStyles.HexNumber, null, out aux) && aux != 0 && aux != 0xFFFF)
                    list.Add(Service(r, e, aux, 2, "AP"));
                int sp = R.GetInt(cfg, "Desc_Point", 0xFFFF);
                if (sp != 0xFFFF) list.Add(Service(r, e, sp, 10, "SP"));
            }
            list.Sort(delegate(VpEntry a, VpEntry b)
            {
                int c = a.Vp.CompareTo(b.Vp);
                return c != 0 ? c : string.Compare(a.Type, b.Type);
            });
            FindConflicts(list);
            return list;
        }

        static VpEntry Service(DrawRectangle r, VpEntry owner, int addr, int len, string kind)
        {
            VpEntry a = new VpEntry();
            a.Obj = r;
            a.Vp = addr;
            a.Len = len;
            a.IsAux = true;
            a.Kind = kind;
            a.Type = "auxiliary " + kind + " (" + owner.Type + ")";
            a.Label = owner.Label;
            a.Name = owner.Name;
            return a;
        }

        // Длина переменной в словах по типу элемента. Где тип неизвестен — 1 слово.
        static int LengthWords(object cfg, string cls)
        {
            switch (cls)
            {
                case "DataTextShow":
                    switch (R.GetInt(cfg, "FVarType", 0))
                    {
                        case 1: case 6: case 8: return 2;   // long, ulong, float
                        case 4: case 7: return 4;           // long long, double
                        default: return 1;
                    }
                case "VarInput":
                    switch (R.GetInt(cfg, "FVarType", 0))
                    {
                        case 1: return 2;
                        case 4: return 4;
                        default: return 1;
                    }
                case "TextShow":
                case "TextShowII":
                case "ScrTextShow":
                    return Math.Max(1, (R.GetInt(cfg, "Text_Length", 2) + 1) / 2);
                case "TextInput":
                case "GBKInput":
                    return Math.Max(1, (R.GetInt(cfg, "VP_Len_Max", 2) + 1) / 2);
                default:
                    return 1;
            }
        }

        // Одинаковый адрес и длина (индикатор + ввод на одной переменной) — «общий», это норма.
        // Частичное пересечение диапазонов — почти всегда ошибка.
        static void FindConflicts(List<VpEntry> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                VpEntry a = list[i];
                for (int j = i + 1; j < list.Count && list[j].Vp < a.Vp + a.Len; j++)
                {
                    VpEntry b = list[j];
                    // Общий AP у двух элементов — ошибка: анимации затрут друг другу состояние.
                    bool same = a.Vp == b.Vp && a.Len == b.Len && !a.IsAux && !b.IsAux;
                    Mark(a, b, same);
                    Mark(b, a, same);
                }
            }
        }

        static void Mark(VpEntry e, VpEntry other, bool same)
        {
            VpConflict c = same ? VpConflict.Shared : VpConflict.Overlap;
            if (c > e.Conflict) e.Conflict = c;
            string s = other.RangeText + " " + other.Type;
            e.ConflictWith = e.ConflictWith.Length == 0 ? s : e.ConflictWith + "; " + s;
        }

        // Какие элементы сейчас видимы с учётом фильтра «Control display» (все/ввод/вывод).
        public static bool Visible(Document doc, VpEntry e)
        {
            if (doc.Showtype == 1) return e.Obj.f13Type < 100;
            if (doc.Showtype == 2) return e.Obj.f13Type >= 100;
            return true;
        }
    }
}
