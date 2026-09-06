# MeshSMO Sensors — техническая спецификация и план реализации

**Статус:** Draft v0.1  
**Дата:** 2026-09-05  
**Проект:** MeshSMO  
**Цель:** сервис сбора, хранения и публичного отображения показаний pull-only датчиков, доступных через MeshCore/LoRa.

---

## 1. Краткое решение

Система состоит из трёх постоянно работающих контейнеров:

1. **`sensor-gateway`** — .NET Worker, единственный компонент с доступом к MeshCoreTel Repeater. Собирает телеметрию через локальный HTTPS API либо USB Serial CLI, сохраняет её в локальную SQLite outbox и отдаёт накопленные snapshots по внутреннему HTTP API сервису `sensor-web`. **PostgreSQL-доступа у gateway нет.**
2. **`sensor-web`** — ASP.NET Core BFF + собранный React frontend. Забирает телеметрию из gateway по внутреннему API, сохраняет её в PostgreSQL и отдаёт API, статические/prerendered HTML-файлы React Router и SPA fallback.
3. **`sensor-db`** — PostgreSQL.

React **не имеет отдельного production runtime**. Node.js используется только на этапе build.

```text
                        sensors.meshsmo.ru
                               |
                               v
                  +---------------------------+
                  | sensor-web                |
                  | ASP.NET Core BFF          |
                  | + React static assets     |
                  | + prerendered HTML        |
                  +-------------+-------------+
                                |
                         read   |   PostgreSQL
                                v
                      +-------------------+
                      | sensor-db         |
                      | PostgreSQL        |
                      +---------^---------+
                                |
                         flush  |
                  +-------------+-------------+
                  | sensor-gateway            |
                  | .NET Worker               |
                  | Polling + SQLite outbox   |
                  +-------------+-------------+
                                |
                         HTTPS / USB
                                |
                       MeshCoreTel Repeater
                                |
                    local stats / sensors
```

Архитектурная идея: **LoRa/serial никогда не протекают в BFF**, frontend никогда не общается с gateway напрямую, а web можно обновлять/перезапускать без остановки сбора телеметрии.

---

# 2. Цели

## 2.1. Функциональные

Система должна:

- хранить реестр датчиков;
- регулярно опрашивать pull-only датчики;
- работать с MeshCoreTel Repeater через HTTPS API или физический serial/USB CLI;
- сохранять полученные snapshots и показания в локальную durable SQLite outbox;
- коррелировать запросы и ответы;
- иметь timeout/retry/backoff;
- хранить историю измерений;
- хранить технические данные опроса: latency, успешность, RSSI/SNR, ошибки;
- определять состояние датчика: `Online`, `Degraded`, `Offline`, `Unknown`;
- предоставлять HTTP API для frontend;
- показывать dashboard;
- показывать страницу каждого публичного датчика;
- строить графики за разные интервалы;
- поддерживать responsive mobile UI;
- иметь публичные индексируемые страницы;
- публиковать sitemap, robots.txt, canonical URL, OpenGraph и JSON-LD;
- работать после рестарта любого отдельного приложения без потери целостности данных.

## 2.2. Нефункциональные

- один production origin: `https://sensors.meshsmo.ru`;
- отсутствие CORS между frontend и API;
- production frontend без Node.js runtime;
- идемпотентность обработки ответов;
- graceful shutdown;
- воспроизводимые Docker builds;
- GitHub Actions CI/CD;
- health checks;
- структурированные логи;
- OpenTelemetry-ready;
- миграции БД выполняются отдельно от обычного запуска двух приложений;
- конфигурация и секреты не хранятся в git;
- физическое MeshCore устройство доступно только `sensor-gateway`.

---

# 3. Технологический стек

## Backend

- **.NET 10 LTS**
- ASP.NET Core
- .NET Worker Service
- Entity Framework Core
- Npgsql
- PostgreSQL
- OpenTelemetry
- Serilog либо стандартный `Microsoft.Extensions.Logging` с JSON console output

Не привязывать архитектуру к конкретному minor/patch. Patch-версии обновляются Dependabot/Renovate и CI.

## Frontend

- React
- TypeScript
- React Router **Framework Mode**
- Vite toolchain
- `ssr: false`
- route pre-rendering для индексируемых страниц
- SPA fallback для остальных маршрутов
- TanStack Query для server-state
- библиотека графиков с хорошей производительностью на time-series (рекомендуемый кандидат: Apache ECharts; окончательно выбрать через небольшой spike)
- CSS: Tailwind CSS либо CSS Modules; решение принять до начала UI-компонентов

## Infrastructure

- Docker / Docker Compose
- GitHub Container Registry (`ghcr.io`)
- GitHub Actions
- reverse proxy уровня общей MeshSMO инфраструктуры (например Caddy/Traefik, если уже принят в основном репозитории)

---

# 4. Структура репозитория

Рекомендуется **monorepo одного bounded context**.

```text
sensors/
├── src/
│   ├── MeshSMO.Sensors.Domain/
│   ├── MeshSMO.Sensors.Application/
│   ├── MeshSMO.Sensors.Infrastructure/
│   ├── MeshSMO.Sensors.Gateway/
│   ├── MeshSMO.Sensors.Web/
│   ├── MeshSMO.Sensors.DbMigrator/
│   └── web/
│       ├── app/
│       ├── public/
│       ├── package.json
│       ├── react-router.config.ts
│       ├── vite.config.ts
│       └── tsconfig.json
│
├── config/
│   └── sensors/
│       ├── smolensk-center.yaml
│       └── ...
│
├── tests/
│   ├── MeshSMO.Sensors.UnitTests/
│   ├── MeshSMO.Sensors.IntegrationTests/
│   ├── MeshSMO.Sensors.ProtocolTests/
│   └── web/
│
├── deploy/
│   ├── compose.yaml
│   ├── compose.dev.yaml
│   └── env.example
│
├── docs/
│   ├── protocol.md
│   ├── operations.md
│   └── adr/
│
├── .github/
│   └── workflows/
│       ├── ci.yml
│       ├── docker.yml
│       └── deploy.yml
│
├── Directory.Build.props
├── Directory.Packages.props
├── MeshSMO.Sensors.slnx
└── README.md
```

---

# 5. Границы компонентов

## 5.1. `MeshSMO.Sensors.Domain`

Не зависит от:

- EF Core;
- ASP.NET Core;
- serial;
- MeshCore;
- JSON;
- HTTP.

Содержит:

- `Sensor`
- `SensorId`
- `SensorSlug`
- `SensorState`
- `MetricDefinition`
- `Measurement`
- `PollAttempt`
- доменные правила состояния датчика;
- value objects.

## 5.2. `MeshSMO.Sensors.Application`

Содержит use cases и интерфейсы:

```text
ISensorRepository
IMeasurementRepository
IPollAttemptRepository
ISensorRegistry
IMeshTransport
ISensorProtocol
IClock
```

Gateway-specific orchestration можно держать либо здесь, либо в `Gateway`, если она не используется больше нигде.

## 5.3. `MeshSMO.Sensors.Infrastructure`

Содержит:

- EF Core DbContext;
- PostgreSQL repositories;
- migrations;
- сериализацию общих конфигураций;
- OpenTelemetry registration;
- общие infrastructure adapters.

**Не содержит MeshCore-specific serial implementation**, чтобы физический transport оставался в gateway deployment boundary.

## 5.4. `MeshSMO.Sensors.Gateway`

Содержит:

- `BackgroundService`;
- poll scheduler;
- MeshCoreTel HTTPS transport;
- MeshCoreTel serial CLI transport;
- локальную SQLite outbox;
- request correlation;
- sensor protocol codecs;
- retry/timeout;
- обработку inbound events;
- запись измерений в БД;
- gateway health endpoint/health server при необходимости.

