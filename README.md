# MeshSMO Sensors

Сервис сбора и публикации показаний pull-only датчиков MeshSMO через MeshCore/LoRa.

Реализовано и проверено на железе:

- gateway собирает телеметрию MeshCoreTel Repeater (HTTPS или USB Serial CLI) и опрашивает pull-only датчики через acquisition API прошивки (`POST /api/request` + ANON-логин), ответы — Cayenne LPP с маппингом каналов из реестра;
- локальная SQLite outbox в gateway; sensor-web выгружает её в PostgreSQL (ack + идемпотентность);
- PostgreSQL-модель (sensors, measurements, gateway telemetry), one-shot DbMigrator;
- реестр датчиков в YAML (deployment-local, в git не хранится) с валидацией, синхронизацией и prerender-маршрутами;
- публичный `/api/v1` (sensors, latest, status, dashboard), sitemap.xml, robots.txt;
- React Router Framework Mode с `ssr: false`, prerender и SPA fallback;
- полный стек в Docker (`deploy/compose.yaml`), CI на GitHub Actions.

> **Для агентов и новых разработчиков:** начни с [AGENTS.md](./AGENTS.md) — карта репозитория, команды и найденные грабли.

## Документация

| Файл | О чём |
|---|---|
| [AGENTS.md](./AGENTS.md) | точка входа: структура, команды, грабли, статус |
| [ARCHITECTURE.md](./ARCHITECTURE.md) | компоненты, потоки данных, решения, ограничения |
| [docs/protocol.md](./docs/protocol.md) | wire-протоколы: REQ/ANON, Cayenne LPP, payload'ы outbox |
| [docs/MeshSMO-Sensors-IMPLEMENTATION_SPEC.md](./docs/MeshSMO-Sensors-IMPLEMENTATION_SPEC.md) | полная спека и план фаз |
| [docs/repeater-firmware-acquisition-spec.md](./docs/repeater-firmware-acquisition-spec.md) | контракт прошивки репитера (acquisition) |

## Локальная проверка без Docker

```powershell
dotnet tool restore
dotnet restore MeshSMO.Sensors.slnx
# Собирает одновременно .NET и frontend через ProjectReference на .esproj
dotnet build MeshSMO.Sensors.slnx --no-restore
dotnet test MeshSMO.Sensors.slnx --no-build
dotnet run --project src/MeshSMO.Sensors.DbMigrator -- --validate-registry
```

Для разработки BFF и Vite запускаются одной командой. SPA proxy поднимет frontend на `http://localhost:5173`, а Vite проксирует `/api` и `/health` в BFF:

```powershell
dotnet run --project src/MeshSMO.Sensors.Web --launch-profile http
```

`dotnet publish src/MeshSMO.Sensors.Web` также собирает frontend и включает `.output/public` в `wwwroot`; Node.js в production runtime не нужен.

Отдельные frontend-команды по-прежнему доступны из `src/web` для быстрых проверок `npm run lint`, `npm run typecheck` и `npm run build`.

## Gateway и MeshCoreTel Repeater

Поддерживаются два прямых интерфейса репитера:

- `Http` — [HTTPS API MeshCoreTel-firmware](https://vbart.github.io/MeshCoreTel-firmware/api/): `/login`, `X-Auth-Token`, `/api/command` и `/api/stats`;
- `Serial` — USB/UART CLI репитера со скоростью `115200` по умолчанию.

Запросы к устройству выполняются последовательно. Gateway переподключается после ошибок и собирает данные раз в минуту: HTTP сохраняет полный `/api/stats`, Serial — результаты `stats-core`, `stats-radio`, `stats-packets` и постраничный `sensor list`.

HTTP:

```powershell
$env:MeshCore__Mode = "Http"
$env:MeshCore__Http__BaseAddress = "https://192.168.1.123"
$env:MeshCore__Http__AdminPassword = "your-admin-password"
$env:MeshCore__Http__AllowInvalidServerCertificate = "true"
dotnet run --project src/MeshSMO.Sensors.Gateway
```

`AllowInvalidServerCertificate=true` нужен для штатного self-signed сертификата прошивки. Использовать этот режим следует только в доверенной локальной сети; API устройства не нужно публиковать в интернет.

Serial:

```powershell
$env:MeshCore__Mode = "Serial"
$env:MeshCore__Serial__PortName = "COM4" # Linux: /dev/serial/by-id/usb-...
$env:MeshCore__Serial__BaudRate = "115200"
dotnet run --project src/MeshSMO.Sensors.Gateway
```

Показания сохраняются в `data/gateway-telemetry.db`. Таблица `telemetry_snapshots` содержит исходный JSON каждого опроса, а `telemetry_readings` — развёрнутые значения с ключами вроде `core.battery_mv` и `sensors.temperature`. Записи остаются в outbox до явного `AcknowledgeAsync`, поэтому рестарт Gateway их не теряет.

MQTT прошивки сейчас является исходящим uplink в настроенные MeshCoreTel/LetsMesh брокеры, а не прямым локальным command transport. Поэтому отдельного режима `Mqtt` в Gateway нет.

## Миграции и синхронизация реестра

DbMigrator требует доступную PostgreSQL и connection string из окружения:

```powershell
$env:ConnectionStrings__Sensors = "Host=localhost;Port=5432;Database=meshsmo_sensors;Username=meshsmo;Password=..."
dotnet run --project src/MeshSMO.Sensors.DbMigrator
```

Обычные Web и Gateway процессы миграции не запускают. Новую миграцию создавать так:

```powershell
dotnet ef migrations add MigrationName `
  --project src/MeshSMO.Sensors.Infrastructure `
  --startup-project src/MeshSMO.Sensors.Infrastructure `
  --context SensorsDbContext `
  --output-dir Persistence/Migrations
```

## CI и релизы

GitHub Actions (`.github/workflows`):

- **CI** (`ci.yml`) — на каждый push в `main`/`master` и на PR: параллельно .NET build + unit/protocol тесты + валидация реестра, frontend typecheck/test/build и сборка трёх Docker-образов. Каждый push в основную ветку публикует rolling-образы `ghcr.io/meshsmo/meshsmo-sensors-{web,gateway,dbmigrator}` с тегами `<ветка>` и `sha-<hash>`.
- **Release** (`release.yml`) — на push тега `v*.*.*`: прогоняет тот же CI как quality gate, публикует версионированные образы (`1.2.3`, `1.2`, `1`, `latest`) и создаёт GitHub Release с автосгенерированными notes. `workflow_dispatch` без тега переопубликует только `latest`.

Порядок релиза:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Запуск стека из опубликованных образов вместо локальной сборки (нужен `docker login ghcr.io` для приватных пакетов):

```bash
docker compose -f deploy/compose.yaml -f deploy/compose.registry.yaml up -d
```

Префикс и тег образов переопределяются переменными `MESHSMO_IMAGE_PREFIX` и `MESHSMO_IMAGE_TAG`.

## Конфигурация

Основные переменные окружения:

```text
ConnectionStrings__Sensors=...
Registry__Directory=config/sensors
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
Polling__MaxConcurrentPolls=1
Public__BaseUrl=https://sensors.meshsmo.ru
```

Docker/Compose-файлы подготовлены в `deploy`, но для этого стартового среза локально не запускались и не проверялись.
