# ARCHITECTURE

Архитектура MeshSMO Sensors. Нормативный документ — `docs/MeshSMO-Sensors-IMPLEMENTATION_SPEC.md`; здесь — фактическое состояние и обоснования.

## 1. Компоненты и границы

```text
                     sensors.meshsmo.ru (prod) / localhost:8080 (dev-docker)
                                      |
                              +---------------+
                              |  sensor-web   |  ASP.NET Core BFF + TanStack Start SPA static/prerender
                              |  (Kestrel)    |  PostgreSQL <-- единственный владелец БД
                              +---+-------^---+
                        internal |       |  internal HTTP
                        HTTP     |       |  GET /api/telemetry/pending
                                 v       |  POST /api/telemetry/ack
                      +---------------------+
                      |   sensor-gateway    |  .NET Worker + Kestrel (внутренний порт 8080)
                      |  SQLite outbox      |  БД НЕ доступна (принципиально)
                      +--+---------------+--+
              HTTPS /api/request |        | USB serial (опционально)
                                 v        v
                        MeshCoreTel Repeater (ESP32, кастомная прошивка vbart-meshcoretel)
                                 |
                            LoRa (MeshCore)
                                 |
                        Sensor nodes (pull-only, отвечают на REQ телеметрией Cayenne LPP)
```

Плюс **sensor-migrator** (one-shot: EF-миграции + sync registry) и **sensor-db** (PostgreSQL, не публикуется наружу).

