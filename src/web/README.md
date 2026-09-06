# MeshSense Telemetry

# Промпт для frontend-ИИ: переработка фронтенда MeshSMO Sensors

## Роль и задача

Ты — senior frontend-инженер. Задача: полная переработка UI проекта **MeshSMO Sensors**
(публичный сайт телеметрии LoRa-датчиков, `sensors.meshsmo.ru`) в репозитории
`D:\repos\MeshSMO\MeshSMO.Sensors`. Текущий фронтенд — голая заглушка: четыре статические
страницы без живых данных, несистемный CSS (в том числе сломанные светлые границы карточек
на тёмном фоне). Нужно довести его до продакшн-уровня: дизайн-система, подключение живых
данных из BFF API, живой дашборд, страница датчика с графиками — и при этом **не сломать
SEO/prerender-конвейер**, который уже работает и проверяется CI.

## Контекст проекта

- Сервис собирает показания физических датчиков через радиосеть MeshCore (LoRa) и публикует
  их в вебе. Регион — Смоленская область. Сейчас реальных данных почти нет (система
  разворачивается, один тестовый датчик) — **дизайн обязан одинаково хорошо выглядеть
  с нулём данных и с данными**. Пустые состояния — первоклассный deliverable, а не заглушка.
- Бэкенд — ASP.NET Core BFF (`src/MeshSMO.Sensors.Web`), он же раздаёт собранный фронтенд
  статикой. Фронтенд живёт в **`src/web`** (React Router Framework Mode как `.esproj`;
  при `dotnet build` вызывается `npm run build:dotnet`).
- Язык интерфейса — **русский**. Тон — инженерный и честный: никаких выдуманных цифр,
  lorem ipsum, «фейковых» превью данных.

## Стек (обязателен, не менять)

- React 19 + TypeScript (strict, `verbatimModuleSyntax`), **React Router v8 Framework Mode**,
  Vite 8, `ssr: false`.
- **Prerender**: `react-router.config.ts` генерирует статику для `/`, `/sensors`, `/about`
  и всех индексируемых `/sensors/:slug` из билд-тайм реестра
  `app/generated/sensorRegistry.json` (его создаёт `scripts/generate-registry.mjs`
  из `config/sensors/*.yaml` — пайплайн не трогать).
- SPA fallback для остальных маршрутов раздаёт BFF.
- Сервер-стейт: **TanStack Query v5** (добавить). Графики: **Apache ECharts** (добавить;
  tree-shaken через `echarts/core`, lazy-load только на странице датчика).
- CSS: выбери **Tailwind CSS v4** (`@tailwindcss/vite`) **или** CSS Modules на
  дизайн-токенах — один вариант и полная консистентность. Никаких тяжёлых UI-китов (MUI и т.п.).
- Dev-сервер: `127.0.0.1:5173`, `strictPort`, прокси `/api` и `/health` в BFF
  (см. `vite.config.ts`) — не менять.
- Тесты/линт: `npm run lint` (= tsc), `npm test` (= `node --test`),
  `npm run typecheck` (= `react-router typegen && tsc --noEmit`) — всё должно остаться
  зелёным; чистые функции (форматтеры значений, метрики) можно покрыть `node:test`.

## Жёсткие архитектурные правила (нарушать нельзя)

1. **Prerender без сети.** При `ssr:false` в prerender-роутах запрещены `loader`-ы.
   Вся живая дата — только после гидрации (TanStack Query). Компоненты с живыми данными
   во время билда рендерят **детерминированный** плейсхолдер («—», скелетон): гейт через
   `enabled: typeof window !== "undefined"` или mount-gate. Сборка не должна делать
   сетевых запросов и не должна требовать запущенный бэкенд.
2. **Гидрация без расхождений.** Никаких `Date.now()`, `Math.random()`, «N мин назад»
   в первом рендере prerender-роутов. Всё время-зависимое — только после монтирования.
3. **Два источника контента.** Статический контент (название, описание, локация, перечень
   метрик) — из реестра (`app/lib/registry.ts`), чтобы он присутствовал в prerendered HTML
   для SEO. Живые данные (state, latest, status, история) — только по API после гидрации.
4. Фронтенд знает только `/api/*` (same origin, CORS нет и не нужен). В production runtime
   Node.js отсутствует.
