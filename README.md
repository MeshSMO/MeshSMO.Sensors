# MeshSMO Sensors

Сервис сбора и публикации показаний pull-only датчиков MeshSMO через MeshCore/LoRa.

Сейчас реализован стартовый срез:

- solution на .NET 10 с границами Domain / Application / Infrastructure;
- Worker gateway и ASP.NET Core BFF;
- PostgreSQL-модель и initial EF Core migration;
- отдельный one-shot DbMigrator;
- GitOps-реестр YAML с валидацией и синхронизацией;
- React Router Framework Mode как JSPS `.esproj`, связанный с BFF, с `ssr: false`, prerender главной страницы и SPA fallback;
- health endpoints `/health/live` и `/health/ready`;
- HTTP и USB Serial режимы Gateway для MeshCoreTel Repeater;
- локальная SQLite outbox с исходными snapshots и индексируемыми показаниями;
- unit tests и CI skeleton.

Бинарный sensor protocol, публикация накопленных данных в PostgreSQL и публичный API — следующие фазы. Gateway уже умеет собирать телеметрию MeshCoreTel Repeater через HTTPS или USB Serial CLI и сохранять её локально. Тестовый датчик в `config/sensors` намеренно выключен и содержит placeholder public key.

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

`dotnet publish src/MeshSMO.Sensors.Web` также собирает frontend и включает `build/client` в `wwwroot`; Node.js в production runtime не нужен.

Отдельные frontend-команды по-прежнему доступны из `src/web` для быстрых проверок `npm run typecheck`, `npm test` и `npm run build`.

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
