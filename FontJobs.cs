using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DgusPlus
{
    // Генерация шрифта по запросу MCP: font-generator запускается в Edge/Chrome без окна (/fonts/generator.html#job=<id>),
    // берёт параметры и файл через /api/job/<id>, растрирует тем же кодом, что и вручную, и присылает результат на /api/job/<id>/result.
    static class FontJobs
    {
        class Job
        {
            public Dictionary<string, object> Params;
            public byte[] Font;
            public Dictionary<string, object> Result;
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
        }

        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();

        public static Dictionary<string, object> Run(Dictionary<string, object> prms, byte[] font, int timeoutSec)
        {
            if (McpServer.Port == 0) throw new ArgumentException("The built-in DGUS+ server is disabled.");
            string exe = Browser.FindChromium();
            if (exe == null) throw new ArgumentException("Microsoft Edge or Google Chrome is required — the generator runs in it.");

            string id = Guid.NewGuid().ToString("N").Substring(0, 12);
            Job job = new Job();
            job.Params = prms;
            job.Font = font;
            lock (jobs) jobs[id] = job;

            // Свой профиль на каждое задание: Edge с тем же профилем, ещё не успевший закрыться после
            // прошлого задания, перехватил бы запуск, и новое задание повисло бы.
            string root = Path.Combine(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "DgusPlus"), "headless");
            string profile = Path.Combine(root, id);
            string url = "http://127.0.0.1:" + McpServer.Port + "/fonts/generator.html#job=" + id;
            Process p = null;
            try
            {
                p = Process.Start(exe, "--headless=new --disable-gpu --no-first-run --disable-extensions " +
                                       "--user-data-dir=\"" + profile + "\" \"" + url + "\"");
                if (!job.Done.WaitOne(timeoutSec * 1000, false))
                    throw new TimeoutException("The generator did not respond within " + timeoutSec + " s.");
                return job.Result;
            }
            finally
            {
                lock (jobs) jobs.Remove(id);
                if (p != null)
                    try { Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false }).WaitForExit(5000); }
                    catch { }
                CleanProfiles(root);
            }
        }

        // Удаляем профили прошлых заданий (занятые ещё закрывающимся Edge — в следующий раз).
        static void CleanProfiles(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (string d in Directory.GetDirectories(root))
                try { Directory.Delete(d, true); } catch { }
        }


        public static WebApi.Response Handle(string method, string rest, byte[] body)
        {
            string[] parts = rest.Split('/');
            Job job;
            lock (jobs) jobs.TryGetValue(parts[0], out job);
            if (job == null) return WebApi.Error(404, "No such job.");

            if (method == "GET" && parts.Length == 1) return WebApi.Json(job.Params);
            if (method == "GET" && parts.Length == 2 && parts[1] == "font")
            {
                WebApi.Response r = new WebApi.Response();
                r.Type = "application/octet-stream";
                r.Body = job.Font;
                return r;
            }
            if (method == "POST" && parts.Length == 2 && parts[1] == "result")
            {
                job.Result = McpServer.Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body ?? new byte[0]));
                job.Done.Set();
                return WebApi.Json("ok");
            }
            return WebApi.Error(404, "No such address.");
        }


        public class SystemFont { public string Name, File; }

        public static List<SystemFont> Installed()
        {
            List<SystemFont> res = new List<SystemFont>();
            string winFonts = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "Fonts");
            foreach (RegistryKey root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using (RegistryKey k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
                {
                    if (k == null) continue;
                    foreach (string name in k.GetValueNames())
                    {
                        string file = k.GetValue(name) as string;
                        if (string.IsNullOrEmpty(file)) continue;
                        if (!Path.IsPathRooted(file)) file = Path.Combine(winFonts, file);
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".ttf" && ext != ".otf" && ext != ".ttc" || !File.Exists(file)) continue;
                        SystemFont f = new SystemFont();
                        f.Name = name.Replace("(TrueType)", "").Replace("(OpenType)", "").Trim();
                        f.File = file;
                        res.Add(f);
                    }
                }
            }
            res.Sort(delegate(SystemFont a, SystemFont b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return res;
        }

        static string Norm(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // Путь к файлу или имя установленного шрифта («Inter Bold», «Roboto»).
        public static string Resolve(string query)
        {
            if (string.IsNullOrEmpty(query)) throw new ArgumentException("Specify a font: a file path or an installed font name.");
            if (File.Exists(query)) return query;
            List<SystemFont> all = Installed();
            string q = Norm(query);
            foreach (SystemFont f in all) if (Norm(f.Name) == q) return f.File;
            foreach (SystemFont f in all) if (Norm(f.Name) == q + "regular") return f.File;

            string[] words = query.ToLowerInvariant().Split(new char[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            List<SystemFont> hits = new List<SystemFont>();
            foreach (SystemFont f in all)
            {
                string n = f.Name.ToLowerInvariant();
                bool ok = true;
                foreach (string w in words) if (n.IndexOf(w) < 0) { ok = false; break; }
                if (ok) hits.Add(f);
            }
            if (hits.Count == 1) return hits[0].File;
            if (hits.Count == 0) throw new ArgumentException("Font '" + query + "' not found. See list_system_fonts, or give a file path.");
            StringBuilder sb = new StringBuilder("Be more specific about the font '" + query + "', candidates: ");
            for (int i = 0; i < hits.Count && i < 15; i++) sb.Append(i > 0 ? "; " : "").Append(hits[i].Name);
            if (hits.Count > 15) sb.Append("; … more: ").Append(hits.Count - 15);
            throw new ArgumentException(sb.ToString());
        }
    }
}
