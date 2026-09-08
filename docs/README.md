# Документация MeshSMO Sensors

Структура: **спеки** (`specs/` — что строим: требования, планы, чекбоксы фаз) и **справочники** (`reference/` — как устроено: wire-форматы, исторические документы). Корневые документы репозитория — `README.md` (старт), `AGENTS.md` (для агентов и новых разработчиков), `ARCHITECTURE.md` (фактическая архитектура), `CODESTYLE.md` (правила кодстайла), `SECURITY.md` (политика безопасности).

## Спецификации (`specs/`)

| Документ | Статус | О чём |
|---|---|---|
| [implementation-spec.md](./specs/implementation-spec.md) | план; фазы 0, 1, 3–8 реализованы | Главная спека: требования, технологический стек, границы компонентов, модель данных, SEO-стратегия, конфигурация, план фаз (§49–59 с чекбоксами), MVP (§60). В шапке — свод «план vs факт» |
| [repeater-acquisition-spec.md](./specs/repeater-acquisition-spec.md) | реализован, проверен на железе (2026-09-06) | Контракт acquisition-пути прошивки репитера MeshCoreTel: `POST /api/request`, `POST /api/login` (ANON-bootstrap), CLI `req`/`login`, таймауты и flood-retry |
| [forecasting-spec.md](./specs/forecasting-spec.md) | F1–F3 слиты в master; фича за флагом `Forecasting:Enabled` | Прогнозирование показаний: ML.NET SSA, подготовка ряда, rolling backtest и quality gates, публичный API `/forecast`, план F0–F4 |

## Справочники (`reference/`)

| Документ | О чём |
|---|---|
| [wire-protocol.md](./reference/wire-protocol.md) | Wire-форматы: транспорт gateway↔репитер, mesh-уровень REQ/ANON_REQ, Cayenne LPP, маппинг каналов, формат payload'ов outbox, ошибки acquisition |
| [frontend-redesign-prompt.md](./reference/frontend-redesign-prompt.md) | Исторический промпт (2026-09), по которому фронт переписывался на TanStack Start; контракты API в нём актуальны |

## Что читать по вопросу

- **Первый раз в репозитории** → корневой [README.md](../README.md), затем [AGENTS.md](../AGENTS.md).
- **Как всё устроено и почему** → [ARCHITECTURE.md](../ARCHITECTURE.md); нормативная детализация — `implementation-spec.md` (соответствующий §).
- **Правки C#-кода** → сначала [CODESTYLE.md](../CODESTYLE.md): любой warning ломает сборку.
- **Wire-обмен с репитером/датчиками, payload'ы outbox** → [wire-protocol.md](./reference/wire-protocol.md); контракт прошивки — [repeater-acquisition-spec.md](./specs/repeater-acquisition-spec.md).
- **Новый тип LPP-телеметрии** → `wire-protocol.md` §4–5 + `CayenneLppDecoder` + `TelemetryTypes.KnownTypes` + golden-тест.
- **Прогнозы** → [forecasting-spec.md](./specs/forecasting-spec.md).
- **Реестр датчиков (YAML)** → `implementation-spec.md` §6, схема — [config/sensors/schema.json](../config/sensors/schema.json); синк в БД — DbMigrator.
- **Деплой/конфигурация** → [deploy/](../deploy/) (`compose.yaml`, `env.example`) и `implementation-spec.md` §32–36.
- **Безопасность, сообщение об уязвимости** → [SECURITY.md](../SECURITY.md).

## Правило актуальности

Статус реализации живёт в [AGENTS.md](../AGENTS.md) («Текущий статус») и чекбоксах фаз спек; при существенных изменениях системы обновляются и эти документы (см. AGENTS.md, «Держать агент-доки актуальными»).
