using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace DgusPlus
{
    // HTTP-часть для веб-инструментов (font-editor / font-generator), которые DGUS+ отдаёт
    // со своего адреса 127.0.0.1:<порт>:
    //   GET  /fonts/                        — стартовая страница
    //   GET  /fonts/editor.html, generator.html — инструменты (встроены в exe; папка fonts\ рядом с exe их заменяет)
    //   GET  /api/project                   — файлы DWIN_SET, занятые/свободные номера, кто какой шрифт использует
    //   GET  /api/file?name=…               — прочитать файл из DWIN_SET
    //   GET  /api/job/<id>[/font], POST /api/job/<id>/result — задания генерации шрифта от MCP (FontJobs)
    //   POST /api/save?name=…&overwrite=1&replace=… — записать шрифт в DWIN_SET (старая версия → DgusPlus_backup;
    //                                       replace — старый файл шрифта с тем же номером, но другим именем)
    static class WebApi
    {
        public class Response
        {
            public int Code = 200;
            public string Type = "application/json; charset=utf-8";
            public byte[] Body;
        }

        public static Response Handle(string method, string path, string query, byte[] body)
        {
            Dictionary<string, string> q = ParseQuery(query);
            try
            {
                if (method == "GET" && (path == "/" || path == "/fonts" || path == "/fonts/"))
                    return Html(IndexPage());
                if (method == "GET" && path == "/fonts/dgus.css")
                {
                    Response css = new Response();
                    css.Type = "text/css; charset=utf-8";
                    css.Body = Css();
                    return css;
                }
                if (method == "GET" && path.StartsWith("/fonts/"))
                {
                    byte[] page = Tool(path.Substring("/fonts/".Length));
                    if (page == null) return Text(404, "Нет такой страницы.");
                    Response r = new Response();
                    r.Type = "text/html; charset=utf-8";
                    r.Body = page;
                    return r;
                }
                if (path.StartsWith("/api/job/"))
                    return FontJobs.Handle(method, path.Substring("/api/job/".Length), body);
                if (method == "GET" && path == "/api/project")
                    return Json(Plus.OnUi(delegate { return ProjectFiles.Summary(true); }));
                if (method == "GET" && path == "/api/file")
                {
                    string name = Get(q, "name");
                    if (name == null || Path.GetFileName(name) != name) return Error(400, "Недопустимое имя файла.");
                    string full = Path.Combine(ProjectFiles.DwinSet, name);
                    if (!File.Exists(full)) return Error(404, "Нет файла " + name + " в DWIN_SET.");
                    Response r = new Response();
                    r.Type = "application/octet-stream";
                    r.Body = File.ReadAllBytes(full);
                    return r;
                }
                if (method == "POST" && path == "/api/save")
                {
                    string name = Get(q, "name");
                    bool overwrite = Get(q, "overwrite") == "1";
                    string backup;
                    try { backup = ProjectFiles.Save(name, body ?? new byte[0], overwrite, Get(q, "replace")); }
                    catch (IOException ex)
                    {
                        if (ex.Message == "exists") return Error(409, "Файл " + name + " уже есть в DWIN_SET.");
                        throw;
                    }
                    Dictionary<string, object> res = new Dictionary<string, object>();
                    res["saved"] = Path.Combine(ProjectFiles.DwinSet, name);
                    res["bytes"] = body == null ? 0 : body.Length;
                    if (backup != null) res["backup"] = backup;
                    return Json(res);
                }
                return Error(404, "Нет такого адреса.");
            }
            catch (ArgumentException ex) { return Error(400, ex.Message); }
            catch (Exception ex)
            {
                Plus.Log(ex);
                return Error(500, ex.Message);
            }
        }

        // Инструменты: сначала папка fonts\ рядом с exe (для доработки без пересборки), потом ресурс exe.
        static byte[] Tool(string file)
        {
            string key = file == "editor.html" ? "editor" : file == "generator.html" ? "generator" : null;
            if (key == null) return null;
            string local = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts"), file);
            if (File.Exists(local)) return File.ReadAllBytes(local);
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fonts." + key + ".html"))
            {
                if (s == null) return null;
                byte[] b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                return b;
            }
        }

        // Общий стиль инструментов: цвета выбранной темы DGUS+ как CSS-переменные + правила из dgus.css.
        static byte[] Css()
        {
            Func<Color, string> hex = delegate(Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); };
            StringBuilder sb = new StringBuilder();
            sb.Append(":root{color-scheme:").Append(Theme.Dark ? "dark" : "light").Append(";")
              .Append("--bg:").Append(hex(Theme.Bg)).Append(";")
              .Append("--panel:").Append(hex(Theme.Panel)).Append(";")
              .Append("--panel-alt:").Append(hex(Theme.PanelAlt)).Append(";")
              .Append("--border:").Append(hex(Theme.Border)).Append(";")
              .Append("--input:").Append(hex(Theme.Input)).Append(";")
              .Append("--fg:").Append(hex(Theme.Text)).Append(";")
              .Append("--muted:").Append(hex(Theme.TextDim)).Append(";")
              .Append("--accent:").Append(hex(Theme.Accent)).Append(";")
              .Append("--group:").Append(hex(Theme.Accent)).Append(";")
              .Append("--selection:").Append(hex(Theme.Selection)).Append(";")
              .Append("--button:").Append(hex(Theme.Button)).Append(";")
              .Append("--button-hover:").Append(hex(Theme.ButtonHover)).Append(";")
              .Append("--bad:").Append(hex(Theme.Warn)).Append(";")
              .Append("--warn:").Append(hex(Theme.Shared)).Append(";")
              .Append("--dirty:").Append(hex(Theme.Shared)).Append(";")
              .Append("--pixel-on:").Append(hex(Theme.Text)).Append(";")
              .Append("--pixel-off:").Append(hex(Theme.Input)).Append(";")
              .Append("--grid-line:rgba(128,128,128,0.18);}\n");
            string local = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts"), "dgus.css");
            if (File.Exists(local)) sb.Append(File.ReadAllText(local, Encoding.UTF8));
            else
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fonts.css"))
                    if (s != null) using (StreamReader r = new StreamReader(s, Encoding.UTF8)) sb.Append(r.ReadToEnd());
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        static string IndexPage()
        {
            return "<!DOCTYPE html><html lang='ru'><head><meta charset='utf-8'><title>DGUS+ — шрифты</title>" +
                   "<style>body{background:#1a1a1a;color:#ddd;font:14px Consolas,monospace;padding:40px}" +
                   "a{display:block;margin:12px 0;color:#4af;font-size:16px}</style></head><body>" +
                   "<h2>DGUS+ — шрифты</h2>" +
                   "<a href='/fonts/generator.html'>Генератор шрифтов (TTF/OTF → .bin)</a>" +
                   "<a href='/fonts/editor.html'>Редактор шрифтов (.bin)</a></body></html>";
        }


        static Response Html(string s)
        {
            Response r = new Response();
            r.Type = "text/html; charset=utf-8";
            r.Body = Encoding.UTF8.GetBytes(s);
            return r;
        }

        static Response Text(int code, string s)
        {
            Response r = new Response();
            r.Code = code;
            r.Type = "text/plain; charset=utf-8";
            r.Body = Encoding.UTF8.GetBytes(s);
            return r;
        }

        public static Response Json(object o)
        {
            Response r = new Response();
            r.Body = Encoding.UTF8.GetBytes(McpServer.Json.Serialize(o));
            return r;
        }

        public static Response Error(int code, string message)
        {
            Dictionary<string, object> e = new Dictionary<string, object>();
            e["error"] = message;
            Response r = Json(e);
            r.Code = code;
            return r;
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string part in (query ?? "").Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string k = Uri.UnescapeDataString((eq < 0 ? part : part.Substring(0, eq)).Replace('+', ' '));
                string v = eq < 0 ? "" : Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
                d[k] = v;
            }
            return d;
        }

        static string Get(Dictionary<string, string> q, string k)
        {
            string v;
            return q.TryGetValue(k, out v) ? v : null;
        }
    }
}
