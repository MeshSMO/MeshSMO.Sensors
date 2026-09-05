# MeshSMO Sensors

Сервис сбора и публикации показаний pull-only датчиков MeshSMO через MeshCore/LoRa.

Сейчас реализован стартовый срез:

- solution на .NET 10 с границами Domain / Application / Infrastructure;
- Worker gateway и ASP.NET Core BFF;
- PostgreSQL-модель и initial EF Core migration;
- отдельный one-shot DbMigrator;
- GitOps-реестр YAML с валидацией и синхронизацией;
- React Router Framework Mode с `ssr: false`, prerender главной страницы и SPA fallback;
- health endpoints `/health/live` и `/health/ready`;
- unit tests и CI skeleton.

MeshCore serial transport, binary protocol, polling и публичный API — следующие фазы. Тестовый датчик в `config/sensors` намеренно выключен и содержит placeholder public key.

## Локальная проверка без Docker

```powershell
dotnet tool restore
dotnet restore MeshSMO.Sensors.slnx
dotnet build MeshSMO.Sensors.slnx --no-restore
dotnet test MeshSMO.Sensors.slnx --no-build
dotnet run --project src/MeshSMO.Sensors.DbMigrator -- --validate-registry

Set-Location src/web
npm ci
npm run typecheck
npm test
npm run build
```

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
MeshCore__Device=/dev/meshcore
MeshCore__ReconnectDelay=5s
Polling__MaxConcurrentPolls=1
Public__BaseUrl=https://sensors.meshsmo.ru
```

Docker/Compose-файлы подготовлены в `deploy`, но для этого стартового среза локально не запускались и не проверялись.