5. Не трогать: .NET-проекты, `config/sensors`, `deploy/`, `scripts/generate-registry.mjs`,
   порт/прокси Vite, `public/robots.txt`, URL-структуру (`/`, `/sensors`, `/sensors/:slug`,
   `/about`). HTTP 404 для несуществующих slug обеспечивает BFF — не эмулируй его редиректами.
6. Чистки: в `routes/home.tsx` заголовок продублирован разметкой вместо `` —
   унифицировать. Папку `app/welcome/` (пустой мусор) удалить. Английские тексты
   ErrorBoundary в `root.tsx` локализовать.

## Контракт API (base `/api/v1`, same origin)

### Реализовано сейчас

`GET /api/v1/dashboard` — один агрегированный payload для первого экрана:

```json
{
  "summary": { "total": 1, "online": 0, "degraded": 0, "offline": 0, "unknown": 1 },
  "sensors": [
    {
      "slug": "smolensk-center",
      "displayName": "Смоленск — центр",
      "description": "Тестовая конфигурация метеодатчика MeshSMO...",
      "latitude": 55.0000,
      "longitude": 33.0000,
      "metrics": ["battery", "humidity", "pressure", "temperature"],
      "state": "Unknown"
    }
  ]
}
```

`GET /api/v1/sensors` — тот же массив `{ "sensors": [...] }` без summary.

`GET /api/v1/sensors/{slug}`:

```json
{
  "slug": "smolensk-center",
  "displayName": "Смоленск — центр",
  "description": "...",
  "location": { "latitude": 55.0000, "longitude": 33.0000, "precision": "approximate" },
  "metrics": ["battery", "humidity", "pressure", "temperature"],
  "protocol": "meshcoretel-repeater",
  "pollIntervalSeconds": 300,
  "state": "Unknown"
}
```

`location` равен `null`, если координат нет. 404 `{ "error": "NotFound" }` для
неизвестного slug.

`GET /api/v1/sensors/{slug}/status`:

```json
{
  "state": "Unknown",
  "lastPollAt": "2026-09-06T09:00:00Z",
  "lastSuccessAt": null,
  "consecutiveFailures": 0,
  "lastRssi": -92.5,
  "lastSnr": 8.25,
  "updatedAt": "2026-09-06T09:01:00Z"
}
```

`GET /api/v1/sensors/{slug}/latest`:

```json
{
  "values": [
    {
      "metric": "temperature",
      "timestamp": "2026-09-06T09:00:00Z",
      "numericValue": 21.6,
      "textValue": null,
      "unit": "°C"
    }
  ]
}
```

### Специфицировано, но бэкендом пока НЕ реализовано — фронт обязан деградировать изящно

`GET /api/v1/sensors/{slug}/measurements?metric=temperature&from=&to=&resolution=auto`:

```json
{
  "sensor": { "slug": "smolensk-center", "displayName": "Смоленск — центр" },
  "metric": { "key": "temperature", "unit": "°C" },
  "range": { "from": "2026-09-04T00:00:00Z", "to": "2026-09-05T00:00:00Z", "resolution": "15m" },
  "points": [{ "timestamp": "2026-09-04T00:00:00Z", "min": 17.8, "avg": 18.1, "max": 18.6 }]
}
```

Downsampling на бэкенде: ≤24h → raw/5m; ≤7d → 15m; ≤31d → 1h; ≤180d → 6h; >180d → 1d.
Точек 2–5k максимум. Пока эндпоинт отдаёт 404/ошибку — график показывает честное пустое
состояние («история появится, когда датчик начнёт передавать данные»), а не ошибку приложения.

### Правила работы с данными

- Состояния датчика — enum: `Online` / `Degraded` / `Offline` / `Unknown`. Русские подписи:
  «В сети», «Нестабилен», «Не отвечает», «Статус неизвестен».
- Пуллинг: `latest`/`status`/`dashboard` — каждые 15–30 с (для датчика клампить снизу его
  `pollIntervalSeconds`).
- Все timestamps в API — ISO-8601 UTC; форматируй в таймзоне пользователя
  (`Intl.DateTimeFormat("ru-RU")`).
- Единицы брать из API (`unit` в latest/measurements); центральный реестр метрик
  `app/lib/metrics.ts`: `temperature` → «Температура» (°C), `humidity` → «Влажность» (%),
  `pressure` → «Давление» (hPa), `battery` → «Батарея» (V) — с иконкой и цветом для графика;
  неизвестные ключи не ломают UI.