## 5.5. `MeshSMO.Sensors.Web`

Содержит:

- ASP.NET Core BFF;
- public API;
- admin API в будущем;
- frontend static-file hosting;
- SPA fallback;
- caching headers;
- sitemap;
- robots.txt;
- health endpoints.

## 5.6. `src/web`

Содержит весь React UI.

Frontend знает только `/api/*`.

Он **не знает**:

- topology MeshCore;
- serial;
- MeshCore packet format;
- внутренние DB entities.

---

# 6. Реестр датчиков

Для первой версии использовать **GitOps sensor registry**.

Один датчик — один YAML:

```yaml
id: "0198..."
slug: "smolensk-center"
displayName: "Смоленск — центр"
description: "Метеодатчик MeshSMO в центральной части Смоленска."

mesh:
  publicKey: "..."
  protocol: "meshsmo-weather-v1"

polling:
  interval: "5m"
  timeout: "30s"
  maxAttempts: 2
  enabled: true

public:
  visible: true
  indexable: true

location:
  latitude: 55.0000
  longitude: 33.0000
  precision: "approximate"

metrics:
  - temperature
  - humidity
  - pressure
  - battery

telemetry:
  channels:
    - channel: 1
      type: voltage
      metric: battery_voltage
      displayName: "Напряжение батареи"
      unit: "В"
    - channel: 2
      type: voltage
      metric: solar_panel_voltage
      displayName: "Напряжение солнечной панели"
      unit: "В"
    - channel: 3
      type: temperature
      metric: temperature
      displayName: "Температура"
      unit: "°C"
```

`telemetry.channels` — опциональный маппинг декодированных Cayenne LPP значений
(`channel` + LPP `type`, либо `*` для любого типа на канале) в публичные metric keys
с человекочитаемым `displayName` и `unit`. Значения без маппинга получают ключ LPP-типа
(с суффиксом канала, если тип встречается на нескольких каналах: `voltage_2`).
Маппинги синхронизируются в `sensor_metrics` (display_name, unit) и отдаются через API.

## Почему GitOps

Это даёт:

- review изменений через PR;
- стабильные `slug`;
- автоматический список URL для frontend prerender;
- воспроизводимый deploy;
- простой audit trail;
- отсутствие необходимости сразу делать admin UI.

При deploy конфигурация синхронизируется в таблицу `sensors`.

В будущем можно добавить admin UI, но `slug` и SEO metadata должны оставаться стабильными.

---

# 7. Repeater transport и sensor protocol

Критическая граница:

```text
Repeater transport != Sensor protocol
```

## 7.1. `IRepeaterClient`

Пример контракта:

```csharp
public interface IRepeaterClient
{
    string TransportName { get; }

    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<string> ExecuteCommandAsync(
        string command,
        CancellationToken cancellationToken);

    Task<JsonDocument> GetTelemetryAsync(
        CancellationToken cancellationToken);
}
```

Реализации:

```text
MeshCoreTelHttpClient
    -> HTTPS API

RepeaterSerialClient
    -> USB SerialPort
    -> text CLI
```

### 7.1.1. MeshCoreTel HTTP mode

Для устройств с MeshCoreTel-firmware Gateway также поддерживает локальный HTTPS API прошивки:

```text
MeshCoreTelHttpClient
    -> POST /login
    -> X-Auth-Token
    -> POST /api/command
    -> GET /api/stats
```

Этот режим предназначен для удалённого CLI, диагностики и статистики. Клиент:

- выполняет не более одного HTTP-запроса к устройству одновременно;
- повторно аутентифицируется и повторяет запрос один раз после `401`;
- поддерживает self-signed сертификат только через явную настройку;
- ограничивает размер команды согласно буферу прошивки;
- не публикует пароль или session token в логах.

### 7.1.2. MeshCoreTel Serial mode

Serial mode отправляет CLI-команду, завершённую `CR`, и читает ответ после маркера `->`. Телеметрия собирается командами `stats-core`, `stats-radio`, `stats-packets` и `sensor list`. Многостраничный `sensor list` дочитывается по маркеру `... next:<index>`.

### 7.1.3. Ограничение Repeater API

Текущие HTTP и serial CLI интерфейсы репитера не предоставляют Companion binary request/response API. Они подходят для телеметрии самого репитера и встроенных датчиков. Исходное требование опроса удалённых pull-only LoRa sensors нельзя реализовать только через эти интерфейсы: для него потребуется специальный endpoint в прошивке либо отдельное устройство, способное инициировать MeshCore requests.

### 7.1.4. Локальная SQLite outbox

Каждый опрос атомарно сохраняется в:

- `telemetry_snapshots` — время, transport и исходный JSON (единственные данные снапшота; отдельная таблица плоских readings `telemetry_readings` удалена миграцией `RemoveTelemetryReadings` 2026-09-06 — она дублировала payload и никем не потреблялась).

Записи удаляются только после успешной публикации в основное хранилище (`AcknowledgeAsync`). Незавершённая очередь переживает рестарт Gateway.

Схема outbox управляется EF Core (`LocalOutboxDbContext` в Gateway): миграции лежат в `src/MeshSMO.Sensors.Gateway/Migrations` и применяются при старте gateway (отдельного SQLite-мигратора нет).

### 7.1.5. Gateway telemetry API

Gateway не имеет доступа к PostgreSQL. Забирает данные из outbox основной backend (`sensor-web`) по внутреннему HTTP API gateway:

```text
GET  /api/telemetry/pending?maxCount=N   -> { pendingCount, snapshots: [{ id, capturedAt, transport, payloadJson }] }
POST /api/telemetry/ack                  -> { ids: [...] } — удалить подтверждённые snapshot'ы из outbox
GET  /health/live, /health/ready
```

Правила канала:

- gateway слушает только внутреннюю Docker network, наружу порт не публикуется;
- опциональный общий секрет `Gateway:ApiKey` проверяется по заголовку `X-Api-Key`;
- batch ограничен `Gateway:MaximumBatchSize`;
- ack отправляется только после успешной записи батча в PostgreSQL;
- идемпотентность обеспечивается уникальным `gateway_snapshot_id` в PostgreSQL: повторно доставленный snapshot пропускается, а не дублируется.

### 7.1.6. Push-режим доставки (gateway → основной API)

Альтернатива pull для раздельного деплоя: gateway и `sensor-web` на разных серверах, при этом gateway не должен быть доступен извне. Основной backend сам не опрашивает gateway — gateway самостоятельно доставляет батчи из локальной outbox на ingest-эндпоинт `sensor-web`. Наружу смотрит только `sensor-web` (он и так публичный), gateway входящих портов не имеет.

Контракт повторяет pull 1-в-1: тот же JSON `pendingCount` + `snapshots[]` с `payloadJson`, тот же `X-Api-Key`, та же идемпотентность (`gateway_snapshot_id`, `(sensor_id, request_id)`).

```text
sensor-web (Push mode):
POST /api/telemetry/ingest
     body: { pendingCount, snapshots: [{ id, capturedAt, transport, payloadJson }] }
     -> 200 { accepted: N }   (N — новых записанных снимков; 2xx = весь батч записан или уже был известен)
     -> 401 {"error":"Unauthorized"} — нет/неверен X-Api-Key
     -> 400 {"error":"BatchTooLarge"} — батч больше Gateway:Ingest:MaximumBatchSize
```

Выбор режима — на стороне `sensor-web`, ключ `Gateway:Mode`:

- `Pull` (по умолчанию) — текущее поведение: `GatewayIngestionWorker` опрашивает gateway по `Gateway:BaseUrl`;
- `Push` — pull-воркер выключен, маппится `POST /api/telemetry/ingest`.

На стороне gateway push включается наличием `Push:ApiUrl` — приложение продолжает обслуживать pull API (`/api/telemetry/pending` + `/ack`), поэтому переходный период, когда gateway уже пушит, а старый `sensor-web` ещё тянет, безопасен: обе стороны идемпотентны и делят одну outbox.

Конфигурация:

```text
# gateway
Push__ApiUrl=https://sensors.example.com   # наличие ключа включает push
Push__ApiKey=...                            # = Gateway__Ingest__ApiKey на sensor-web
Push__BatchSize=200
Push__IntervalSeconds=15
Push__AllowInsecureHttp=false               # http разрешён только явно (изолированные сети)

# sensor-web
Gateway__Mode=Push
Gateway__Ingest__ApiKey=...                 # обязателен в Push-режиме, иначе старт падает
Gateway__Ingest__MaximumBatchSize=500
```

Семантика надёжности:

- gateway читает батч из SQLite outbox (`ReadPendingAsync(BatchSize)`), отправляет его и удаляет снимки (`AcknowledgeAsync`) **только после 2xx** ответа;
- при сетевой ошибке, 5xx или 401 батч остаётся в outbox и повторяется на следующем тике `IntervalSeconds` — потери данных нет, ошибки видны в логах;
- повторная доставка не создаёт дублей: пропускаются уже сохранённые `gateway_snapshot_id`;
- битый `payloadJson` или неизвестный slug не ломают батч: снимок сохраняется как telemetry-only с warning в логе (как в pull), ответ всё равно 2xx.

Предохранители:

- `sensor-web` стартует с ошибкой, если `Gateway:Mode=Push`, но не задан `Gateway:Ingest:ApiKey` (иначе эндпоинт остался бы открытым);
- gateway стартует с ошибкой, если `Push:ApiUrl` задан, но `Push:ApiKey` пуст, URI не абсолютный или схема http без явного `Push:AllowInsecureHttp`;
- `Push:ApiUrl` по умолчанию должен быть HTTPS (прецедент — валидация `MeshCore:Http:BaseAddress`).

Рассинхрон конфигураций: если `sensor-web` в `Pull`, а gateway уже настроен на push, gateway будет бесконечно ретраить 404 — это видно в логах и не теряет данные. Сравнение заголовка `X-Api-Key` на ingest-эндпоинте — constant-time (`CryptographicOperations.FixedTimeEquals`).

## 7.2. `ISensorProtocol`

```csharp
public interface ISensorProtocol
{
    string ProtocolId { get; }

    ReadOnlyMemory<byte> BuildPollRequest(
        Sensor sensor,
        PollRequestId requestId);

    SensorResponse ParseResponse(
        Sensor sensor,
        ReadOnlySpan<byte> payload);
}
```

## 7.3. Wire protocol

Начать с versioned binary envelope.

Пример:

```text
+------------------+
| Version      u8  |
+------------------+
| MessageType  u8  |
+------------------+
| RequestId    u32 |
+------------------+
| Payload ...      |
+------------------+
```

Минимальные типы:

```text
0x01 PollRequest
0x02 PollResponse
0x03 InfoRequest
0x04 InfoResponse
0x7F Error
```

Точные integer encoding/endian/checksum описать в `docs/protocol.md` и покрыть golden tests.

Не использовать JSON внутри LoRa без отдельной причины: бинарный формат компактнее и предсказуемее по airtime.

---

# 8. Poll Scheduler

## 8.1. Требования

Scheduler должен:

- не синхронизировать все датчики после restart;
- учитывать individual poll interval;
- поддерживать jitter;
- избегать бесконечных retries;
- корректно переживать долгий timeout;
- предотвращать дублирующий poll одного датчика;
- поддерживать cancellation;
- сохранять `poll_attempt`;
- вычислять `NextPollAt`.

## 8.2. Базовый алгоритм

На старте:

1. загрузить enabled sensors;
2. вычислить `NextPollAt` с deterministic jitter;
3. положить sensors в priority queue;
4. выбрать ближайший;
5. дождаться due time;
6. выполнить poll;
7. записать результат;
8. вычислить следующее время;
9. вернуть sensor в queue.

Начальное ограничение:

```text
MaxConcurrentPolls = 1
```

Увеличивать concurrency только после реальных измерений поведения MeshCore сети.

## 8.3. Jitter

Jitter должен быть deterministic относительно `SensorId`, чтобы после каждого рестарта порядок оставался достаточно стабильным.

Например:

```text
effectiveOffset =
    hash(sensorId) % min(pollInterval * 0.20, 30 seconds)
```

## 8.4. Retry

Пример policy:

```text
attempt 1
  |
  +-- success -> done
  |
  +-- timeout
       |
       wait small randomized backoff
       |
       attempt 2
            |
            +-- success -> done
            +-- timeout -> failed poll cycle
```

Не делать aggressive retries: LoRa airtime — ограниченный ресурс.

---

# 9. Request correlation

Gateway должен иметь in-memory registry outstanding requests:

```text
RequestId -> PendingRequest
```

`PendingRequest`:

```text
requestId
sensorId
sentAt
deadline
TaskCompletionSource<Response>
```

Inbound pipeline:

```text
MeshCore inbound packet
       |
       v
decode envelope
       |
       v
extract requestId
       |
       +-- known request -> complete pending poll
       |
       +-- unknown request -> log/debug + metric
```

После timeout запись должна удаляться.

После restart outstanding requests считаются потерянными; это допустимо.

---

# 10. Sensor state machine

Состояние не хранить как единственный источник истины, если его можно вычислить. Допустимо кешировать/materialize для быстрого dashboard.

Рекомендуемые состояния:

```text
Unknown
Online
Degraded
Offline
Disabled
```

Пример правил:

### Online

- последний успешный poll не старше `2 * PollInterval`;
- `ConsecutiveFailures < degradedThreshold`.

### Degraded

- есть недавние успехи;
- несколько последних attempts завершились timeout/error;
- либо наблюдается явно плохой link quality.

### Offline

- нет успешного ответа дольше заданного offline threshold.

Thresholds конфигурируемые, не hardcode в frontend.

---

# 11. Модель данных

## 11.1. `sensors`

```text
id UUID PK
slug VARCHAR UNIQUE NOT NULL
display_name TEXT NOT NULL
description TEXT NULL

mesh_public_key TEXT UNIQUE NOT NULL
protocol_id TEXT NOT NULL

poll_interval_seconds INT NOT NULL
poll_timeout_seconds INT NOT NULL
poll_max_attempts INT NOT NULL
enabled BOOLEAN NOT NULL

public_visible BOOLEAN NOT NULL
public_indexable BOOLEAN NOT NULL

latitude DOUBLE PRECISION NULL
longitude DOUBLE PRECISION NULL
location_precision TEXT NULL

created_at TIMESTAMPTZ NOT NULL
updated_at TIMESTAMPTZ NOT NULL
```

## 11.2. `measurement_samples`

Один успешно принятый sensor response.

```text
id UUID PK
sensor_id UUID FK NOT NULL

request_id BIGINT NULL

measured_at TIMESTAMPTZ NULL
received_at TIMESTAMPTZ NOT NULL

rssi REAL NULL
snr REAL NULL
round_trip_ms INT NULL

protocol_id TEXT NOT NULL
raw_payload BYTEA NULL
extra JSONB NULL
```

Индекс:

```text
(sensor_id, received_at DESC)
```

