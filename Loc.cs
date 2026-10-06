using System;
using System.Collections.Generic;

namespace DgusPlus
{
    // Тексты надстройки: английский — исходный, русский подставляется, когда в DGUS выбран «Русский».
    static class Loc
    {
        public static bool Russian;
        public static event EventHandler Changed;

        static readonly List<KeyValuePair<Action<string>, string>> bound = new List<KeyValuePair<Action<string>, string>>();

        static readonly Dictionary<string, string> ru = new Dictionary<string, string>
        {
            { "↶  Undo", "↶  Отменить" }, { "↷  Redo", "↷  Повторить" }, { "Fit", "Вписать" }, { "VP panel", "Панель VP" },
            { "Font generator", "Генератор шрифтов" }, { "Font editor", "Редактор шрифтов" },
            { "Upload to display", "Залить в дисплей" }, { "Over UART, no SD card", "По UART, без SD-карты" },
            { "First click only selects", "Первый клик только выделяет" }, { "Wheel = zoom", "Колесо = масштаб" },
            { "VP in canvas labels", "VP в подписях на холсте" }, { "Center page", "Страница по центру" }, { "Show Export/Import", "Показывать Экспорт/Импорт" },
            { "fonts", "шрифты" }, { "Font generator (TTF/OTF → .bin)", "Генератор шрифтов (TTF/OTF → .bin)" },
            { "Font editor (.bin)", "Редактор шрифтов (.bin)" },
            { "Writes the list of controls to TouchConfig.xls and DisplayConfig.xls in the project folder",
              "Выгружает список элементов в TouchConfig.xls и DisplayConfig.xls в папке проекта" },
            { "Rebuilds ALL controls on all pages from TouchConfig.xls and DisplayConfig.xls (you pick the folder); existing controls are erased",
              "Пересоздаёт ВСЕ элементы на всех страницах из TouchConfig.xls и DisplayConfig.xls (папку выбираете сами); существующие элементы стираются" },
            { "Theme:", "Тема:" }, { "Dark", "Тёмная" }, { "Light", "Светлая" }, { "Original", "Оригинальная" },
            { "The theme will be fully applied after restarting DGUS.", "Тема полностью применится после перезапуска DGUS." },
            { "MCP: ", "MCP: " },

            { "Align", "Выровнять" }, { "Left", "По левому краю" }, { "Center", "По центру" }, { "Right", "По правому краю" },
            { "Top", "По верхнему краю" }, { "Middle", "По середине" }, { "Bottom", "По нижнему краю" },
            { "Distribute", "Распределить" }, { "Horizontally", "По горизонтали" }, { "Vertically", "По вертикали" },
            { "Size", "Размер" }, { "Same width", "Одинаковая ширина" }, { "Same height", "Одинаковая высота" },
            { "Same size", "Одинаковый размер" },
            { "The reference is the element you right-clicked", "Эталон — элемент, на котором нажата правая кнопка" },

            { "Open a project first.", "Сначала откройте проект." }, { "Port", "Порт" }, { "Baud rate", "Скорость" },
            { "Select all", "Выбрать все" }, { "Upload", "Залить" }, { "Stop", "Стоп" }, { "Close", "Закрыть" },
            { "Tick the files to upload.", "Отметьте файлы для заливки." },
            { "An upload is running. Stop it?", "Заливка идёт. Остановить?" },

            { "Filter: address, type or name", "Фильтр: адрес, тип или имя" }, { "Type", "Тип" }, { "Name", "Имя" },
            { "Overlaps with: ", "Пересекается с: " }, { "Shares address with: ", "Общий адрес с: " },
            { "Elements with VP: ", "Элементов с VP: " }, { "   ⚠ overlaps: ", "   ⚠ пересечений: " },

            { "Shift+click — DWIN generator", "Shift+клик — генератор DWIN" },
            { "The built-in DGUS+ server is disabled (McpEnabled=False in DgusPlus.ini).",
              "Встроенный сервер DGUS+ выключен (McpEnabled=False в DgusPlus.ini)." },
        };

        public static string T(string en)
        {
            string r;
            return Russian && ru.TryGetValue(en, out r) ? r : en;
        }

        // Выставляет текст сейчас и заново при каждой смене языка.
        public static void Bind(Action<string> set, string en)
        {
            set(T(en));
            bound.Add(new KeyValuePair<Action<string>, string>(set, en));
        }

        public static void SetRussian(bool value)
        {
            if (Russian == value) return;
            Russian = value;
            foreach (KeyValuePair<Action<string>, string> b in bound.ToArray())
                Plus.Guard(delegate { b.Key(T(b.Value)); });
            if (Changed != null) Plus.Guard(delegate { Changed(null, EventArgs.Empty); });
        }
    }
}