- Пороги/нормы значений на фронтенде не хардкодить.

## Страницы и UX

### `/` — главная: лендинг + живой дашборд

- Хиро: eyebrow «LoRa · MeshCore · Смоленская область», заголовок, короткий lede,
  CTA «Смотреть датчики». Радио/mesh-мотив в оформлении (сетка/волны на фоне), но сдержанно.
- Живой блок дашборда (после гидрации, из `/api/v1/dashboard`): карточки-счётчики
  `[Всего N] [В сети N] [Нестабильны N] [Не отвечают N]` + список датчиков: имя, последняя
  температура (или первая метрика), статус-бейдж. Скелетоны при загрузке.

### `/sensors` — список датчиков

- H1 «Публичные датчики», короткое пояснение.
- Карточки/таблица датчиков из реестра (SEO-контент в HTML) + живые статус-бейдж и последнее
  значение по API. Ссылка на детальную страницу — вся карточка кликабельна.
- Пустое состояние: «Пока ни один датчик не опубликован...».

### `/sensors/:slug` — страница датчика (основная работа)

Референс из спеки:

```text
Смоленск — центр                 [В сети]

21.6 °C   62 %   1009 hPa   3.91 V

[ График: температура ]

[24h] [7d] [30d] [6m] [1y]

Диагностика канала
RSSI ...   SNR ...   Последний успешный опрос ...
```

- Шапка: имя + статус-бейдж; описание; локация (координаты моноширинно + текстовая точность,
  без карт в v1).
- Блок «Показания»: крупные mono-цифры текущих значений из `latest` (по одной карточке
  на метрику), время измерения.
- Блок «История»: график ECharts — линия `avg` + полоса min–max (points дают min/avg/max),
  сетка, tooltip с локальным временем; селектор диапазона `[24h][7d][30d][6m][1y]` и выбор
  метрики, **синхронизированные с URL**
  (`/sensors/smolensk-center?metric=temperature&range=7d`); canonical страницы без query
  string. График ленивый (`React.lazy`), при отсутствии данных — текстовое summary вместо
  пустого полотна.
- Блок «Диагностика канала» — **визуально отделён** от пользовательских показаний: RSSI (dBm),
  SNR (dB), последний опрос, последний успешный, счётчик подряд неудач, интервал опроса,
  протокол.
- Breadcrumbs «Главная / Датчики / Смоленск — центр» + `BreadcrumbList` JSON-LD.
- Неизвестный slug (в реестре нет) — брендированная 404 со ссылкой на `/sensors`
  (BFF уже отдал HTTP 404).

### `/about` — о проекте

Содержание сохранить/улучшить (как работает путь данных от эфира до страницы), оформить
по дизайн-системе.

## Дизайн-направление

Тема — тёмная, «радиотелеметрическая консоль», развитие текущей зелёной айдентики
(её не менять на другую):

```css
--bg: #0a100e; /* почти чёрный с зелёным подтоном */
--surface: #101815; /* карточки/панели */
--surface-raised: #142019;
--border: #1e2a24;
--text: #e8f1ec;
--text-muted: #94a69c;
--accent: #46d68c; /* фирменный зелёный */
--status-online: #34d399;
--status-degraded: #f5b840;
--status-offline: #f0655a;
--status-unknown: #7f8d86;
```

- Шрифты: Inter (UI) + JetBrains Mono (все значения, единицы, координаты, timestamps;
  `font-variant-numeric: tabular-nums`).
- Заголовки плотные (tight tracking), eyebrow-надписи капсом с letter-spacing — сохранить
  как мотив.
- Компоненты: карточка (1px граница + лёгкий подъём), статус-бейдж (точка + текст + цвет),
  кнопка/CTA, скелетон, пустое состояние; тосты не нужны.
- Фон страниц: едва заметная радиальная подсветка/сетка, без пестроты; тёмный `color-scheme`.
- Header sticky с blur, footer с подписью «Данные поступают по LoRa-сети».
- Мобильный лэйаут обязателен (360 px): карточки в колонку, значения сеткой 2×2, график
  на всю ширину.

## SEO (не сломать, усилить)