## 11.3. `measurement_values`

Значения внутри sample.

```text
sample_id UUID FK NOT NULL
sensor_id UUID FK NOT NULL
metric_key TEXT NOT NULL
timestamp TIMESTAMPTZ NOT NULL

numeric_value DOUBLE PRECISION NULL
text_value TEXT NULL
unit TEXT NULL
quality TEXT NULL
```

Индекс для графиков:

```text
(sensor_id, metric_key, timestamp DESC)
```

Обычно значение будет `numeric_value`.

Такая модель намеренно допускает новые метрики без изменения schema.

Если объём вырастет настолько, что long-form storage становится проблемой, оптимизировать только после профилирования: partitioning, continuous aggregates/TimescaleDB либо отдельные typed tables.

## 11.4. `poll_attempts`

```text
id UUID PK
sensor_id UUID FK NOT NULL
request_id BIGINT NOT NULL

started_at TIMESTAMPTZ NOT NULL
completed_at TIMESTAMPTZ NULL

attempt_number INT NOT NULL

status TEXT NOT NULL
error_code TEXT NULL
error_message TEXT NULL

round_trip_ms INT NULL
```

Индекс:

```text
(sensor_id, started_at DESC)
```

## 11.5. `sensor_status`

Опциональная materialized/cache таблица:

```text
sensor_id UUID PK
state TEXT NOT NULL

last_poll_at TIMESTAMPTZ NULL
last_success_at TIMESTAMPTZ NULL

consecutive_failures INT NOT NULL

last_rssi REAL NULL
last_snr REAL NULL

updated_at TIMESTAMPTZ NOT NULL
```

---

# 12. Время и timestamps

В БД — только `TIMESTAMPTZ`.

В API — ISO-8601 UTC:

```text
2026-09-05T11:42:00Z
```

Frontend форматирует по timezone пользователя.

Если датчик передаёт собственное время:

- `measuredAt` = время датчика;
- `receivedAt` = время gateway.

Если датчик не имеет надёжных RTC:

- `measuredAt = null`;
- authoritative timestamp = `receivedAt`.

---

# 13. Миграции БД

Не запускать `Database.Migrate()` одновременно в `sensor-web` и `sensor-gateway`.

Создать:

```text
MeshSMO.Sensors.DbMigrator
```

Deployment:

```text
1. pull new images
2. start/update PostgreSQL if needed
3. run DbMigrator one-shot
4. start sensor-web
5. start sensor-gateway
6. health check
```

Rollback schema должен быть отдельно продуман для destructive migrations.

---

# 14. API BFF

Base:

```text
/api/v1
```

## 14.1. Sensors

```http
GET /api/v1/sensors
GET /api/v1/sensors/{slug}
GET /api/v1/sensors/{slug}/latest
GET /api/v1/sensors/{slug}/status
```

## 14.2. Measurements

```http
GET /api/v1/sensors/{slug}/measurements
    ?metric=temperature
    &from=...
    &to=...
    &resolution=auto
```

Ответ:

```json
{
  "sensor": {
    "slug": "smolensk-center",
    "displayName": "Смоленск — центр"
  },
  "metric": {
    "key": "temperature",
    "unit": "°C"
  },
  "range": {
    "from": "2026-09-04T00:00:00Z",
    "to": "2026-09-05T00:00:00Z",
    "resolution": "15m"
  },
  "points": [
    {
      "timestamp": "2026-09-04T00:00:00Z",
      "min": 17.8,
      "avg": 18.1,
      "max": 18.6
    }
  ]
}
```

## 14.3. Dashboard

```http
GET /api/v1/dashboard
```

Должен возвращать один агрегированный payload для первого экрана, чтобы frontend не делал N+1 запросов.

Например:

```json
{
  "summary": {
    "total": 12,
    "online": 10,
    "degraded": 1,
    "offline": 1
  },
  "sensors": []
}
```

---

# 15. Downsampling

Нельзя отдавать миллионы raw points браузеру.

`resolution=auto` выбирает агрегирование на backend.

Стартовая policy:

```text
range <= 24h       -> raw или 5m
range <= 7d        -> 15m
range <= 31d       -> 1h
range <= 180d      -> 6h
range > 180d       -> 1d
```

Bucket:

```text
timestamp
min
avg
max
count
```

API должен иметь верхний лимит количества точек, например около 2–5 тысяч на одну series.

Конкретное значение выбрать после UI/performance tests.

---

# 16. Кеширование

## Public metadata

Можно кешировать:

```text
GET /api/v1/sensors
GET /api/v1/sensors/{slug}
```

с коротким `Cache-Control`.

## Latest/status

Короткий cache либо no-cache в зависимости от poll interval.

## Historical measurements

Для завершившихся временных окон можно отдавать длинный cache lifetime.

Например вчерашние hourly buckets уже не меняются, если система не поддерживает late-arriving measurements.

---

# 17. Realtime

## v1

Frontend polling:

```text
/latest every 15–30 seconds
```

или относительно sensor poll interval.

## v2

Добавить Server-Sent Events:

```http
GET /api/v1/events
Accept: text/event-stream
```

События:

```text
measurement.received
sensor.status.changed
```

WebSocket на первой версии не нужен.

---

# 18. Frontend routes

## Публичные индексируемые

```text
/
 /sensors
 /sensors/:slug
 /about
```

Дополнительно в будущем:

```text
/metrics/:metric
```

если для этого реально появляется полезный уникальный контент.

## Не индексировать

```text
/admin/*
/debug/*
```

Если появится персонализированный интерфейс:

```text
/app/*
```

его также можно `noindex`.

---

# 19. SEO и rendering strategy

## 19.1. Основное решение

Использовать React Router Framework Mode:

```ts
export default {
  ssr: false,

  async prerender() {
    return [
      "/",
      "/sensors",
      "/about",
      // + /sensors/:slug из GitOps registry
    ];
  },
};
```

Production runtime SSR отсутствует.

Build генерирует:

```text
build/client/index.html
build/client/sensors/index.html
build/client/sensors/smolensk-center/index.html
...
build/client/__spa-fallback.html
assets/*
```

ASP.NET Core копирует/отдаёт эти файлы как static content.

## 19.2. Что означает «SPA»

После первой загрузки React hydrates HTML и дальнейшая навигация работает client-side.

То есть:

```text
initial request
    -> готовый индексируемый HTML

после hydration
    -> обычная SPA navigation
```

Это не «старый SPA с пустым div».

## 19.3. Dynamic sensor pages

Реализация: registry читается на пребилде скриптом `src/web/scripts/generate-registry.mjs` (npm prebuild), который создаёт `src/web/app/generated/sensorRegistry.json`. Маршруты сенсоров рендерят контент из этого JSON напрямую — при `ssr:false` React Router запрещает `loader` в prerender-роутах.

`prerender()` читает registry и генерирует URL для каждого:

```text
public=true
indexable=true
```

Пример:

```ts
async prerender() {
  const sensors = await loadSensorRegistry();

  return [
    "/",
    "/sensors",
    "/about",
    ...sensors
      .filter(x => x.public && x.indexable)
      .map(x => `/sensors/${x.slug}`)
  ];
}
```

Prerender должен использовать **локальный build artifact из `config/sensors`**, а не зависеть от production API.

Так CI остаётся reproducible.

## 19.4. Что входит в prerendered sensor page

В initial HTML должны присутствовать:

- `<h1>` с названием;
- описание датчика;
- район/приблизительная локация;
- перечень измеряемых показателей;
- пояснение, что данные передаются через MeshSMO/MeshCore;
- ссылки на соседние полезные страницы;
- нормальный semantic HTML.

