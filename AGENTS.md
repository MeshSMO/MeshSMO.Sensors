# AGENTS.md

Инструкция для ИИ-агентов и новых разработчиков: что это за репозиторий, где что лежит, как собирать и какие грабли уже найдены. **Прочитай это перед тем, как исследовать код руками.**

## Что это

**MeshSMO Sensors** — сервис сбора и публичного отображения телеметрии датчиков, доступных через радиосеть MeshCore (LoRa). Три runtime-компонента:

- **sensor-gateway** — .NET Worker+Kestrel, единственный, кто общается с репитером MeshCoreTel (HTTP/serial). Собирает панельную телеметрию репитера (opt-in: `MeshCore:TelemetryCollectionEnabled`, по умолчанию выключена — main API её не потребляет), опрашивает pull-only датчики через acquisition API прошивки, всё складывает в локальную SQLite outbox и отдаёт по внутреннему HTTP API (pull) либо сам доставляет батчи на web (push, `Push:ApiUrl`). **Доступа к PostgreSQL не имеет.**
- **sensor-web** — ASP.NET Core BFF + TanStack Start SPA (статический prerender, без Node.js в рантайме). Забирает батчи из gateway (pull) или принимает их на `POST /api/telemetry/ingest` (push, `Gateway:Mode=Push`), пишет в PostgreSQL, отдаёт `/api/v1/*`, статику, sitemap/robots.
- **sensor-db** — PostgreSQL 17 (только для sensor-web и DbMigrator).

Подробности: [ARCHITECTURE.md](./ARCHITECTURE.md).

## Product context (для всех текстов: README, UI, SEO, docs)

Иерархия смыслов: **MeshSMO — проект/экосистема, Sensors — один его сервис, MeshCore/LoRa — технология.** Нижний уровень не поднимается выше верхнего — ни в текстах, ни визуально.

- **MeshSMO** — некоммерческий проект региональной LoRa mesh-сети в Смоленске и Смоленской области; вокруг сети развивается набор сервисов (карта, мониторинг инфраструктуры, документация, датчики). Планируемые сервисы не описывать как существующие.
- **MeshSMO Sensors** — сервис этого проекта: опрос подключённых к сети физических датчиков, хранение истории измерений, публичный сайт и API. Sensors ≠ вся сеть и ≠ весь MeshSMO — не определять MeshSMO через телеметрию датчиков.
- Пользовательские тексты отвечают на «что и зачем», техника («как») — следом. Детали реализации (outbox, BFF, prerender, «API того же домена») — не маркетинговые тезисы; не обещать того, что pull-опрос не гарантирует («без задержек», «без посредников»).
- Данные: измеренное называть измеренным; агрегаты и прогноз явно помечать как расчётные значения; пропуски не заполняются прогнозом и отображаются как отсутствие данных.
- SEO описывает продукт («датчики MeshSMO в Смоленске и Смоленской области», «история температуры, влажности и других показателей»), а не повторяет слова LoRa/MeshCore/телеметрия.

## Карта репозитория

