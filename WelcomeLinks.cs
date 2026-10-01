using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;

namespace DgusPlus
{
    // Ссылки «DGUS Font» и «Gray Word Library Generator» на странице Welcome открывают генератор шрифтов DGUS+
    // (вторая — сразу в режиме «Серый, 4 бита») вместо инструментов DWIN. Shift+клик — по-старому.
    static class WelcomeLinks
    {
        static readonly Dictionary<string, string> links = new Dictionary<string, string>
        {
            { "link_DGUS_Font", "generator.html" },
            { "linkLabel15", "generator.html?mode=gray" },
        };
        static readonly List<string> done = new List<string>();

        public static void Poll(Form main)
        {
            if (main == null || done.Count == links.Count) return;
            foreach (KeyValuePair<string, string> l in links)
            {
                if (done.Contains(l.Key)) continue;
                LinkLabel link = Find(main, l.Key) as LinkLabel;
                if (link == null) continue;
                done.Add(l.Key);
                Hook(link, l.Value);
            }
        }

        static void Hook(LinkLabel link, string page)
        {
            object key = typeof(LinkLabel).GetField("EventLinkClicked", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            EventHandlerList events = (EventHandlerList)typeof(Component)
                .GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link, null);
            Delegate original = events[key];
            if (original != null) events.RemoveHandler(key, original);

            link.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                if ((Control.ModifierKeys & Keys.Shift) != 0 && original != null)
                    original.DynamicInvoke(s, e);
                else
                    Plus.Guard(delegate { Browser.OpenTool(page); });
            };
            new ToolTip().SetToolTip(link, Loc.T("Shift+click — DWIN generator"));
        }

        static Control Find(Control root, string name)
        {
            if (root.Name == name) return root;
            foreach (Control c in root.Controls)
            {
                Control r = Find(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