Live data и charts могут загружаться после hydration.

---

# 20. Metadata

Каждая индексируемая route должна иметь собственные:

```html
<title>...</title>
<meta name="description" content="...">
<link rel="canonical" href="...">

<meta property="og:title" content="...">
<meta property="og:description" content="...">
<meta property="og:url" content="...">
<meta property="og:type" content="website">
<meta property="og:image" content="...">

<meta name="twitter:card" content="summary_large_image">
```

Пример title:

```text
Датчик «Смоленск — центр» — температура и влажность | MeshSMO
```

Не генерировать сотни почти одинаковых thin pages только ради SEO.

---

# 21. Structured data

Добавлять JSON-LD только там, где schema соответствует реальному содержимому.

Безопасный базовый набор:

## Главная

- `WebSite`
- `Organization`

## Breadcrumbs

- `BreadcrumbList`

Для страницы датчика не выдумывать schema type, если подходящего типа нет.

Structured data должен описывать видимый пользователю контент.

---

# 22. Sitemap

BFF отдаёт:

```text
/sitemap.xml
```

Источник URL:

- static routes;
- public/indexable sensors из registry/БД.

Для sensor URL:

```xml
<url>
  <loc>https://sensors.meshsmo.ru/sensors/smolensk-center</loc>
  <lastmod>...</lastmod>
</url>
```

Не нужно обновлять `<lastmod>` при каждом измерении.

Лучше менять его при существенном изменении страницы/metadata/config.

---

# 23. robots.txt

```text
User-agent: *
Allow: /

Disallow: /admin/
Disallow: /api/

Sitemap: https://sensors.meshsmo.ru/sitemap.xml
```

API не требуется индексировать.

`robots.txt` не является механизмом авторизации.

---

# 24. URL rules

Использовать:

```text
/sensors/smolensk-center
```

а не:

```text
/#/sensor?id=123
/sensor/0198d9...
```

Правила:

- lowercase;
- kebab-case;
- стабильные slugs;
- человекочитаемые URL;
- `slug` не меняется при переименовании display name;
- если slug всё же меняется — HTTP 301 со старого URL.

---

# 25. Canonical

Каждая indexable route имеет canonical на себя:

```text
https://sensors.meshsmo.ru/sensors/smolensk-center
```

Query parameters графиков:

```text
?s=temperature&range=7d
```

не должны становиться самостоятельными canonical страницами.

Canonical остаётся базовый sensor URL.

---

# 26. SPA fallback в ASP.NET

Порядок routing:

```text
/api/*              -> ASP.NET API
/health/*           -> ASP.NET
/sitemap.xml        -> ASP.NET
/robots.txt         -> ASP.NET

existing static HTML/assets
                     -> UseStaticFiles

unknown valid React route
                     -> __spa-fallback.html

unknown resource/API
                     -> 404
```

Нельзя indiscriminately отдавать SPA fallback для `/api/not-found`, иначе реальные API 404 превратятся в HTML 200.

---

# 27. HTTP status codes и SEO

Если датчика нет:

```text
GET /sensors/not-existing
-> HTTP 404
```

Не отдавать `200 index.html` для заведомо несуществующего public sensor slug.

Для отключённого/удалённого public sensor решить policy:

- временно скрыт -> 404/503 в зависимости от причины;
- навсегда перемещён -> 301;
- навсегда удалён -> 410 допустим, если это осознанно.

---

# 28. Frontend data architecture

## Server state

TanStack Query:

```text
useSensors()
useSensor(slug)
useLatestMeasurement(slug)
useMeasurements(slug, metric, range)
```

## Local state

Не добавлять Redux без реальной причины.

Для v1:

- URL search params;
- React state;
- TanStack Query cache.

Диапазон графика должен отражаться в URL:

```text
/sensors/smolensk-center?metric=temperature&range=7d
```

Это даёт shareable state, но canonical остаётся без query string.

---

# 29. Dashboard UX

Главная dashboard часть:

```text
[Online 10] [Degraded 1] [Offline 1]

Sensors
------------------------------------------------
Смоленск — центр      21.6°C      Online
...
```

Sensor page:

```text
Смоленск — центр                 Online

21.6 °C   62 %   1009 hPa   3.91 V

[ Temperature chart                            ]

[24h] [7d] [30d] [6m] [1y]

Link quality
RSSI ...
SNR ...
Last response ...
```

Не смешивать telemetry пользователя и диагностические значения без визуального разделения.

---

# 30. Accessibility

Минимум:

- semantic headings;
- keyboard navigation;
- focus states;
- charts имеют текстовое summary;
- цвет не является единственным способом показать Online/Offline;
- `aria-live` только для действительно полезных live updates;
- contrast AA;
- mobile layout.

---

# 31. Performance budgets

Стартовые цели:

- prerendered HTML содержит meaningful content;
- lazy-load тяжёлой chart library на routes, где график реально нужен;
- route-level code splitting;
- не грузить map/chart code на главной, если он не используется;
- Brotli/Gzip;
- immutable caching hashed assets;
- изображения WebP/AVIF где уместно.

Задать CI budget после первого production-like Lighthouse прогона.

---

# 32. Docker build `sensor-web`

Multi-stage:

```text
stage 1: node
  npm ci
  npm run build

stage 2: dotnet sdk
  dotnet publish

stage 3: aspnet runtime
  copy dotnet publish
  copy React build/client -> frontend directory/wwwroot
```

В финальном image отсутствуют:

- node_modules;
- npm;
- исходники frontend;
- .NET SDK.

---

# 33. Docker build `sensor-gateway`

Final image содержит только .NET runtime + published worker.

Serial device mount:

```yaml
services:
  sensor-gateway:
    devices:
      - /dev/serial/by-id/usb-...:/dev/meshcore
```

В приложении:

```text
MESHSMSO_MESHCORE_DEVICE=/dev/meshcore
```

Никогда не привязываться к `/dev/ttyACM0`, если доступен stable `/dev/serial/by-id/...`.

---

# 34. Compose

Концептуально:

```yaml
services:
  sensor-db:
    image: postgres:...
    restart: unless-stopped
    volumes:
      - sensor-db-data:/var/lib/postgresql/data
    healthcheck: ...

  sensor-web:
    image: ghcr.io/meshsmo/sensors-web:...
    restart: unless-stopped
    depends_on:
      sensor-db:
        condition: service_healthy

  sensor-gateway:
    image: ghcr.io/meshsmo/sensors-gateway:...
    restart: unless-stopped
    depends_on:
      sensor-db:
        condition: service_healthy
    devices:
      - /dev/serial/by-id/...:/dev/meshcore
```

PostgreSQL порт наружу по умолчанию не публиковать.

---

# 35. Конфигурация

Использовать environment variables.

Пример:

```text
ConnectionStrings__Sensors=...
MeshCore__Mode=Http
MeshCore__ReconnectDelaySeconds=5
MeshCore__TelemetryCollectionIntervalSeconds=60
MeshCore__Http__BaseAddress=https://192.168.1.123
MeshCore__Http__AdminPassword=...
MeshCore__Http__AllowInvalidServerCertificate=true
MeshCore__Http__TimeoutSeconds=15
MeshCore__Serial__PortName=/dev/serial/by-id/usb-...
MeshCore__Serial__BaudRate=115200
MeshCore__Serial__CommandTimeoutSeconds=10
LocalTelemetry__DatabasePath=data/gateway-telemetry.db
Gateway__ApiKey=...
Push__ApiUrl=...          # опционально: включает push-доставку телеметрии на основной API
Push__ApiKey=...          # обязателен, если задан Push__ApiUrl
```

sensor-web:

