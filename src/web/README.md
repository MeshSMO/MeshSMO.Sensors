# MeshSMO Sensors — фронтенд

Публичный фронтенд телеметрии LoRa-датчиков ([sensors.meshsmo.ru](https://sensors.meshsmo.ru/)): живой дашборд, страницы датчиков с графиками и прогнозом (ML.NET), карта нод. В production Node.js отсутствует: собранная статика раздаётся ASP.NET Core BFF (`src/MeshSMO.Sensors.Web`) как `wwwroot`.

## Стек

- **TanStack Start** (SPA-режим, без SSR-рантайма) + TanStack Router + TanStack Query v5, React 19, TypeScript strict
- **Vite 8**, обёртка `@lovable.dev/vite-tanstack-config` (общие плагины — не дублировать в `vite.config.ts`)
- **Tailwind CSS v4** + Radix/shadcn-примитивы, recharts, leaflet, i18next (русский UI)
- Пакетный менеджер — **npm**; `bun.lock`/`bunfig.toml` — наследие Lovable-скаффолда, не используются

## Маршруты

| Путь               | Страница                                                                                                           |
| ------------------ | ------------------------------------------------------------------------------------------------------------------ |
| `/`                | лендинг + живой дашборд (счётчики статусов, карточки датчиков, проценты батарей)                                   |
| `/sensors`         | список датчиков                                                                                                    |
| `/sensors/{slug}/` | страница датчика: показания, графики истории (recharts, min/avg/max, диапазоны в URL), прогноз, диагностика канала |
| `/map`             | интерактивная карта нод (leaflet)                                                                                  |
| `/about`           | о проекте                                                                                                          |

## Запуск

Полный dev-цикл — одной командой из корня репозитория (SPA-proxy сам поднимет Vite):

```bash
dotnet run --project src/MeshSMO.Sensors.Web --launch-profile http
# BFF → http://localhost:5200, фронт → http://localhost:5173
```

Только фронтенд:

```bash
npm ci
npm run dev        # http://127.0.0.1:5173, прокси /api и /health → :5200
```

Отдельные проверки: `npm run lint`, `npm run format:check`, `npm run typecheck`, `npm run build`. CI гоняет все четыре + `dotnet test`.

## Конвейер сборки

1. **prebuild**: `scripts/generate-registry.mjs` читает deployment-local YAML реестра (`config/sensors/*.yaml`, gitignored) и пишет публичную проекцию `src/generated/sensorRegistry.json` (только `public.visible: true`; без локальных YAML — сохраняет закоммиченный снапшот).
2. **vite build**: пререндер статического HTML для `prerenderPaths` (`vite.config.ts`: `/`, `/sensors`, `/map`, `/about` + индексируемые `/sensors/{slug}`) в SPA-режиме; выход — `.output/public`.
3. **postbuild**: `scripts/postbuild.mjs` создаёт `__spa-fallback.html` (копия `index.html` без canonical — фолбэк отдаётся под чужими URL).
4. BFF забирает `.output/public` как `wwwroot`; `dotnet publish` собирается с `/p:SkipFrontendBuild=true` — фронт в контейнер едет из node-стадии `deploy/Dockerfile.web`.

## Структура

```
src/
├── routes/          # маршруты TanStack Router (routeTree.gen.ts генерируется)
├── features/        # home · sensors · sensor-detail (history/) · map · about · app
├── components/
│   ├── site/        # доменные: MetricChart, CombinedChart, SensorMap, StatusBadge, Shell…
│   └── ui/          # shadcn/Radix-примитивы
├── lib/
│   ├── api/         # клиент BFF: client, queries (TanStack Query), types, ranges
│   ├── registry.ts  # чтение сгенерированного реестра (статический SEO-контент)
│   └── …            # seo, metrics, format, favorite-* (localStorage)
├── i18n/            # русская локализация
├── generated/       # sensorRegistry.json (gitignored, генерируется)
└── styles.css       # Tailwind v4
```

## Данные и SEO — два контура

- **Статический контент** (имя, описание, локация, перечень метрик) берётся из реестра и попадает в пререндерed HTML — это то, что видит поисковик без JavaScript.
- **Живые данные** (`/api/v1/dashboard`, `/sensors/{slug}`, `/status`, `/latest`, `/measurements`, `/forecast`) — только после гидрации через TanStack Query. Сборка не делает сетевых запросов.
- Правила SEO-конвейера (canonical со слэшем, 404 вместо catch-all-200, sitemap) — в корневом [AGENTS.md](../../AGENTS.md), грабля 4.

Для агентов: см. [AGENTS.md](./AGENTS.md) в этой папке — инварианты, команды, структура. Исторический промпт редизайна — [docs/reference/frontend-redesign-prompt.md](../../docs/reference/frontend-redesign-prompt.md).

Фронтенд вырос из [Lovable](https://lovable.dev)-проекта (TanStack Start) и дизайн-референса [mesh-skyline](https://github.com/xarleyn/mesh-skyline); развивается в этом репозитории.
