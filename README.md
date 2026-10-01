<p align="center"><img src="assets/icon.png" width="96" alt="DGUS+"></p>

# DGUS Addon — add-on and MCP server for DWIN DGUS Tool

**RU** · [EN](#english)

Надстройка **DGUS+** над **DWIN DGUS Tool V7.650** (дисплеи T5L) и **MCP-сервер**, через который Claude Code редактирует открытый проект.

- отмена/повтор (Ctrl+Z / Ctrl+Y), зум колесом, таблица VP, тёмная и светлая темы;
- **Alt при перетаскивании** — магнит к краям и центрам других элементов и страницы, направляющие и размерные линии до соседей и краёв страницы;
- **ПКМ по выделенным** — Выровнять / Распределить / Одинаковая ширина, высота, размер (как в PowerPoint, без «кликни эталон»);
- **Ctrl+S** — сохранить проект; координаты курсора — в углу рабочей области;
- **русский язык интерфейса DGUS** — пункт «Русский» в списке языков (Настройки → Language);
- генератор и редактор шрифтов, заливка в дисплей по UART.

> Неофициальный проект, не связан с Beijing DWIN Technology. Файлы DWIN не изменяются и не распространяются. Единственное
> исключение — русский перевод: `Language\Russian.ini` (свой, собственный перевод, встроен в exe) кладётся в папку DGUS;
> прежний файл, если он был, сохраняется как `Russian.dwin.ini`.

**Установка:** положите `DgusPlus.exe` из [Releases](../../releases) рядом с `DGUS_V7.650.exe` и запускайте его вместо DGUS.

**Подключение MCP (один раз):**

    claude mcp add --transport http --scope user dgus http://127.0.0.1:8765/mcp

**Заливка через свой контроллер:** если дисплей подключён к контроллеру, его прошивка может работать мостом: по первому
кадру DWIN (`5A A5 …`) от ПК пересылать байты в обе стороны как есть до нескольких секунд тишины. Если первый кадр
потерялся, DGUS+ повторяет его.

**Сборка:** `powershell -File build.ps1 -Target C:\DGUS_V7650` (`csc.exe` из .NET Framework 3.5).

---

<a name="english"></a>
## English

**DGUS+**, an add-on for **DWIN DGUS Tool V7.650** (T5L displays), and an **MCP server** that lets Claude Code edit the open project.

- undo/redo (Ctrl+Z / Ctrl+Y), wheel zoom, VP table, dark and light themes;
- **Alt while dragging** — snap to edges and centers of other elements and the page, with guides and distance lines to neighbors and page edges;
- **right-click on a selection** — Align / Distribute / Same width, height, size (PowerPoint-style, no "click the reference" step);
- **Ctrl+S** saves the project; cursor coordinates are shown in the corner of the workspace;
- **Russian DGUS interface** — a "Русский" entry in the language list (Setting → Language);
- font generator and editor, upload to the display over UART.

> Unofficial, not affiliated with Beijing DWIN Technology. No DWIN files are modified or redistributed. The only exception is
> the Russian translation: `Language\Russian.ini` (our own translation, embedded in the exe) is placed into the DGUS folder;
> a previous file, if any, is kept as `Russian.dwin.ini`.

**Install:** put `DgusPlus.exe` from [Releases](../../releases) next to `DGUS_V7.650.exe` and run it instead of DGUS.

**Connect MCP (once):**

    claude mcp add --transport http --scope user dgus http://127.0.0.1:8765/mcp

**Upload through your own controller:** if the display sits behind a controller, its firmware can act as a bridge:
on the first DWIN frame (`5A A5 …`) from the PC, pass bytes both ways unchanged until a few seconds of silence.
DGUS+ resends the first frame if it is lost.

**Build:** `powershell -File build.ps1 -Target C:\DGUS_V7650` (.NET Framework 3.5 `csc.exe`).

## License

MIT — see [LICENSE](LICENSE).