| Путь | Что это |
|---|---|
| `src/MeshSMO.Sensors.Domain` | Домен без зависимостей: `Sensor`, `SensorId`/`SensorSlug` (value objects), `SensorMetric` (с `DisplayName`/`Unit`), `MeasurementSample/Value`, `PollAttempt`, `SensorState` |
| `src/MeshSMO.Sensors.Application` | Абстракции: репозитории, `ISensorRegistry`, `ISensorProtocol`, `IMeshTransport`, `SensorDefinition`, `TelemetryChannelMapping` (+ `TelemetryTypes.KnownTypes` — все LPP-типы) |
| `src/MeshSMO.Sensors.Infrastructure` | EF Core + Npgsql (`SensorsDbContext`, миграции, репозитории), GitOps-registry (`FileSystemSensorRegistry` — YAML-парсинг и валидация, `SensorRegistrySynchronizer`) |
| `src/MeshSMO.Sensors.Forecasting` | ML.NET SSA: подготовка равномерного ряда, baseline, rolling backtest, quality gates и построение прогноза; не зависит от EF/ASP.NET и не хранит модели/результаты |
| `src/MeshSMO.Sensors.Gateway` | `Worker` (опрос телеметрии репитера), `MeshCore/` (HTTP+serial клиенты репитера), `Polling/` (`SensorTelemetryPoller`, `CayenneLppDecoder`), `LocalStorage/` (SQLite outbox на EF Core: `LocalOutboxDbContext` + `LocalTelemetryStore`, EF-миграции в `Migrations/`, применяются на старте), `Api/` (внутренний telemetry API), `Push/` (push-доставка в web: `TelemetryPushWorker` + клиент) |
| `src/MeshSMO.Sensors.Web` | BFF: `Api/` (публичные endpoints, sitemap, SPA-fallback), `GatewayIngestion/` (pull-воркер, ingest-эндпоинт push-режима, общий `GatewayTelemetryImporter`, клиент gateway) |
| `src/MeshSMO.Sensors.DbMigrator` | One-shot: EF-миграции + sync registry → БД. `--validate-registry` — только валидация YAML (используется в CI) |
| `src/web` | Фронтенд: TanStack Start в SPA-режиме (`spa.enabled`, prerender статикой, без SSR-рантайма); публичная проекция registry генерируется локально на prebuild (`scripts/generate-registry.mjs` → gitignored `src/generated/sensorRegistry.json`); UI на Tailwind v4 + Radix/shadcn, живые данные через TanStack Query |
| `config/sensors/*.yaml` | Реестр датчиков (один файл — один датчик); в git не хранится (gitignored, чувствительные данные) — правится локально и на хосте деплоя, в репо только `schema.json`. `polling.schedule` — per-sensor расписание интервалов опроса по времени суток (gateway-only, `timeZone` обязателен) |
| `deploy/` | `compose.yaml`, `Dockerfile.{web,gateway,dbmigrator}`, `env.example`, локальный `.env` (в git не хранится) |
| `tests/MeshSMO.Sensors.UnitTests` | xUnit; в т.ч. in-memory TestServer тесты gateway/web API. Папки зеркалят тестируемые проекты (`Domain/`, `Infrastructure/`, `Gateway/MeshCore|Polling|LocalStorage|Api|Push`, `Web/Api|GatewayIngestion`), неймспейс повторяет папку — новые тесты класть туда же |
| `tests/MeshSMO.Sensors.ProtocolTests` | Golden-тесты wire-протокола (пока минимальные) |

## Ключевые документы

| Документ | Содержание |
|---|---|
| [docs/README.md](./docs/README.md) | Индекс всей документации: что читать по какому вопросу |
| `CODESTYLE.md` | Правила кодстайла, проверяемые сборкой: warnings-as-errors, анализаторы, что именно ломает билд. Читать перед написанием C#-кода |
| `docs/specs/implementation-spec.md` | Главная спека: требования, модель данных, фазы (§49–59 с чекбоксами), MVP-определение (§60). В шапке — свод «план vs факт» |
| `docs/specs/repeater-acquisition-spec.md` | Контракт прошивки репитера: `POST /api/request`, `POST /api/login`, CLI `req`/`login`. Реализовано и проверено на железе |
| `docs/specs/forecasting-spec.md` | Прогнозирование: ML.NET SSA, quality gates, публичный API, план фаз F0–F4 (F1–F3 слиты) |
| `docs/reference/wire-protocol.md` | Wire-формат: MeshCore REQ/ANON, Cayenne LPP, формат payload'ов outbox |
| `docs/reference/frontend-redesign-prompt.md` | Исторический промпт, по которому фронт переписывался внешним ИИ (Lovable/TanStack Start); контракты API в нём актуальны |

## Команды

