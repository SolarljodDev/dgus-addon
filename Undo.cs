using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using BizDraw.Core;
using BizDraw.Objects;

namespace DgusPlus
{
    // Отмена/повтор через снимки страницы: после каждого действия пользователя страница сериализуется тем же BinaryFormatter,
    // которым DGUS пишет .hmi; отличие от текущего снимка — новый шаг (ловятся любые правки).
    static class Undo
    {
        const int MaxSteps = 200;
        const int CoalesceMs = 1200;

        class History
        {
            public List<byte[]> States = new List<byte[]>();
            public int Pos;
            public DateTime LastPush;
            public bool LastByKeyboard;
        }

        static readonly Dictionary<Document, History> histories = new Dictionary<Document, History>();
        static bool pending;
        static bool pendingKeyboard;
        static DateTime lastInput;
        static string projectPath;

        public static void NoteInput(bool keyboard)
        {
            pending = true;
            pendingKeyboard = keyboard;
            lastInput = DateTime.Now;
        }

        public static void Poll()
        {
            string path = BizDraw.Globel.ProjectPath;
            if (path != projectPath) { histories.Clear(); projectPath = path; }

            Document doc = Plus.CurrentDoc;
            if (doc != null && !histories.ContainsKey(doc) && Control.MouseButtons == MouseButtons.None)
                Get(doc, true);

            if (!pending || Control.MouseButtons != MouseButtons.None) return;
            if ((DateTime.Now - lastInput).TotalMilliseconds < 120) return;
            pending = false;
            Capture(pendingKeyboard);
        }

        static History Get(Document doc, bool create)
        {
            History h;
            if (histories.TryGetValue(doc, out h)) return h;
            if (!create) return null;
            h = new History();
            h.States.Add(Snap(doc));
            histories[doc] = h;
            return h;
        }

        // Для правок извне (MCP): до правки — BeforeEdit, после — AfterEdit. Страница любая.
        public static void BeforeEdit(Document doc)
        {
            if (doc == null) return;
            if (!histories.ContainsKey(doc)) Get(doc, true);
            else Capture(doc, false);   // зафиксировать ручные правки, ещё не попавшие в историю
        }

        public static void AfterEdit(Document doc)
        {
            if (doc == null) return;
            Capture(doc, false);
            History h = Get(doc, false);
            if (h != null) h.LastByKeyboard = false;   // не склеивать с набором на клавиатуре
        }

        static void Capture(bool keyboard) { Capture(Plus.CurrentDoc, keyboard); }

        static void Capture(Document doc, bool keyboard)
        {
            if (doc == null) return;
            if (!histories.ContainsKey(doc)) { Get(doc, true); return; }
            History h = histories[doc];

            byte[] cur = Snap(doc);
            if (cur == null || Same(cur, h.States[h.Pos])) return;

            if (h.Pos < h.States.Count - 1)
                h.States.RemoveRange(h.Pos + 1, h.States.Count - h.Pos - 1);

            // Серия стрелок или набор в поле свойств — один шаг, а не двадцать.
            bool coalesce = keyboard && h.LastByKeyboard && h.Pos > 0 &&
                            (DateTime.Now - h.LastPush).TotalMilliseconds < CoalesceMs;
            if (coalesce)
                h.States[h.Pos] = cur;
            else
            {
                h.States.Add(cur);
                h.Pos++;
                if (h.States.Count > MaxSteps) { h.States.RemoveAt(0); h.Pos--; }
            }
            h.LastPush = DateTime.Now;
            h.LastByKeyboard = keyboard;
        }

        public static void DoUndo() { Step(-1); }
        public static void DoRedo() { Step(+1); }

        public static bool CanUndo
        {
            get
            {
                History h = Plus.CurrentDoc == null ? null : Get(Plus.CurrentDoc, false);
                return h != null && h.Pos > 0;
            }
        }

        public static bool CanRedo
        {
            get
            {
                History h = Plus.CurrentDoc == null ? null : Get(Plus.CurrentDoc, false);
                return h != null && h.Pos < h.States.Count - 1;
            }
        }

        static void Step(int dir)
        {
            Document doc = Plus.CurrentDoc;
            if (doc == null) return;
            pending = false;
            Capture(false);
            History h = Get(doc, true);
            h.LastByKeyboard = false;

            int target = h.Pos + dir;
            if (target < 0 || target >= h.States.Count) { SystemSounds.Beep.Play(); return; }
            h.Pos = target;
            Restore(doc, h.States[target]);
        }

        static void Restore(Document doc, byte[] data)
        {
            DrawObject[] objs = Deserialize(data);
            if (objs == null) return;

            // Окна свойств держат ссылки на старые объекты — закрываем их.
            Plus.Space.CloseVarFrm();
            doc.Items.UnselectAll();
            doc.Items.Clear();
            for (int i = 0; i < objs.Length; i++) doc.Items.Insert(i, objs[i]);
            doc.SetDirtyFlag(true);
            Plus.Space.TempDocument_SelectedObjectChanged(null, EventArgs.Empty);
            Plus.CurrentDrawArea.Refresh();
            VpPanel.MarkDirty();
        }

        static byte[] Snap(Document doc)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    new BinaryFormatter().Serialize(ms, doc.Items.ToArray());
                    return ms.ToArray();
                }
            }
            catch (Exception ex) { Plus.Log(ex); return null; }
        }

        static DrawObject[] Deserialize(byte[] data)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                    return (DrawObject[])new BinaryFormatter().Deserialize(ms);
            }
            catch (Exception ex) { Plus.Log(ex); return null; }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