```text
ConnectionStrings__Sensors=...
Gateway__Mode=Pull          # Pull (по умолчанию) | Push
Gateway__BaseUrl=http://sensor-gateway:8080
Gateway__ApiKey=...
Gateway__Ingest__ApiKey=... # обязателен в Gateway__Mode=Push
Gateway__PollIntervalSeconds=15
Polling__MaxConcurrentPolls=1
Public__BaseUrl=https://sensors.meshsmo.ru
```

Секреты:

- `.env` только локально;
- production secrets через deployment environment;
- не печатать connection string в logs.

---

# 36. Health checks

## `sensor-web`

```text
/health/live
/health/ready
```

Ready проверяет:

- приложение запущено;
- PostgreSQL доступен.

## `sensor-gateway`

Liveness не должен падать только потому, что один датчик offline. Слушает только внутреннюю сеть.

```text
/health/live
/health/ready
```

Container restart полезен при зависшем gateway process, но не должен запускать restart loop из-за временного отсутствия LoRa sensor.

---

# 37. Observability

## Logs

JSON structured logs.

Минимальные поля:

```text
service
sensorId
sensorSlug
requestId
pollAttemptId
protocol
durationMs
result
errorCode
```

Не логировать сырой binary payload на `Information`.

Payload dump только на `Debug/Trace` с лимитом размера.

## Metrics

```text
sensor_poll_total
sensor_poll_success_total
sensor_poll_timeout_total
sensor_poll_duration_seconds

meshcore_packets_received_total
meshcore_packets_sent_total
meshcore_reconnect_total

measurements_written_total

http_request_duration_seconds
```

## Traces

OpenTelemetry tracing:

```text
poll sensor
  -> send MeshCore packet
  -> await response
  -> decode
  -> database insert
```

---

# 38. Error taxonomy

Не хранить произвольную строку как единственную форму ошибки.

Начальные codes:

```text
MeshDeviceUnavailable
MeshConnectionLost
MeshSendFailed

PollTimeout
PollCancelled

MalformedPacket
UnsupportedProtocolVersion
UnexpectedMessageType
UnknownRequest

SensorError

DatabaseWriteFailed
```

`error_message` можно хранить дополнительно для диагностики.

---

# 39. Idempotency

Повторный inbound response не должен создавать дубликаты.

Возможные ключи:

```text
(sensor_id, request_id)
```

или protocol-level response ID.

Если sensor protocol допускает повтор request ID после reboot, учитывать временную область/epoch.

Решение обязательно зафиксировать в `docs/protocol.md`.

---

# 40. Retention

Для первой версии **не удалять measurement history автоматически**.

Но предусмотреть конфигурацию:

```text
Poll attempts: 90–180 days
Raw payloads: 30–90 days
Measurements: indefinite
```

Точные сроки принять после понимания объёма.

Raw payload может занимать больше места и нужен главным образом для диагностики.

---

# 41. Security

## Public API

- read-only;
- rate limiting;
- request size limits;
- server-side validation всех query params;
- upper bound временного диапазона/числа points.

## Admin

До появления аутентификации admin endpoints не публиковать.

## Gateway

Не слушает публичный TCP port без необходимости. В push-режиме gateway остаётся без входящих портов: он сам обращается к публичному ingest-эндпоинту `sensor-web` с общим секретом `Push:ApiKey` / `Gateway:Ingest:ApiKey` (см. 7.1.6).

## Database

Доступна только внутренней Docker network.

Желательно отдельные DB users:

```text
sensor_gateway_writer
sensor_web_reader
sensor_migrator
```

На первой версии допустим один application user, но privilege split — целевой вариант.

---

# 42. Backup

Минимум:

- регулярный `pg_dump`;
- retention нескольких копий;
- backup вне Docker volume;
- проверяемая процедура restore.

`docs/operations.md` должен содержать реальную команду восстановления.

Backup без теста restore не считать готовым.

---

# 43. CI

На каждый PR:

```text
dotnet restore
dotnet build
dotnet test

npm ci
npm run typecheck
npm run lint
npm test
npm run build

validate sensor YAML
validate duplicate slug/public key
validate prerender route generation

docker build sensor-web
docker build sensor-gateway
```

Дополнительно:

- dependency vulnerability scan;
- container scan;
- formatting check.

---

# 44. CD

На merge/tag:

1. CI;
2. build immutable images;
3. push:
   - `ghcr.io/meshsmo/sensors-web:<git-sha>`
   - `ghcr.io/meshsmo/sensors-gateway:<git-sha>`
4. optional semver/latest tags;
5. server pull;
6. run DB migrator;
7. update `sensor-web`;
8. health check;
9. update `sensor-gateway`;
10. health check.

Gateway обновлять **после web/migration**, чтобы уменьшить риск остановить сбор данных из-за несовместимой schema.

Миграции должны быть backward-compatible хотя бы на один deployment step.

---

# 45. Testing strategy

## Unit

- state machine;
- scheduler;
- jitter;
- range/resolution selection;
- metric conversion;
- protocol encoder/decoder;
- SEO metadata builder;
- slug validation.

## Protocol golden tests

Фиксированные byte arrays:

```text
known request -> exact bytes
known response bytes -> exact domain result
malformed payload -> expected error
```

## Integration

Через Testcontainers PostgreSQL:

- repositories;
- migrations;
- measurement inserts;
- aggregation queries;
- duplicate handling.

## Fake MeshCore

Обязателен fake transport:

```text
FakeRepeaterClient
```

Сценарии:

- immediate response;
- delayed response;
- timeout;
- duplicate response;
- malformed response;
- out-of-order response;
- disconnect/reconnect.

Это позволит тестировать gateway без реального radio.

## Web

- API integration tests;
- React component tests;
- Playwright e2e.

---

# 46. SEO tests в CI

Автоматически проверять prerendered HTML:

- не пустой `<title>`;
- description;
- canonical;
- ровно один `<h1>`;
- public sensor name присутствует в HTML **без исполнения JS**;
- internal links имеют обычный `<a href>`;
- noindex routes действительно имеют noindex;
- sitemap содержит все indexable sensors;
- hidden/private sensors отсутствуют.

Playwright:

```text
JavaScript disabled
```

и убедиться, что public page всё равно имеет meaningful content.

---

# 47. Search Console / production SEO checklist

После запуска:

- зарегистрировать `sensors.meshsmo.ru` в Google Search Console;
- отправить sitemap;
- проверить URL Inspection;
- посмотреть rendered HTML;
- проверить canonical;
- проверить mobile rendering;
- мониторить Coverage/Indexing;
- не считать Lighthouse SEO score заменой реальной Search Console.

---

# 48. ADR, которые следует создать

## ADR-001 — Bounded context monorepo

Решение: один repo, два deployable .NET apps + React + migrator.

## ADR-002 — Gateway без прямого доступа к БД

Gateway не подключается к PostgreSQL: он пишет только в локальную SQLite outbox и отдаёт snapshots по внутреннему API. `sensor-web` забирает телеметрию из gateway фоновым ingestion-сервисом и пишет в PostgreSQL.

Причина: gateway остаётся изолированным от основного хранилища (меньше поверхности атаки и прав доступа), а надёжность доставки обеспечивает outbox + ack с идемпотентной записью на стороне web.

Пересмотреть при: multiple gateways / заметных накладных расходах pull-модели.

## ADR-003 — No broker in v1

Нет RabbitMQ/NATS/MQTT между gateway и DB.

Пересмотреть при:

- multiple gateways;
- multiple consumers;
- alerts/integrations;
- event replay.

## ADR-004 — React Router prerender + SPA fallback

Нет Node runtime SSR.

