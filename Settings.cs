using System;
using System.Collections.Generic;
using System.IO;

namespace DgusPlus
{
    // Настройки надстройки: DgusPlus.ini рядом с exe, формат key=value.
    class Settings
    {
        public bool ClickToSelect = true;   // первый клик только выделяет, тащить можно уже выделенное
        public bool WheelZoom = true;       // колесо = зум, Ctrl+колесо = вертикальная прокрутка
        public bool ShowVpLabels = true;    // ", 5000" после подписи элемента на холсте
        public bool CenterPage = true;      // страница по центру рабочей области
        public string Theme = "dark";       // dark | light | original
        public bool McpEnabled = true;      // MCP-сервер для Claude Code на 127.0.0.1
        public int McpPort = 8765;
        public string DisplayPort = "";
        public int DisplayBaud = 115200;

        static string PathName
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DgusPlus.ini"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(PathName)) return s;
                Dictionary<string, string> kv = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(PathName))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                s.ClickToSelect = Bool(kv, "ClickToSelect", s.ClickToSelect);
                s.WheelZoom = Bool(kv, "WheelZoom", s.WheelZoom);
                s.ShowVpLabels = Bool(kv, "ShowVpLabels", s.ShowVpLabels);
                s.CenterPage = Bool(kv, "CenterPage", s.CenterPage);
                if (kv.ContainsKey("Theme")) s.Theme = kv["Theme"];
                s.McpEnabled = Bool(kv, "McpEnabled", s.McpEnabled);
                int port;
                if (kv.ContainsKey("McpPort") && int.TryParse(kv["McpPort"], out port)) s.McpPort = port;
                if (kv.ContainsKey("DisplayPort")) s.DisplayPort = kv["DisplayPort"];
                int baud;
                if (kv.ContainsKey("DisplayBaud") && int.TryParse(kv["DisplayBaud"], out baud)) s.DisplayBaud = baud;
            }
            catch (Exception ex) { Plus.Log(ex); }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(PathName, new string[] {
                    "ClickToSelect=" + ClickToSelect,
                    "WheelZoom=" + WheelZoom,
                    "ShowVpLabels=" + ShowVpLabels,
                    "CenterPage=" + CenterPage,
                    "Theme=" + Theme,
                    "McpEnabled=" + McpEnabled,
                    "McpPort=" + McpPort,
                    "DisplayPort=" + DisplayPort,
                    "DisplayBaud=" + DisplayBaud,
                });
            }
            catch (Exception ex) { Plus.Log(ex); }
        }

        static bool Bool(Dictionary<string, string> kv, string key, bool dflt)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) return dflt;
            bool b;
            return bool.TryParse(v, out b) ? b : dflt;
        }
    }
}
