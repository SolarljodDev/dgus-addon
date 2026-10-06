// Страницы инструментов написаны по-русски. Если в DGUS выбран не русский язык, этот скрипт переводит их на английский:
// текст, подсказки (title/placeholder), а также alert/confirm. Язык приходит от DGUS+ (window.DGUS_LANG) и опрашивается
// заново, поэтому смена языка в DGUS применяется и к уже открытым окнам.
(function () {
  var lang = window.DGUS_LANG || 'ru';
  var CY = /[А-Яа-яЁё]/;

  var EXACT = {
    'Генератор шрифтов': 'Font generator', 'Редактор шрифтов': 'Font editor', 'Шрифт': 'Font',
    'Файл…': 'File…', 'Установленные…': 'Installed…', 'Ячейка': 'Cell', 'Ширина': 'Width', 'Высота': 'Height',
    'Размер шрифта': 'Font size', 'База': 'Baseline', 'Сдвиг X': 'Shift X', 'Сдвиг Y': 'Shift Y', 'Тип': 'Type',
    'Чёрно-белый (1 бит)': 'Black and white (1 bit)', 'Серый (4 бита)': 'Gray (4 bit)',
    'Серый (8 бит, 256 уровней)': 'Gray (8 bit, 256 levels)', 'Порог': 'Threshold', 'Гамма': 'Gamma',
    'Пробел, px': 'Space, px', 'Насыщенность': 'Weight', 'Пробный символ': 'Test glyph', 'Диапазоны': 'Ranges',
    '+ Диапазон': '+ Range', 'Файл': 'File', 'Превью': 'Preview', 'Скачать .bin': 'Download .bin', 'Проект': 'Project',
    'Заменить шрифт': 'Replace font', 'Номер': 'ID', 'Сохранить в проект': 'Save to project',
    'Новый шрифт': 'New font', 'авто': 'auto', 'метка': 'label', 'Масштаб': 'Zoom', 'ширина ячейки': 'cell width',
    'Код или символ': 'Code or character', 'Сохранить': 'Save', 'Авто': 'Auto', 'Формат файла': 'File format',
    'Глиф': 'Glyph', 'Смещение': 'Offset', 'Код глифа 0': 'Glyph 0 code', 'Очистить': 'Clear', 'Заполнить': 'Fill',
    'Инвертировать': 'Invert', '← Назад': '← Back', 'Далее →': 'Next →', 'Шрифты проекта…': 'Project fonts…',
    'нет': 'none', 'КБ': 'KB', 'Восстановить из файла': 'Restore from the file',
    'Сначала загрузите шрифт': 'Load a font first', 'Генерация…': 'Generating…',
    'Строка внутри ячейки, считая от верхнего края вниз': 'Row inside the cell, counted from the top edge down',
    'В отличие от DWIN-тула сдвигает точку рендера, а не готовый растр — низ у глифов со спусками не обрезается':
      'Unlike the DWIN tool, this shifts the render point rather than the finished bitmap, so descenders are not cut off at the bottom',
    'Серый 4 бита — 16 уровней, кодировка Unicode, выводится элементом Text II. Серый 8 бит — 256 уровней, формат шрифта №0 DWIN: только ASCII 0x21–0x7E, один размер ячейки, ширина символов фиксированная':
      'Gray 4 bit — 16 levels, Unicode encoding, shown by a Text II element. Gray 8 bit — 256 levels, DWIN font No. 0 format: ASCII 0x21–0x7E only, one cell size, fixed character width',
    'Меньше 1 — текст плотнее, больше 1 — тоньше': 'Below 1 — denser text, above 1 — thinner',
    'Ширина пробела; пусто — треть размера шрифта': 'Space width; empty — a third of the font size',
    'Ширина пробела; пусто — ширина ячейки (как у DWIN)': 'Space width; empty — cell width (as in the DWIN tool)',
    'Ось wght вариативного шрифта: 400 — Regular, 700 — Bold': 'wght axis of a variable font: 400 — Regular, 700 — Bold',
    'Серый 8 бит — шрифт №0 DWIN: только ASCII 0x21–0x7E (94 символа). Кириллицы и других диапазонов в этом формате нет.':
      'Gray 8 bit is the DWIN font No. 0: ASCII 0x21–0x7E only (94 characters). There is no Cyrillic or other ranges in this format.',
    'Вычислить базовую линию по цифрам и заглавным': 'Work out the baseline from digits and capital letters',
    'Байт, с которого начинается глиф 0 — для дампа прошивки, где таблица шрифта лежит не с начала файла':
      'Byte where glyph 0 starts — for a firmware dump where the font table does not begin at the start of the file',
    'Unicode-код первого глифа (hex). Нужен только для подписей и поиска, в файл не пишется':
      'Unicode code of the first glyph (hex). Used only for labels and search, not written to the file',
    'Номер должен быть от 1 до 127': 'The ID must be from 1 to 127',
    'Укажите номер шрифта 1–127': 'Enter a font ID 1–127',
    'Не сохранено: шрифт не влезает, см. выше': 'Not saved: the font does not fit, see above',
    'Это серый (4-битный) шрифт — в редакторе он не открывается: сглаженные пиксели вручную не правят. Меняйте кегль, базу, гамму в генераторе и генерируйте заново.':
      'This is a gray (4-bit) font — it does not open in the editor: anti-aliased pixels are not edited by hand. Change the size, baseline and gamma in the generator and generate again.',
    'Несохранённые изменения будут потеряны. Открыть другой шрифт?': 'Unsaved changes will be lost. Open another font?',
    'В DWIN_SET нет шрифтов': 'No fonts in DWIN_SET'
  };

  // Куски динамических строк: применяются подстановкой, поэтому работают и со вставленными числами.
  var FRAG = [
    ['Глифов: ', 'Glyphs: '], [' из 94 ASCII', ' of 94 ASCII'], [' · Файл: ', ' · File: '], [' байт', ' bytes'],
    ['(заголовок 2 КБ + 94 ячейки)', '(2 KB header + 94 cells)'],
    ['(таблица Unicode 320 КБ + глифы)', '(Unicode table 320 KB + glyphs)'],
    [' · вне ASCII (', ' · outside ASCII ('], [' шт) в этот формат не попадают', ' pcs) are not in this format'],
    [' шт', ' pcs'], ['Свободно: нет', 'Free: none'], ['Свободно: ', 'Free: '],
    ['не хватает номеров: нужно до ', 'not enough IDs: need up to '], [', максимум 127', ', max 127'],
    [' зарезервирован DGUS', ' is reserved by DGUS'], [' занят файлом ', ' is taken by '],
    ['Не влезает: ', 'Does not fit: '], [' КБ = ', ' KB = '], [' бл. (', ' blocks ('], ['Влезает: ', 'Fits: '],
    [', номера ', ', IDs '],
    [' уже есть в проекте. Заменить?\nСтарая версия уйдёт в DgusPlus_backup.', ' already exists in the project. Replace?\nThe old version goes to DgusPlus_backup.'],
    ['Сохранено: ', 'Saved: '], [' · старая версия: ', ' · old version: '], ['Не сохранено: ', 'Not saved: '],
    ['8-битный шрифт: ширина до ', '8-bit font: width up to '], [', высота до ', ', height up to '], [' (у вас ', ' (yours '],
    ['Загружен: ', 'Loaded: '], [' (системный)', ' (system)'], ['⚠ возможно нет в шрифте', '⚠ possibly missing from the font'],
    [' · глифов: ', ' · glyphs: '], [' · смещение ', ' · offset '], [' · заглавные ', ' · capitals '],
    [' · ⚠ остаток ', ' · ⚠ remainder '], [' б — проверьте размер глифа и смещение', ' B — check the glyph size and offset'],
    [' · изменено: ', ' · changed: '], ['Не удалось прочитать ', 'Could not read '], [' КБ', ' KB']
  ];
  FRAG.sort(function (a, b) { return b[0].length - a[0].length; });

  function tr(s) {
    if (!CY.test(s)) return s;
    var m = /^(\s*)([\s\S]*?)(\s*)$/.exec(s);
    if (EXACT.hasOwnProperty(m[2])) return m[1] + EXACT[m[2]] + m[3];
    for (var i = 0; i < FRAG.length; i++) if (s.indexOf(FRAG[i][0]) >= 0) s = s.split(FRAG[i][0]).join(FRAG[i][1]);
    return s;
  }

  var origText = new WeakMap(), setText = new WeakMap();
  function fixText(n) {
    var cur = n.data, o = origText.get(n);
    if (o === undefined || (cur !== o && cur !== setText.get(n))) {
      if (!CY.test(cur)) { origText.delete(n); return; }   // запоминаем только русский текст
      o = cur; origText.set(n, o);
    }
    var want = lang === 'en' ? tr(o) : o;
    if (n.data !== want) { setText.set(n, want); n.data = want; }
  }

  var ATTRS = ['title', 'placeholder'];
  function fixAttr(el, a) {
    var key = '__i18n_' + a, setKey = '__i18n_set_' + a, cur = el.getAttribute(a);
    if (cur === null) return;
    var o = el[key];
    if (o === undefined || (cur !== o && cur !== el[setKey])) {
      if (!CY.test(cur)) { delete el[key]; return; }
      o = el[key] = cur;
    }
    var want = lang === 'en' ? tr(o) : o;
    if (cur !== want) { el[setKey] = want; el.setAttribute(a, want); }
  }

  function walk(root) {
    if (root.nodeType === 3) { fixText(root); return; }
    if (root.nodeType !== 1) return;
    if (root.tagName === 'SCRIPT' || root.tagName === 'STYLE') return;
    for (var i = 0; i < ATTRS.length; i++) fixAttr(root, ATTRS[i]);
    for (var c = root.firstChild; c; c = c.nextSibling) walk(c);
  }

  function start() {
    walk(document.documentElement);
    new MutationObserver(function (list) {
      list.forEach(function (m) {
        if (m.type === 'characterData') fixText(m.target);
        else if (m.type === 'attributes') fixAttr(m.target, m.attributeName);
        else m.addedNodes.forEach(walk);
      });
    }).observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ATTRS });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();

  ['alert', 'confirm'].forEach(function (name) {
    var orig = window[name];
    window[name] = function (msg) { return orig.call(window, lang === 'en' ? tr(String(msg)) : msg); };
  });

  // Смена языка в DGUS на лету.
  if (location.protocol.indexOf('http') === 0) {
    setInterval(function () {
      fetch('/api/lang').then(function (r) { return r.json(); }).then(function (j) {
        if (j && j.lang && j.lang !== lang) { lang = j.lang; walk(document.documentElement); }
      }).catch(function () {});
    }, 2000);
  }
})();
