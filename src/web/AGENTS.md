# AGENTS.md — фронтенд (`src/web`)

Инструкция для ИИ-агентов, работающих с фронтендом MeshSMO Sensors. Корневой [AGENTS.md](../../AGENTS.md) — первичен; здесь только специфика `src/web`.

## Что это

TanStack Start в **SPA-режиме** (`spa.enabled` в `vite.config.ts`): без SSR-рантайма; билд статически пререндерит индексируемые маршруты в `.output/public`, остальное — SPA-фолбэк. Прод-выход раздаёт ASP.NET Core BFF как `wwwroot`; **Node.js в production отсутствует**. Обёртка `@lovable.dev/vite-tanstack-config` подключает общие vite/tanstack-плагины — не дублируй их в `vite.config.ts` вручную.

## Стек

React 19 + TypeScript (strict, `verbatimModuleSyntax`), TanStack Start/Router + TanStack Query v5, Vite 8 (rolldown), Tailwind CSS v4 + Radix/shadcn (`src/components/ui`), recharts (графики), leaflet (карта `/map`), i18next (русская локализация, `src/i18n`), zod + react-hook-form. Пакетный менеджер — **npm** (`package-lock.json`); `bun.lock`/`bunfig.toml` — наследие Lovable-скаффолда, CI и сборка их не используют.

## Команды

```bash
npm ci                 # установка (CI делает именно так)
npm run dev            # dev-сервер 127.0.0.1:5173, прокси /api и /health → BFF :5200
npm run build          # vite build + scripts/postbuild.mjs (нужен prebuild-артефакт)
npm run typecheck      # pretypecheck генерирует реестр, затем tsc --noEmit
npm run lint           # ESLint 9 (включая Prettier как правило)
npm run format         # Prettier — единственный способ форматирования
```

При `dotnet build` решения фронт собирается сам через esproj (`npm run build:dotnet` = typecheck + build). `dotnet publish` идёт с `/p:SkipFrontendBuild=true` — npm в контейнере не запускается.

## Инварианты (нарушать нельзя)

1. **Пререндер без сети.** Сборка не делает HTTP-запросов и не требует запущенный БД/BFF. Живые данные — только после гидрации через TanStack Query. В пререндерed HTML — детерминированные плейсхолдеры («—», скелетоны), никакого «loading»-текста и `Date.now()`/`Math.random()` (гидрация без расхождений).
2. **Пререндер-набор = индексируемые маршруты.** Список задаёт `prerenderPaths` в `vite.config.ts` из публичной проекции реестра `src/generated/sensorRegistry.json` (генерируется `scripts/generate-registry.mjs` на prebuild/pretypecheck из `config/sensors/*.yaml`; каталог gitignored). Фильтрация после попадания в клиентский бандл защитой не считается.
3. **Два источника контента.** Статика (название, описание, локация, метрики) — из реестра через `src/lib/registry.ts` (попадает в пререндерed HTML для SEO); живые данные (status/latest/measurements/forecast) — только по API после гидрации. API-слой: `src/lib/api/` (client, queries, types, ranges).
4. **SEO-контракт BFF** (корневой AGENTS.md, грабля 4): canonical/og:url/JSON-LD — со слэшем на конце; `scripts/postbuild.mjs` копирует `index.html` в `__spa-fallback.html` и **вычищает из него canonical** (фолбэк отдаётся под чужими URL). Неизвестные URL вне `/sensors/*` — HTTP 404 от BFF, не эмулировать редиректами.
5. **Форматирование — только `npm run format`** (Prettier: printWidth 100, LF, trailing commas). Ручное форматирование «по .editorconfig» линт не пройдёт; `format:check` — шаг CI.
6. URL-структура (`/`, `/sensors`, `/sensors/:slug/`, `/map`, `/about`), порт/прокси Vite, `public/robots.txt` — не менять. BFF-контракты (`/api/v1/*`) менять аккуратно и докладывать.
7. Копирайт-правило: в UI без «AI/ИИ» — только «ML-модели» / «ML.NET».

## Структура

| Путь                   | Что                                                                                                                            |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `src/routes/`          | файловые маршруты TanStack Router (`index`, `sensors.index`, `sensors.$slug`, `map`, `about`); `routeTree.gen.ts` генерируется |
| `src/features/`        | фичи: `home`, `sensors`, `sensor-detail` (history/, SensorPage), `map`, `about`, `app`                                         |
| `src/components/site/` | доменные компоненты (MetricChart, CombinedChart, SensorMap, StatusBadge, Shell…)                                               |
| `src/components/ui/`   | shadcn/Radix-примитивы                                                                                                         |
| `src/lib/`             | api-клиент и query-хуки, registry, seo, metrics, format, favorite-* (localStorage)                                             |
| `src/i18n/`            | русская локализация (i18next)                                                                                                  |
| `scripts/`             | `generate-registry.mjs` (prebuild), `postbuild.mjs` (SPA-фолбэк)                                                               |

Исторический промпт редизайна (Lovable/TanStack Start, контракты API в нём актуальны): [docs/reference/frontend-redesign-prompt.md](../../docs/reference/frontend-redesign-prompt.md).