```bash
dotnet build MeshSMO.Sensors.slnx                 # сборка всего (включая фронт через esproj)
dotnet test tests/MeshSMO.Sensors.UnitTests       # unit-тесты
dotnet run --project src/MeshSMO.Sensors.DbMigrator -- --validate-registry   # валидация YAML
dotnet ef migrations add <Name> --project src/MeshSMO.Sensors.Infrastructure \
    --startup-project src/MeshSMO.Sensors.DbMigrator                         # EF-миграция

cd src/web && npm ci && npm run build          # фронт (prebuild генерирует src/generated/sensorRegistry.json)
cd deploy && docker compose up -d --build      # полный стек (нужен .env, см. env.example)
```

Порты: в Docker web → `:8080` (gateway не публикуется); локально web → `:5200`, vite dev → `:5173` (проксирует `/api` и `/health` на `:5200`; SPA-прокси запускает vite сам при `dotnet run`).

## Грабли, на которые уже наступали (не наступай снова)

1. **Npgsql не транслирует `sensor.Slug.Value == slug`** (член по value object). Сравнивай value object'ы: `sensor.Slug == new SensorSlug(slug)` с try/catch для кривых slug. Тесты на EF InMemory это НЕ ловят — InMemory принимает всё.
2. **Compose обрабатывает `$` в `.env` как специальный символ**: каждый литеральный знак доллара в значении нужно удваивать. Проверять итоговую подстановку следует без вывода секрета в терминал или CI-лог.
3. **ESP32-репитер по TLS**: только TLS 1.2 + cipher `TLS_RSA_WITH_AES_128_GCM_SHA256` (static-RSA). Из Linux-контейнеров (OpenSSL) обязателен `CipherSuitesPolicy` — уже зафиксирован в `MeshCoreGatewayServiceCollectionExtensions`. На Windows работает и без него.
4. **Фронт (`src/web`) — TanStack Start в SPA-режиме**: SSR-рантайма нет, прод-выход — только статика `.output/public` (её BFF раздаёт как `wwwroot`). Prerender-набор страниц захардкожен в `vite.config.ts` через `prerenderPaths` из gitignored публичной проекции `src/generated/sensorRegistry.json` (генерируется `npm prebuild` из `config/sensors`; если локальных YAML нет — генератор пишет пустой массив). В проекцию разрешено включать только датчики с `public.visible: true`; фильтрация после попадания данных в клиентский bundle не считается защитой. Если правишь схему registry YAML — правь и `scripts/generate-registry.mjs`, и `src/lib/registry.ts`. Контракт BFF: `index.html`, `__spa-fallback.html` (= копия `index.html`, делает `scripts/postbuild.mjs`, который вычищает из неё canonical — фолбэк раздаётся под чужими URL и неверный canonical в сыром HTML перебил бы per-route canonical после гидрации), `sensors/<slug>/index.html` для индексируемых slug. Canonical/og:url/JSON-LD/sitemap — со слэшем на конце (`https://…/sensors/<slug>/`): `UseDefaultFiles` в BFF 301-ит безслэшевые URL на слэш-вариант, и именно он отдаёт 200 — неслэшевые canonical там ломают консистентность (2026-09-08). Неизвестные URL вне `/sensors/*` — настоящий 404 (телом — SPA-фолбэк, рендерит клиентскую not-found), а не 200. Lovable-обёртка (`@lovable.dev/vite-tanstack-config`) может «пиннить» опции (crawlLinks, autoSubfolderIndex) — при сомнениях смотреть в её `dist/index.js`.
5. **`deploy/Dockerfile.web`**: `dotnet publish` идёт с `/p:SkipFrontendBuild=true` — esproj не должен запускать npm в контейнере (JavaScript SDK игнорирует `ShouldRunBuildScript` для publish-таргетов). Фронт собирает node-стадия и кладёт `.output/public` в `wwwroot`.
5a. **.NET 10 DI: `AddOptions<T>()` больше не регистрирует сам `T`** — `Validate<TDep>`/`ValidateOnStart`, резолвящие `T` из DI, падают с «No service for type T has been registered» на старте. Тесты на TestServer это не ловят, если они собирают своё приложение (см. `GatewayIngestEndpointsTests`); фикс — явная `AddTransient<T>(sp => sp.GetRequiredService<IOptions<T>>().Value)` в `Program.cs`.
6. **SQLite в тестах**: после Dispose приложения вызывай `SqliteConnection.ClearAllPools()`, иначе файл залочен. Имя InMemory-БД (`UseInMemoryDatabase(...)`) должно быть фиксированной строкой — лямбда вызывается на каждый scope.
7. **Идемпотентность измерений**: `measurement_samples` имеет unique `(sensor_id, request_id)`. `request_id` генерирует gateway (монотонный счётчик от unix time). Не создавай сэмплы в обход этого ключа.
8. **Outbox-дисциплина**: gateway пишет снапшоты только в SQLite; удаляет их по ack от web (pull) или после 2xx от `POST /api/telemetry/ingest` (push); web подтверждает только после успешной записи в PostgreSQL. Не нарушай этот порядок. Маппинг payload → БД общий у обоих режимов — правь `GatewayTelemetryImporter`, не дублируй его.
9. **Пароли/ключи**: в git не хранятся. Пароль админа репитера — `MESHCORE_HTTP_ADMIN_PASSWORD` в `deploy/.env`. Пароли нод — per-sensor: `mesh.loginPassword` в `config/sensors/*.yaml` с подстановкой `${VAR}` / `${VAR:-default}` (лимит 15 байт UTF-8; пустая строка = у ноды нет пароля; поле отсутствует = общий `SensorPolling:LoginPassword` из `SENSOR_LOGIN_PASSWORD`). Переменные из `.env` прокидываются в gateway/dbmigrator через `env_file`; литеральный `$` в `.env` экранируйте как `$$` (см. граблю 2). Сами YAML реестра в git не хранятся (gitignored, см. карту репо).
10. **Панель репитера — один глобальный токен**: каждый успешный `POST /login` инвалидирует предыдущий токен. Все потребители панели (`Worker` + `SensorTelemetryPoller`) обязаны делить singleton `MeshCoreTelSession` — логин один на старте, повторный только после 401. Новый код не должен логиниться в панель самостоятельно: клиент с собственным логином устроит взаимный 401-пинг-понг (перелогин на каждый запрос). Idle-таймаут сессии в прошивке — 24 ч; после ребута репитера gateway перелогинивается сам на первом 401.
11. **Hosted-сервисы не должны потреблять scoped-сервисы через конструктор**: `SensorTelemetryPoller` (singleton) падал на старте с «Cannot consume scoped service ISensorRegistry from singleton», потому что брал `ISensorRegistry` (scoped) напрямую. Резолвь через `IServiceScopeFactory.CreateScope()` внутри `ExecuteAsync` (как `GatewayIngestionWorker`). Коварно вдвойне: в Docker/Production валидации DI-графа нет и баг молча «работает» (scoped живёт как singleton), а падает только локальный `dotnet run` (Development → ValidateOnBuild). Тесты на TestServer тоже не ловят — `WebApplication.CreateBuilder()` в них по умолчанию в Production.
12. **`Microsoft.AspNetCore.OpenApi`: генератор протекает в consumers**: source generator пакета падает с CS9137 (interceptors) в любом проекте, который получает его транзитивно через ProjectReference (юнит-тесты). Зафиксировано глобально в `Directory.Build.props` (`InterceptorsNamespaces`) + пакет в `MeshSMO.Sensors.Web.csproj` подключён с `PrivateAssets="Analyzers;Build"`. Наследование для новых тестовых проектов уже работает — не убирай.
13. **`+` в query-параметрах**: таймстампы вида `...+00:00` в query string теряют `+` (становятся пробелом) → параметр не биндится и валидация отдаёт 400. Фронт шлёт `toISOString()` (суффикс `Z`) — ок; в тестах/скриптах экранируй `Uri.EscapeDataString`.
14. **Локальный outbox — EF Core, не raw SQL**: gateway-овская SQLite живёт в `LocalOutboxDbContext` (`Gateway/LocalStorage`); схема — EF-миграции в `src/MeshSMO.Sensors.Gateway/Migrations`, применяются на старте gateway (`LocalOutboxDatabase.MigrateAsync` в `Program.cs`, отдельного SQLite-мигратора нет и не нужно). Новые поля/таблицы — `dotnet ef migrations add <Name> --project src/MeshSMO.Sensors.Gateway` (design-time factory лежит в проекте). Ручные `CREATE TABLE`/`SqliteConnection` для доступа к данным туда не возвращать (прагмы `busy_timeout`/`synchronous` — через `SqliteOutboxConnectionInterceptor`, WAL выставляет мигратор). БД, созданная до EF-миграций (без `__EFMigrationsHistory`), миграцией не подхватится — при апгрейде просто удалить `gateway-telemetry.db` (несбитые снапшоты теряются, допущено осознанно).
15. **Любой warning = ошибка сборки**: `TreatWarningsAsErrors` + `AnalysisLevel latest-Recommended` + Meziantou/Roslynator + `EnforceCodeStyleInBuild` (`Directory.Build.props`, `.editorconfig`, с 2026-09-06). Компиляторный, анализаторный или стилевой warning валит `dotnet build` — «оставить на потом» нельзя. Свод правил и список исключений — [CODESTYLE.md](./CODESTYLE.md).
16. **Автофикс анализаторов может молча ломать поведение — прогоняй тесты после «Auto fix all»**: MA0023 («use ExplicitCapture or named groups») фиксером дописал `RegexOptions.ExplicitCapture` к `EnvironmentReferenceRegex` с нумерованными группами — группы перестали захватываться, резолв `${VAR}` в `mesh.loginPassword` стал отдавать пустое имя («environment variable '' that is not set»), 3 теста registry покраснели. Правило: в regex'ах с группами, которые читаются через `Groups[i]`, использовать именованные группы `(?<name>...)` и читать через `Groups["name"]`. Для справки: гигантские многоинсертные команды в логах web — это EF/Npgsql batching одного `SaveChanges` (один roundtrip, это норм); с 2026-09-10 логирование переведено на Serilog, SQL-команды (`Microsoft.EntityFrameworkCore.Database.Command`) в консоль не пишутся вовсе — только в файловый sink (в контейнерах `/app/logs` — том в compose, локально `bin/**/logs`; ротация по дню + 128 МиБ, 14 файлов). Уровни — секция `Serilog:MinimumLevel` в appsettings, переопределение через `Serilog__*` env. Грабля: в `Serilog.Extensions.Hosting` 10.0.0 нет перегрузок `UseSerilog(IHostApplicationBuilder)` — Web/Gateway идут через классический `builder.Host.UseSerilog(...)` (IHostBuilder), DbMigrator через статический `Log.Logger` + `builder.Logging.AddSerilog(Log.Logger, dispose: true)`.
17. **Форматирование фронта — только через Prettier, EOL — только LF** (с 2026-09-07): `npm run lint` включает Prettier как ESLint-правило, а `npm run format` / `npm run format:check` (последний — шаг в CI) — это тот же Prettier. Ручное форматирование «по .editorconfig» линт не пройдёт — Prettier навязывает ещё кавычки/переносы по printWidth 100/trailing commas, которые .editorconfig не описывает. Не форматтируй ts/tsx руками — правь код и запускай `npm run format`. EOL: `.gitattributes` (`* text=auto eol=lf`) + `.prettierrc` (`endOfLine: lf`) + закоммиченные `.vscode/settings.json` (format on save, Prettier как форматтер для веб-файлов). Временные каталоги для диффов/проверок создавать **вне** репо или в gitignored-путях: каталог `.headcheck`, созданный для сравнения HEAD с Prettier, ускакал в коммит 17ce473 и валит CI (прибран в следующем коммите). Вывод `scripts/generate-registry.mjs` (`src/generated/`) находится в `.gitignore` и `.prettierignore`: он генерируется заново, не коммитится и не проверяется Prettier.

