using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DgusPlus
{
    // Веб-инструмент в окне Edge/Chrome без адресной строки; без Chromium — в браузере по умолчанию.
    static class Browser
    {
        public static void OpenTool(string page)
        {
            if (McpServer.Port == 0)
            {
                MessageBox.Show("The built-in DGUS+ server is disabled (McpEnabled=False in DgusPlus.ini).", "DGUS+");
                return;
            }
            string url = "http://127.0.0.1:" + McpServer.Port + "/fonts/" + page;
            string exe = FindChromium();
            try
            {
                if (exe != null)
                {
                    // Отдельный профиль: окно не смешивается с обычными вкладками и помнит свой размер.
                    string profile = Path.Combine(Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData), "DgusPlus"), "browser");
                    Process.Start(exe, "--app=" + url + " --user-data-dir=\"" + profile + "\" --window-size=1500,950");
                }
                else Process.Start(url);
            }
            catch (Exception ex)
            {
                Plus.Log(ex);
                Process.Start(url);
            }
        }

        public static string FindChromium() { return Find("msedge.exe") ?? Find("chrome.exe"); }

        static string Find(string exe)
        {
            foreach (RegistryKey root in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using (RegistryKey k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                {
                    string p = k == null ? null : k.GetValue("") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p.Trim('"'))) return p.Trim('"');
                }
            }
            return null;
        }
    }
}
