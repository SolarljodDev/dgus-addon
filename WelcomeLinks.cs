using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;

namespace DgusPlus
{
    // Ссылка «DGUS Font» на странице Welcome открывает генератор шрифтов DGUS+ вместо
    // TOOL\fontLibrary.exe. Shift+клик — по-старому, генератор DWIN.
    static class WelcomeLinks
    {
        static bool done;

        public static void Poll(Form main)
        {
            if (done || main == null) return;
            LinkLabel link = Find(main, "link_DGUS_Font") as LinkLabel;
            if (link == null) return;
            done = true;

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
                    Plus.Guard(delegate { Browser.OpenTool("generator.html"); });
            };
            new ToolTip().SetToolTip(link, "Shift+click — DWIN generator");
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
