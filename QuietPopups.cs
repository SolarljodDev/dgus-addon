using System;

namespace DgusPlus
{
    // Окна DGUS «всё получилось» (Generate, Save, Export...) закрываются сами, а текст
    // коротко показывается на вкладке DGUS+. Ошибки и вопросы остаются окнами —
    // глушим только точные тексты из Language\*.ini (String_10/11/12/21/29).
    static class QuietPopups
    {
        static DialogCatcher catcher;

        static readonly string[] Known =
        {
            "Loading successful", "Save done!",
            "Generation done，File name is： HMIConfig.bin，save under current project",
            "Config file is generated successfully!", "Export successfully！",

            "Загрузка завершена", "Сохранилась",
            "Создание завершено, файл называется： HMIConfig.bin，Разместите его в текущем инженерном каталоге",
            "Конфигурация успешно создана!", "Экспортирова законч!",

            "加载成功", "保存成功", "生成完成，文件名为： HMIConfig.bin，存放当前工程目录下",
            "配置文件生成成功!", "导出完成！",
        };

        // Вызывать в потоке интерфейса DGUS: хук ставится на текущий поток.
        public static void Install()
        {
            if (catcher != null) return;
            catcher = new DialogCatcher(IsKnown);
            catcher.Caught += delegate(string s)
            {
                catcher.Messages.Clear();
                PlusTab.Note(s);
            };
        }

        static bool IsKnown(string s)
        {
            return Array.IndexOf(Known, s) >= 0;
        }
    }
}