Публичные indexable routes pre-rendered.

## ADR-005 — GitOps sensor registry

Sensor metadata и slug version-controlled.

## ADR-006 — Plain PostgreSQL first

Не добавлять TimescaleDB до появления реальной проблемы.

---

# 49. Порядок реализации

## Phase 0 — Bootstrap

### Результат

Репозиторий компилируется, CI зелёный, Docker skeleton работает.

### Tasks

- [x] создать solution;
- [x] создать Domain/Application/Infrastructure;
- [x] создать Gateway Worker;
- [x] создать Web ASP.NET Core;
- [x] создать DbMigrator;
- [x] создать React Router frontend;
- [x] настроить central package management;
- [x] добавить EditorConfig;
- [x] добавить unit test projects;
- [x] добавить compose;
- [x] добавить базовый CI;
- [x] добавить Dependabot/Renovate.

Примечание: Docker/Compose skeleton добавлен, но локально не запускался из-за недоступного Docker на машине разработки.

---

# 50. Phase 1 — Database + registry

### Результат

Sensor registry загружается и синхронизируется с PostgreSQL.

### Tasks

- [x] определить YAML schema;
- [x] validator;
- [x] domain `Sensor`;
- [x] EF mappings;
- [x] initial migration;
- [x] migrator;
- [x] registry sync;
- [x] duplicate slug/public key validation;
- [ ] integration tests;
- [x] добавить первый test sensor.

---

# 51. Phase 2 — Sensor protocol

### Результат

Полностью определён и протестирован binary protocol.

### Tasks

- [ ] versioned envelope;
- [ ] message types;
- [ ] request ID;
- [ ] metric encoding;
- [ ] error frame;
- [ ] endianness;
- [ ] max payload;
- [ ] protocol markdown;
- [ ] encoder;
- [ ] decoder;
- [ ] golden test vectors;
- [ ] compatibility rules будущих версий.

---

# 52. Phase 3 — MeshCoreTel Repeater gateway spike

### Результат

Console/Worker может:

```text
connect -> command/stats -> local SQLite -> reconnect
```

с реальным MeshCoreTel Repeater.

### Tasks

- [x] HTTP configuration/client;
- [x] HTTP session and `401` reauthentication;
- [x] serial configuration/client;
- [x] stable device path configuration;
- [x] serial CLI response parsing;
- [x] reconnect;
- [x] cancellation;
- [x] local SQLite outbox;
- [x] fake transport test;
- [ ] integration test с реальным hardware вручную;
- [ ] записать hardware setup в `operations.md`.

Не писать scheduler, пока transport не доказан отдельно.

---

# 53. Phase 4 — Polling MVP

### Результат

Один реальный датчик регулярно опрашивается, measurements появляются в PostgreSQL.
Реализовано: опрос идёт через acquisition API прошивки
(`POST /api/request`, контракт — `docs/repeater-firmware-acquisition-spec.md`), protocol_id =
`meshcore-req-lpp`: REQ = `timestamp(4 LE) + 0x03 + 0x00`, ответ = `timestamp(4) + Cayenne LPP`.

### Tasks

- [x] priority queue scheduler (due-time priority queue, MaxConcurrentPolls = 1, deterministic jitter);
- [x] deterministic jitter;
- [x] single concurrent request;
- [x] timeout (на попытку: pollTimeout из registry, fallback SensorPolling:RequestTimeoutMs);
- [x] retry (до pollMaxAttempts на цикл, clamped 1..3; randomized backoff SensorPolling:RetryBackoff{Min,Max}Ms; timeout/transport-ошибки ретраятся, пустой/нерасшифруемый ответ — нет, детерминированный);
- [x] request correlation (request_id = монотонный счётчик от unix time; идемпотентность через unique (sensor_id, request_id));
- [x] poll attempts (каждая попытка пишется gateway в SQLite outbox (`sensor_poll` с attemptNumber/startedAt либо `poll_attempt`), web-импортер кладёт в poll_attempts; идемпотентность — unique (sensor_id, request_id, attempt_number)); неудачные попытки двигают sensor_status (Degraded/Offline, пороги Gateway:Degraded/OfflineAfterFailures);
- [x] measurement persistence;
- [x] graceful shutdown;
- [x] gateway health (/health/ready = SQLite outbox writable + YAML registry; недоступность LoRa/репитера не влияет, спека §36);
- [x] structured logs;
- [x] fake scenarios (fake-транспорт: retry после timeout, исчерпание попыток, undecodable body — в SensorTelemetryPollerTests).

Это первый end-to-end milestone.

---

# 54. Phase 5 — BFF API

### Результат

Все данные доступны через стабильный `/api/v1`.

### Tasks

