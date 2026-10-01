using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace DgusPlus
{
    // Запускает оригинальный DGUS в этом же процессе с подключённой надстройкой; файлы DWIN не меняются (DgusPlus.exe лежит рядом с DGUS_V7.650.exe).
    static class Launcher
    {
        const string DgusExe = "DGUS_V7.650.exe";

        [STAThread]
        static void Main(string[] args)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string exe = Path.Combine(dir, DgusExe);
            if (!File.Exists(exe))
            {
                MessageBox.Show(
                    "Put DgusPlus.exe into the DWIN DGUS V7.650 folder (next to " + DgusExe + ") and run it from there.\n\n" +
                    "Now: " + dir,
                    "DGUS+", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Run(exe, args);
        }

        // Отдельный метод: типы DGUS (DwinTerminal.dll) подгружаются только здесь,
        // после проверки, что мы действительно в папке DGUS.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run(string exe, string[] args)
        {
            Assembly dgus = Assembly.LoadFrom(exe);
            MethodInfo main = dgus.GetType("BizDrawClient.Program")
                .GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Plus.Install();
            try
            {
                main.Invoke(null, new object[] { args });
            }
            catch (TargetInvocationException ex)
            {
                Plus.Log(ex.InnerException ?? ex);
                throw;
            }
        }
    }
}
