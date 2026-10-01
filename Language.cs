using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DgusPlus
{
    // Русский язык интерфейса DGUS: свой Russian.ini (встроен в exe) и пункт «Русский» в списке языков.
    // DGUS знает только два пункта списка, а при Lang.ini=Russian падает на старте, поэтому на время
    // запуска выбранный русский прячется за English и возвращается, когда окно уже создано.
    static class Language
    {
        const string Marker = "//DGUS+ Russian translation";
        static bool restoreRussian;

        [DllImport("kernel32")]
        static extern long WritePrivateProfileString(string section, string key, string value, string file);

        static string Dir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Language"); } }

        public static void Prepare()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                InstallFile();
                string lang = Path.Combine(Dir, "Lang.ini");
                if (File.Exists(lang) && Regex.IsMatch(File.ReadAllText(lang), @"language\s*=\s*Russian", RegexOptions.IgnoreCase))
                {
                    restoreRussian = true;
                    WritePrivateProfileString("Language", "Language", "English", lang);
                }
            }
            catch (Exception ex) { Plus.Log(ex); }
        }

        static void InstallFile()
        {
            byte[] ours;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang.russian.ini"))
            {
                if (s == null) return;
                ours = new byte[s.Length];
                s.Read(ours, 0, ours.Length);
            }
            string target = Path.Combine(Dir, "Russian.ini");
            if (File.Exists(target))
            {
                byte[] cur = File.ReadAllBytes(target);
                if (Same(cur, ours)) return;
                string backup = Path.Combine(Dir, "Russian.dwin.ini");
                if (!File.Exists(backup) && !System.Text.Encoding.Unicode.GetString(cur).Contains(Marker))
                    File.Copy(target, backup);
            }
            File.WriteAllBytes(target, ours);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Выбор пункта запускает штатный обработчик DGUS: он пишет Lang.ini и сразу перерисовывает тексты.
        public static void Attach(Form main)
        {
            ComboBox cb = R.Get(main, "comboBox5") as ComboBox;
            if (cb == null || cb.Items.Count != 2) return;
            cb.Items.Add("Русский");
            if (restoreRussian)
            {
                restoreRussian = false;
                cb.SelectedIndex = 2;
            }
        }
    }
}