- [x] sensor list;
- [x] sensor detail;
- [x] latest;
- [x] status;
- [x] historical query (`GET /api/v1/sensors/{slug}/measurements?metric&from&to&resolution`, ответ по §14.2);
- [x] resolution=auto (≤6h → raw; ≤24h → 5m; ≤7d → 15m; ≤31d → 1h; ≤180d → 6h; >180d → 1d; диапазон ≤400 дней);
- [x] aggregation (min/avg/max/count в UTC-aligned бакетах; PG date_bin, SQLite strftime — для тестов);
- [x] dashboard aggregate endpoint;
- [x] validation (400 ValidationError: metric/from/to/resolution, бюджет точек; 404 на неизвестный/скрытый датчик);
- [x] rate limits (per-IP fixed window 120 req/min на /api/v1/sensors/* и /dashboard, политика `public-api`);
- [x] OpenAPI (`/openapi/v1.json`, Microsoft.AspNetCore.OpenApi);
- [x] integration tests (SQLite-провайдер: бакеты, raw, фильтр по метрике, validation; rate limit + OpenAPI smoke).

---

# 55. Phase 6 — Frontend shell + SEO

### Результат

`/`, `/sensors`, `/sensors/:slug` содержат полезный HTML до выполнения JavaScript.

### Tasks

- [x] React Router Framework Mode;
- [x] `ssr:false`;
- [x] prerender;
- [x] sensor routes from registry;
- [x] SPA fallback;
- [x] route metadata;
- [x] canonical;
- [x] OpenGraph;
- [x] responsive layout;
- [x] semantic navigation;
- [x] sitemap;
- [x] robots.txt;
- [x] 404 behavior;
- [x] CI SEO assertions;
- [x] JavaScript-disabled e2e test.

Это второй обязательный release gate.

---

# 56. Phase 7 — Dashboard

### Tasks

- [ ] overview cards;
- [ ] sensor table/cards;
- [ ] status badges;
- [ ] latest values;
- [ ] loading/error/empty states;
- [ ] mobile layout;
- [ ] query caching;
- [ ] frontend polling.

---

# 57. Phase 8 — Sensor charts

### Tasks

- [ ] chart library spike;
- [ ] temperature chart;
- [ ] humidity;
- [ ] pressure;
- [ ] battery;
- [ ] time range selector;
- [ ] URL-synced query params;
- [ ] min/avg/max;
- [ ] tooltip timezone;
- [ ] missing-data visualization;
- [ ] chart accessibility summary;
- [ ] performance test на максимальном разрешённом dataset.

---

# 58. Phase 9 — Operations

### Tasks

- [ ] production compose;
- [ ] health checks;
- [ ] image publishing;
- [ ] migrations in deployment;
- [ ] backups;
- [ ] restore docs;
- [ ] OpenTelemetry;
- [ ] metrics;
- [ ] log rotation/storage;
- [ ] deployment runbook;
- [ ] restart/reconnect tests.

---

# 59. Phase 10 — Release hardening

### Tasks

- [ ] security headers;
- [ ] CSP;
- [ ] rate limiting;
- [ ] dependency scan;
- [ ] container scan;
- [ ] backup restore drill;
- [ ] hardware disconnect test;
- [ ] DB restart test;
- [ ] web restart while gateway continues;
- [ ] gateway restart;
- [ ] duplicate frame test;
- [ ] long-running soak test;
- [ ] Search Console;
- [ ] sitemap submitted;
- [ ] final docs.

---

# 60. MVP definition

MVP считается готовым, если:

1. один или больше реальных датчиков описаны в registry;
2. gateway после запуска самостоятельно соединяется с MeshCoreTel Repeater;
3. датчики регулярно опрашиваются;
4. timeout одного датчика не ломает polling остальных;
5. успешные measurements пишутся в PostgreSQL;
6. Web показывает sensors и latest measurements;
7. есть минимум один historical graph;
8. `/sensors/:slug` открывается напрямую после browser refresh;
9. публичная страница содержит meaningful HTML при отключённом JS;
10. title/description/canonical индивидуальны;
11. sitemap содержит датчики;
12. web restart не прерывает gateway polling;
13. serial reconnect не требует ручного рестарта контейнера;
14. CI проверяет .NET, frontend и Docker builds;
15. есть рабочий backup/restore procedure.

---

# 61. Что НЕ входит в v1

Осознанно не делать в первой версии:

- RabbitMQ;
- NATS;
- Kafka;
- отдельный frontend container;
- runtime Node SSR;
- Kubernetes;
- TimescaleDB без необходимости;
- Redis без измеренной необходимости;
- GraphQL;
- WebSocket;
- отдельный auth service;
- сложный admin panel;
- автоматическое discovery неизвестных sensors;
- alerting engine;
- Home Assistant integration;
- public write API.

Это потенциальные v2+, но не prerequisities.

---

# 62. Возможные v2

После стабильного MVP:

- SSE live updates;
- alerts;
- Telegram notifications;
- multiple physical MeshCore gateways;
- gateway leader/ownership model;
- route selection diagnostics;
- public map of sensors;
- weather summaries;
- data export CSV/JSON;
- Prometheus/Grafana;
- admin UI;
- user-defined dashboards;
- MQTT bridge;
- Home Assistant;
- long-term aggregation;
- automatic anomaly detection.

---

# 63. Критические технические риски

## Risk A — Repeater transport semantics

HTTP и serial CLI репитера не умеют инициировать произвольный Companion binary request к удалённому pull-only sensor.

**Mitigation:** сначала подтвердить acquisition path на реальном железе; при необходимости добавить endpoint в прошивку либо отдельный request-capable radio.

## Risk B — LoRa airtime

Слишком частый polling или retries ухудшат сеть.

**Mitigation:** concurrency=1, jitter, conservative intervals, metrics.

## Risk C — Sensor clocks

RTC датчиков может быть ненадёжным.

**Mitigation:** всегда хранить `receivedAt`, `measuredAt` считать дополнительным.

## Risk D — Thin SEO pages

Автоматическая генерация большого числа страниц с минимальным уникальным текстом не даёт качественного SEO.

**Mitigation:** indexable только реальные публичные sensors с полезным описанием и содержимым.

## Risk E — SPA fallback masking 404

Неправильный fallback может отдавать HTTP 200 на несуществующие URL.

**Mitigation:** route-aware handling public sensor slugs + explicit API/static 404.

## Risk F — Schema incompatibility during deploy

Gateway и Web могут несколько секунд работать на разных версиях.

**Mitigation:** expand/contract migrations и backward-compatible rolling step.

---

# 64. Первый рекомендуемый implementation slice

Не начинать с красивого dashboard.

Первый vertical slice:

```text
real sensor
    |
    v
MeshCoreTel Repeater
    |
    v
Gateway
    |
    v
PostgreSQL
    |
    v
GET /api/v1/sensors/:slug/latest
    |
    v
prerendered /sensors/:slug
    |
    v
React displays latest value
```

То есть первая feature должна доказать **полный путь данных**, а не отдельные слои.

После этого масштабировать:

```text
1 sensor
-> many sensors
-> history
-> graphs
-> polish
-> realtime
```

---

# 65. Рекомендуемый первый backlog

Порядок первых issue:

```text
SENS-001 Bootstrap .NET solution
SENS-002 Bootstrap React Router app
SENS-003 Add PostgreSQL compose
SENS-004 Define sensor registry schema
SENS-005 Create initial EF schema
SENS-006 Implement DbMigrator
SENS-007 Define sensor binary protocol v1
SENS-008 Add protocol golden tests
SENS-009 Implement IRepeaterClient
SENS-010 Implement fake repeater client
SENS-011 MeshCoreTel Repeater hardware spike
SENS-012 Implement request correlation
SENS-013 Implement poll scheduler
SENS-014 Persist measurements
SENS-015 Implement sensor latest API
SENS-016 Implement public sensor route
SENS-017 Add React Router prerender
SENS-018 Add metadata/canonical
SENS-019 Add sitemap/robots
SENS-020 Add first time-series chart
SENS-021 Add CI SEO checks
SENS-022 Build production Docker images
SENS-023 Add CD
SENS-024 Add backup/restore runbook
```

---

# 66. Acceptance criteria для архитектуры

Архитектура считается выдержанной, если следующие утверждения остаются истинны:

```text
BFF не знает, как устроен MeshCore serial protocol.

Frontend не знает, что данные пришли через LoRa.

Gateway не зависит от availability BFF.

Frontend production не требует Node runtime.

PostgreSQL можно backup/restore независимо от приложений.

Добавление нового sensor protocol не требует переписывать scheduler.

Добавление новой metric не требует менять frontend routing.

Public route можно проиндексировать без обязательного исполнения JavaScript.

Смена chart library не затрагивает API.

Перезапуск web не прерывает сбор данных.
```

---

# 67. Официальные references, использованные при выборе rendering strategy

- React Router — Pre-Rendering:  
  https://reactrouter.com/how-to/pre-rendering
- React Router — SPA Mode:  
  https://reactrouter.com/how-to/spa
- React Router — Rendering Strategies:  
  https://reactrouter.com/start/framework/rendering
- Google Search Central — JavaScript SEO:  
  https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics
- Google Search Central — URL structure:  
  https://developers.google.com/search/docs/crawling-indexing/url-structure
- Google Search Central — Canonical URLs:  
  https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls
- .NET support policy:  
  https://dotnet.microsoft.com/platform/support/policy

---

# 68. Итоговое архитектурное решение

Для MeshSMO Sensors принять:

```text
Runtime:
  1. sensor-gateway
  2. sensor-web
  3. sensor-db

Build-time:
  Node + React Router/Vite
  .NET SDK

Frontend rendering:
  prerender indexable public routes
  hydrate into SPA
  SPA fallback for non-prerendered app routes
  no Node SSR runtime

Storage:
  PostgreSQL (только sensor-web)

Gateway storage:
  SQLite outbox, выдаётся sensor-web по внутреннему HTTP API

Sensor registry:
  GitOps YAML

Transport:
  MeshCoreTel Repeater HTTP/Serial behind IRepeaterClient

Sensor payload:
  versioned binary protocol

Data flow:
  Sensor -> LoRa -> MeshCore -> Gateway (SQLite outbox)
        -> internal API -> sensor-web (ingestion, PostgreSQL)
        -> BFF -> React
```

Главный принцип реализации: **сначала надёжность acquisition pipeline, затем API, затем визуализация; SEO строится в rendering architecture с самого начала, а не добавляется react-helmet'ом в конце.**
