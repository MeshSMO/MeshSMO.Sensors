# CODESTYLE.md

Свод правил оформления кода, которые **реально проверяет сборка**. Источники истины: [.editorconfig](./.editorconfig) и [Directory.Build.props](./Directory.Build.props) — если этот файл с ними расходится, правы они.

## Режим сборки (все C#-проекты, кроме esproj)

- **`TreatWarningsAsErrors=true`** — любой warning (компилятор, CA/MA/RCS-анализатор, IDE-стиль) валит сборку. «Оставить варнинг на потом» не выйдет.
- **`AnalysisLevel=latest-Recommended`** + анализаторы **Meziantou.Analyzer** (MA\*) и **Roslynator.Analyzers** (RCS\*) во всех проектах.
- **`EnforceCodeStyleInBuild=true`** — стилевые правила .editorconfig проверяются при билде, а не только в IDE.
- `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.

Практика: после любых правок запускай `dotnet build MeshSMO.Sensors.slnx` — сборка и есть линтер. Варнинги чинить в коде; глушить диагностику (`#pragma`, suppression, понижение severity в .editorconfig) — только осознанно, рядом с уже существующим списком исключений и с обоснованием, не молча. Для автофикса стиля можно `dotnet format MeshSMO.Sensors.slnx --severity warn`.

## Что ломает сборку (severity error/warning)

- **`var` везде** для локальных переменных: `var count = 10;`, `var client = new HttpClient();` (IDE0007 = error). Явный тип — только когда `var` не может вывести тип (`Sensor s = null;`, касты). **Target-typed `new` не использовать**: не `Foo foo = new();`, а `var foo = new Foo();` (implicit object creation выключен).
- **File-scoped namespaces**: `namespace Foo.Bar;`.
- **Usings вне namespace**, `System.*` первыми, группы usings не разделять пустой строкой.
- **Expression-bodied members** везде, где можно выразить телом-выражением: методы, конструкторы, операторы, свойства, аксессоры, лямбды — `public string Name => _name;`, а не блок с `return`.
- **Современный синтаксис**: `using var x = ...`; `static` у локальных функций/лямбд без захватов; switch-выражения вместо switch-стейтментов; pattern matching (`x is null`, `x is not Foo f`) вместо `as` + null-check и `is` + cast; throw-выражения (`Name => _name ?? throw ...`); inline out (`int.TryParse(s, out var v)`); `?.` и `??` вместо тернарников с null.
- **Инициализаторы**: object/collection initializers вместо присваивания полей после конструктора.
- **`readonly`** у private-полей, где возможно.
- **Упрощения**: `if (flag)` вместо `if (flag == true)`, без лишнего `ToString()`/`AppendLiteral` в интерполяции.
- **Без `this.`** — `Foo()`, не `this.Foo()`.
- **Скобки**: открытая скобка всегда с новой строки (Allman), `else`/`catch`/`finally` — с новой строки. Правило `when_multiline`: одиночный оператор можно без скобок (на отдельной строке), многострочный — только в скобках. `if (x) Execute();` в одну строку — запрещено. Лишние скобки вокруг одиночного оператора — RCS1002/1003/1004 warning → убрать (т.е. вокруг одиночного оператора скобки **не нужны**).

## Не валит сборку, но учтено (suggestion/silent)

- **CA1848 silent** — LoggerMessage-делегаты не обязательны, обычные `_logger.LogInformation(...)` ок.
- **CA1305 suggestion** — предупреждения про `IFormatProvider`/`CultureInfo` видны, но не блокируют.
- **MA0004 suggestion** — `ConfigureAwait(false)` желателен, не принудителен.
- **CA1707 none** — подчёркивания в идентификаторах (тесты `Method_UnderCondition_Expectation`) разрешены.
- По вкусу (suggestion): primary constructors, extended property pattern, деконструкция, `^1`/ranges, составные присваивания (`x += y`).
- MA0048 (имя файла = имя типа), MA0051 (слишком длинный метод), CA1859 — suggestion.

## Формат файлов

- **LF** (не CRLF), UTF-8, файл кончается переводом строки, без трейлинг-пробелов (кроме `.md` — там двойной пробел в конце строки = явный `<br>`, его не стрипать).
- Отступы: C# — 4 пробела; csproj/props/targets/xml/json/yaml/ts/tsx/js/css/html/md — 2 пробела. Табы нигде.

## Фронтенд (`src/web`)

Канонический форматтер — **Prettier** (`src/web/.prettierrc`): printWidth 100, semi, двойные кавычки, trailing commas, `endOfLine: lf`. `.editorconfig` задаёт только универсальное (LF, отступы 2, `max_line_length = 100` для JS/TS — для линейки редактора).

Единый пайплайн (все способы форматирования дают одинаковый результат):

- `npm run format` — переписать, `npm run format:check` — проверить (шаг в CI, job `frontend`).
- `npm run lint` — ESLint c `eslint-plugin-prettier`: расхождение с Prettier = error.
- ts/tsx руками не форматировать: править код и запускать `npm run format`. Ручное форматирование «по .editorconfig» проверку не проходит — Prettier ведёт переносы/кавычки, которые .editorconfig не выражает.
- EOL = LF на трёх уровнях: `.gitattributes` (`* text=auto eol=lf`, источник истины для git), `.prettierrc` (`endOfLine`), `.vscode/settings.json` (`files.eol`, format on save, Prettier как форматтер веб-файлов — настройки коммитятся).
- `src/generated/` (вывод `scripts/generate-registry.mjs`) — в `.prettierignore`: формат генерированного JSON принадлежит генератору, Prettier его не трогает и не проверяет.
- `src/components/ui/**` (shadcn) — исключение из `react-refresh/only-export-components`, это генерируемый код.

## Если анализатор мешает легитимному коду

Порядок действий: переписать код; если реально мешает — точечный `[SuppressMessage]`/`#pragma` с комментарием-обоснованием; в крайнем случае понижение severity в `.editorconfig` (рядом с существующим списком исключений) — и предупредить пользователя.