Доставка outbox → PostgreSQL — два режима (спека §7.1.5/§7.1.6): **pull** (по умолчанию; web опрашивает gateway по `GET /api/telemetry/pending`) или **push** (`Gateway:Mode=Push` у web + `Push:ApiUrl` у gateway; gateway сам постит батчи на `POST /api/telemetry/ingest` web'а с общим секретом `X-Api-Key`). Контракт payload'ов и идемпотентность у режимов идентичны; push позволяет разнести gateway и web по серверам, не публикуя порты gateway.

Принципы границ (см. спека §5, §66):

- **BFF не знает про MeshCore**: ни serial, ни пакетный формат не протекают в `Web`. Web оперирует снапшотами outbox.
- **Gateway не знает про PostgreSQL**: он пишет только в локальную SQLite и отдаёт данные по API. Web может быть недоступен/перезапускаться — сбор продолжается.
- **Frontend не знает про LoRa**: только `/api/v1/*`.
- **Forecasting не меняет телеметрию**: отдельная библиотека ML.NET читает один числовой ряд `(sensor_id, metric_key)` через BFF, строит результат on demand и ничего не записывает в PostgreSQL.
- Registry — deployment-local (`config/sensors/*.yaml` gitignored: могут содержать чувствительные данные). В git — только `schema.json` и сгенерированный публичный снапшот `src/web/src/generated/sensorRegistry.json`. Локальные YAML — источник истины для синхронизации таблицы `sensors` и генерации prerender-маршрутов.

## 2. Потоки данных

### 2.1. Телеметрия самого репитера

`Worker` (BackgroundService), **opt-in и по умолчанию выключен** (`MeshCore:TelemetryCollectionEnabled`, compose-переменная `MESHCORE_TELEMETRY_COLLECTION_ENABLED`; сбор не влияет на опрос датчиков — панельная сессия логинится лениво): connect → `ver` handshake → раз в `MeshCore:TelemetryCollectionIntervalSeconds` → `GetTelemetryAsync` (HTTP `GET /api/stats` либо serial CLI `stats-core/radio/packets` + `sensor list`) → `ILocalTelemetryStore.AppendAsync` (только payload JSON; плоские readings из SQLite и HTTP-контракта убраны 2026-09-06 — они никем не читались и дублировали payload) → ack-цикл web'а выгружает. Выключено осознанно: сырой статус панели репитера (core.*, archive.*, конфиги, events) никто в web не потребляет, а он раздувает landing-таблицы.

### 2.2. Опрос датчиков (основной продуктовый путь)

`SensorTelemetryPoller` (BackgroundService в gateway):

1. Загружает включённые датчики из GitOps-registry (`ISensorRegistry`).
2. Последовательно (concurrency = 1, deterministic jitter по slug) для каждого датчика по его `polling.interval`:
   - строит REQ: `timestamp(4 LE) + 0x03 + 0x00` (MeshCore `GET_TELEMETRY_DATA`);
   - отправляет через настроенный канал `IMeshNodeClient`: по умолчанию `POST /api/request` репитера (см. `docs/repeater-firmware-acquisition-spec.md`); альтернатива — стоковый компаньон (`MeshCore:Mode=Companion`, companion frame protocol по TCP:5000, `CMD_SEND_BINARY_REQ` — тот же LoRa-wire; без RSSI/SNR, wire-timestamp генерирует прошивка компаньона);
   - при первом таймауте ноды — однократный ANON-логин bootstrap (`POST /api/login`; пароль: `mesh.loginPassword` датчика из registry — поддерживает `${VAR}`-подстановку из env, пустая строка = у ноды нет пароля, иначе общий `SensorPolling:LoginPassword`);
   - ответ: `timestamp(4) + Cayenne LPP` → `CayenneLppDecoder` → маппинг каналов из registry (`TelemetryChannelMapping`: `(channel, type|*) → metric, displayName, unit`) → значения;
   - payload `{type:"sensor_poll", sensor, requestId, rssi, snr, responseHex, readings:[{metric,value,unit}]}` → в outbox.

Wire-детали: [docs/protocol.md](./docs/protocol.md).

### 2.3. Ingestion в PostgreSQL

Доставка из outbox — pull или push (см. §1); запись в БД общая — `GatewayTelemetryImporter` (в web, `GatewayIngestion/`):

- pull: `GatewayIngestionWorker` раз в `Gateway:PollIntervalSeconds` → `GET /api/telemetry/pending?maxCount=N` у gateway;
- push: `TelemetryPushWorker` (в gateway) раз в `Push:IntervalSeconds` постит батч на `POST /api/telemetry/ingest` web'а; web хэндлер делает тот же импорт и отвечает 2xx, после чего gateway ack-ает (удаляет) снапшоты у себя.

Атомарно в одном SaveChanges:

- `gateway_telemetry_snapshots` (raw архив payload_json, идемпотентно по unique `(gateway_id, gateway_snapshot_id)`);
- для `sensor_poll`-payload: `measurement_samples` + `measurement_values` (идемпотентно по unique `(sensor_id, request_id)`), `unit` из payload;
- upsert `sensor_status` → `Online` + RSSI/SNR.

Ack — только после коммита транзакции (pull: `POST /api/telemetry/ack`; push: локальное удаление после 2xx). Сбой на любом шаге = повторная доставка без дублей.

### 2.4. Чтение (BFF)

`/api/v1/sensors`, `/sensors/{slug}`, `/sensors/{slug}/status` (материализованный `sensor_status`, фолбэк `Unknown`), `/sensors/{slug}/latest` (последние значения с `displayName`/`unit` из `sensor_metrics`), `/dashboard` (агрегат одним payload'ом), `/sitemap.xml`. Больше нет ничего — фронт живёт на этих эндпоинтах.

### 2.5. Прогнозирование

`GET /api/v1/sensors/{slug}/forecast?metric=...&horizon=1h|6h|12h|24h` читает только числовые значения выбранной пары датчик/метрика. PostgreSQL агрегирует историю в равномерные UTC-buckets; `MeshSMO.Sensors.Forecasting` проверяет полноту ряда, выполняет rolling backtest SSA против last-value и seasonal-naive baseline и возвращает прогноз только после quality gate. Готовый ответ кратковременно кешируется в памяти BFF; single-flight и глобальный semaphore ограничивают CPU. Прогнозные точки и модели в БД не сохраняются. Подробный контракт: [docs/sensor-forecasting-spec.md](./docs/sensor-forecasting-spec.md).

## 3. Модель данных (PostgreSQL)

| Таблица | Назначение | Ключевые ограничения |
|---|---|---|
| `sensors` | реестр (sync из YAML) | unique slug, unique mesh_public_key |
| `sensor_metrics` | состав метрик + presentation metadata | PK (sensor_id, metric_key); `display_name`, `unit` |
| `measurement_samples` | один успешный ответ датчика | unique `(sensor_id, request_id)`; rssi/snr/raw_payload |
| `measurement_values` | значения по метрикам | PK (sample_id, metric_key); unit; индекс под графики |
| `poll_attempts` | диагастика попыток (схема есть, не заполняется) | — |
| `sensor_status` | материализованный статус для дашборда | 1:1 к sensor |
| `gateway_telemetry_snapshots` | raw-архив outbox gateway (payload_json; таблица плоских readings дропнута 2026-09-06) | unique `(gateway_id, gateway_snapshot_id)` |

Миграции — только через `DbMigrator` (`deploy/compose.yaml` запускает его до web/gateway). Никакого `Database.Migrate()` в runtime-сервисах.

## 4. Deployment

- `deploy/compose.yaml`: `sensor-db` → `sensor-migrator` (one-shot) → `sensor-web` (:8080 наружу) + `sensor-gateway` (без published-порта, volume `/app/data` для SQLite, ro-mount `config/`).
- Конфигурация только env (12-factor): см. `deploy/env.example`. Секреты — в `deploy/.env` (не в git).
- Сборки: `deploy/Dockerfile.{web,gateway,dbmigrator}`; web — node-стадия собирает фронт, dotnet-стадия публикует BFF c `/p:SkipFrontendBuild=true`.
- CI (`.github/workflows/ci.yml`): dotnet restore/build/test, валидация registry, npm typecheck/lint/test/build, docker build трёх образов. CD пока нет.

## 5. Ключевые решения (мини-ADR)

| # | Решение | Почему |
|---|---|---|
| 1 | Monorepo, bounded context | один домен, атомарные изменения контрактов |
| 2 | Gateway без PostgreSQL; доставка из outbox — pull (внутренний API) или push (gateway сам шлёт на web) | изоляция радиочасти от БД; ack/idempotency дают ровно-однажды запись; push позволяет раздельный деплой без published-портов gateway |
| 3 | SQLite outbox в gateway | переживает рестарты, простая эксплуатация, no-broker |
| 4 | Опрос нод через acquisition API прошивки (REQ + ANON login), не через свой radio | Risk A из спеки решён кастомной прошивкой репитера; gateway не держит радио |
| 5 | Cayenne LPP как формат ответов датчиков | стандарт MeshCore; свой бинарный envelope (Phase 2) отложен до собственной прошивки датчиков |
| 6 | Registry-маппинг каналов (`telemetry.channels`) в YAML | канал ≠ смысл; имена/юниты/отображение — версионируются в Git, а не в БД |
| 7 | TanStack Start в SPA-режиме (без SSR) + prerender статикой из registry + JSON-снапшот на prebuild | SEO без Node-SSR runtime; reproducible builds |
| 8 | TLS 1.2 + static-RSA cipher pinning в HTTP-клиенте репитера | ESP32-firmware не поднимает TLS 1.3/ECDHE; из Linux-контейнеров иначе не подключиться |
| 9 | ML.NET SSA per `(sensor_id, metric_key)`, on demand, без DB persistence | разные датчики и физические величины имеют разные ряды; backtest и MASE не позволяют выдавать слабую модель за полезный прогноз |

## 6. Известные ограничения / что дальше

- Нет retry-окна внутри опроса, `poll_attempts` не заполняется, нет gateway health-check-компонентов (спека §53, §36).
- Нет historical API (`/measurements` + downsampling, спека §14.2, §15) и графиков.
- Нет OTel/metrics (только JSON console logs), нет CD в ghcr, нет backup/runbook (спека §37–42, §58–59).
- Фронт будет переделан по `docs/frontend-redesign-prompt.md` — не вкладывайся в текущую вёрстку.