## Куда класть новый код

- Доменные правила и value objects → `Domain` (без EF/JSON/HTTP).
- Контракты → `Application`.
- EF, registry-парсинг, синхронизация → `Infrastructure`.
- Схема локального SQLite outbox gateway → `Gateway/LocalStorage` (EF-модель + EF-миграции в том же проекте; не смешивать с Npgsql-`Infrastructure`).
- Всё, что касается LoRa/serial/repetера → **только** `Gateway`. BFF не должен знать про MeshCore.
- Маппинг/чтение PostgreSQL → `Web` (или `Infrastructure`-репозитории).
- Алгоритмы прогнозирования и их чистые контракты → `Forecasting`; PostgreSQL source, HTTP/cache/rate limits → `Web`.
- Новые типы LPP-телеметрии → `CayenneLppDecoder` + `TelemetryTypes.KnownTypes` + golden-тест.

## Текущий статус (2026-09-08)

Работает end-to-end на железе: gateway опрашивает физический датчик через репитер (`POST /api/request`, ANON-логин bootstrap), LPP-значения с маппингом каналов идут SQLite → PostgreSQL → `/api/v1/*`. Реализованы фазы 0, 1, 3–8 (6 — SEO-ассерты как unit-тесты BFF, 7/8 — в переписанном фронте; детальные чекбоксы и «план vs факт» — в спеке §49–59). Доставка outbox → PostgreSQL: pull (по умолчанию) и push (`Gateway:Mode=Push` + `Push:ApiUrl`, спека §7.1.6) — для раздельного деплоя gateway/web без публикации портов gateway. Фронт переписан (Lovable/TanStack Start → `src/web`): живой дашборд, страница датчика с графиками (recharts), график истории работает через `GET /api/v1/sensors/{slug}/measurements` (resolution=auto, downsample min/avg/max, бюджет ~5k точек, параметр `maxPoints` до 50k + контрол плотности на фронте — процент от частоты измерений, `?density=` в URL; ошибки — 404/400 с `ValidationError`). Прогноз построен на ML.NET SSA (`Forecasting`): on-demand, quality gates (MASE/coverage/bounds), операционный флаг `Forecasting:LenientMode` их отключает (спека forecasting-spec.md §9.4.1). Phase 4 закрыт: due-time priority queue, retry с randomized backoff (по registry `pollMaxAttempts`, clamp 1..3), каждая попытка пишется gateway в outbox (`sensor_poll` с `attemptNumber`/`startedAt`, неудачи — `poll_attempt`, см. wire-protocol.md §6) и импортируется в `poll_attempts` (unique `sensor+request+attempt`); неудачные попытки двигают `sensor_status` (Degraded/Offline, пороги `Gateway:{Degraded,Offline}AfterFailures`), успех сбрасывает в Online. Gateway `/health/ready` проверяет SQLite outbox и YAML registry (LoRa-недоступность не влияет); в compose healthchecks у web/gateway через curl. Rate limit на публичном API — per-IP 120 req/min (`public-api`), OpenAPI на `/openapi/v1.json`. Локальный outbox gateway переписан с raw ADO.NET/SQLite на EF Core (`LocalOutboxDbContext`, миграции применяются на старте gateway; 2026-09-06). Per-sensor расписание интервалов опроса по времени суток: `polling.schedule` в YAML реестра — полуоткрытые окна `[from, to)` с wrap через полночь, обязательный `timeZone` (IANA), вне окон действует базовый `interval`; режим переключается при следующей постановке в очередь (на границе окна «доезжает» максимум один старый интервал), правки самого YAML — при рестарте gateway (2026-09-07). Актуальные чекбоксы — в спеке §49–59. Снапшоты outbox в PostgreSQL неймспейсятся по gateway (unique `(gateway_id, gateway_snapshot_id)`; 2026-09-08). Документация актуализирована и переложена 2026-09-08: спеки — `docs/specs/`, справочники — `docs/reference/`, индекс — `docs/README.md`. Не сделано (Phase 9/10, отложено осознанно): OTel/метрики, backup/restore + runbook, security headers/CSP/сканы, restore drill/soak-тесты, авто-деплой на хост.