- У каждой индексируемой страницы: свой `title`/`description`/`canonical`/OG/Twitter-теги
  через `meta` экспорт React Router. Пример title:
  `Датчик «Смоленск — центр» — температура и влажность | MeshSMO`. Базовый URL —
  `publicBaseUrl()` из `app/lib/baseUrl.ts` (уже заведено через Vite `define`).
- JSON-LD: `WebSite` + `Organization` на главной, `BreadcrumbList` на страницах датчиков.
  Только по реально видимому контенту.
- Пререндер обязан продолжать выдавать: `build/client/index.html`,
  `build/client/sensors/index.html`, `build/client/sensors//index.html` для всех
  индексируемых slug, `__spa-fallback.html`. В prerendered HTML датчика должны
  присутствовать `

`, описание, локация, перечень показателей — без JavaScript.

- `lang="ru"`, favicon, `robots.txt`, `/sitemap.xml` (BFF) — не трогать.

## Доступность и производительность

- Семантические заголовки по иерархии, клавиатурная навигация, видимые `:focus-visible`
  состояния.
- Статус не только цветом: всегда точка/иконка + текстовая подпись.
- `aria-live` только для реально полезного живого обновления (последние показания — да,
  спиннеры — нет).
- Контраст AA на тёмном фоне; `prefers-reduced-motion` уважать (без анимаций графиков
  и transitions).
- Графики имеют текстовое summary (последнее значение, min/max за диапазон).
- Route-level code splitting (сам собой в RRV8) + lazy ECharts только на детальной странице;
  ECharts импортировать через `echarts/core` + нужные модули (`LineChart`, `GridComponent`,
  `TooltipComponent`, `CanvasRenderer`), не весь пакет.

## Порядок работы

1. `npm ci` в `src/web`; базовая проверка `npm run typecheck && npm run build` —
   зафиксировать зелёную точку.
2. Дизайн-система: токены, Tailwind (или CSS Modules), layout-компоненты
   (header/footer/page-shell), чистка дублей и багов текущего CSS.
3. API-слой: типы ответов, клиент, TanStack Query provider и хуки `useDashboard`,
   `useSensors`, `useSensor(slug)`, `useStatus(slug)`, `useLatest(slug)`,
   `useMeasurements(slug, metric, range)`.
4. Главная + дашборд, `/sensors` со живыми статусами.
5. `/sensors/:slug`: показания, график с URL-синхронизацией, диагностика, meta + JSON-LD.
6. Состояния (loading/error/empty), мобильная вёрстка, a11y-полировка, локализация
   ErrorBoundary.
7. Финальная проверка по чек-листу.

## Критерии приёмки

- [ ] `npm run typecheck`, `npm run lint`, `npm test`, `npm run build` в `src/web` —
      зелёные; `dotnet build` решения — зелёный.
- [ ] `build/client` содержит prerender HTML для `/`, `/sensors`, `/about` и всех
      индексируемых slug; в HTML датчика есть h1/описание/метрики; живых значений
      и «loading»-текста в prerendered HTML нет.
- [ ] Сборка не делает сетевых запросов.
- [ ] Живой дашборд на главной, живые статусы на `/sensors`, значения/статус/график/
      диагностика на `/sensors/:slug`.
- [ ] `?metric=&range=` синхронизированы с URL; перезагрузка страницы восстанавливает
      вид графика.
- [ ] Все данные имеют loading/error/empty состояния; при недоступности API страница
      остаётся читаемой (статический контент на месте).
- [ ] Статус-бейджи различимы без цвета; клавиатурная навигация работает; мобильная
      вёрстка от 360 px не ломается.
- [ ] Ни одного выдуманного значения данных в UI.

This project was built with [Lovable](https://lovable.dev).

**Live app**: https://mesh-skyline.lovable.app

## Build with Lovable

Continue developing this project in the [Lovable editor](https://lovable.dev/projects/75b8fb65-155d-489f-a1d3-b69e65ec23a6).

- **Ship faster**: describe what you want to build and Lovable handles the code.
- **Stay in sync**: every change made in Lovable is committed straight to this repository.
- **Full ownership**: this code is yours. Push to `main` on GitHub and your changes sync back into Lovable, ready for your next prompt.

## Development

Prefer working locally? You need Node.js and npm — [install with nvm](https://github.com/nvm-sh/nvm#installing-and-updating).

```sh
git clone <this-repository-url>
cd <repository-name>
npm i
npm run dev
```
