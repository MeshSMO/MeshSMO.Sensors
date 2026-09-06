# protocol.md — wire-протоколы сбора телеметрии

Статус: актуален на 2026-09-06. Реализация: `src/MeshSMO.Sensors.Gateway/Polling/`, контракт прошивки: `docs/repeater-firmware-acquisition-spec.md`.

---

## 1. Уровни

```text
Gateway ──HTTPS(TLS 1.2)── Repeater firmware API          (транспорт, см. §2)
Repeater ──LoRa/MeshCore── Sensor node                    (mesh-уровень, см. §3)
Sensor node ──ответ── Cayenne LPP телеметрия               (полезная нагрузка, см. §4)
```

## 2. Транспорт: Gateway ↔ Repeater

- Аутентификация: `POST /login` (body = plain-text пароль) → токен в теле ответа → заголовок `X-Auth-Token` на все `/api/*`. Панель держит **один глобальный токен**: каждый успешный логин инвалидирует предыдущий. Поэтому все потребители панели (`Worker` + `SensorTelemetryPoller`) делят singleton `MeshCoreTelSession`: логин один на старте, повторный — только после `401` (+ один retry того же запроса) — реализовано в `MeshCoreTelHttpClient`. Клиент, логинящийся независимо, устроит взаимный 401-пинг-понг. Токен также протухает по idle-таймауту панели (24 ч в текущей прошивке, раньше 15 мин) и после ребута репитера — gateway перелогинивается сам на первом же 401.
- TLS: **только TLS 1.2, cipher `TLS_RSA_WITH_AES_128_GCM_SHA256`** (static-RSA). ESP32-сервер отвергает TLS 1.3 и ECDHE-наборы от OpenSSL-рантаймов. Зафиксировано в `MeshCoreGatewayServiceCollectionExtensions` (non-Windows).
- Ограничения CLI: команда ≤ 191 байт UTF-8; serial-ответ читается до маркера `->`.

| Эндпоинт | Назначение |
|---|---|
| `POST /api/command` | CLI: `ver`, `clock`, `advertise`, `sensor list [start]`, `stats-core`, `stats-radio`, `stats-packets` |
| `GET /api/stats[?series=...]` | телеметрия репитера (core/radio/packets/history) |
| `POST /api/request` | acquisition: `{destination: <64 hex>, payload: <hex ≤160 Б>, timeoutMs 1..30000}` → `{status: ok\|timeout, responseHex, rssi, snr, elapsedMs}`; синхронный, один in-flight на репитер |
| `POST /api/login` | ANON-логин bootstrap: `{destination, password (0..15 Б), timeoutMs ≤10000}`; тот же ответ |

## 3. Mesh-уровень: REQ / ANON_REQ

MeshCore нода принимает запросы только от известных контактов. Bootstrap — анонимный логин:

```text
ANON_REQ plaintext = now_unique (uint32 LE) + password (≤15 байт, без терминатора)
  → нода (пароль "hello"/пустой) добавляет отправителя в ACL; запись персистентна.
```

Запрос к ноде (`PAYLOAD_TYPE_REQ`, ECDH self↔dest, flood):

```text
plaintext = timestamp (uint32 LE, unix seconds) + request_type (u8) + args
  request_type 0x01 = get stats (репитеры/room server)
  request_type 0x03 = GET_TELEMETRY_DATA (сенсорные ноды); args = inverse perm mask (0x00 = все права)
```

Для телеметрии MeshSMO: **`timestamp(4 LE) + 0x03 + 0x00`**, hex-пример: `f83d9d6a0300`.

Ответ: `PAYLOAD_TYPE_RESPONSE`, plaintext = **отражённый timestamp (4 байта, тег корреляции) + тело**. Gateway декодирует тело начиная с байта 4.

Correlation/idempotency: gateway использует сам `timestamp` как `request_id` (монотонный счётчик, начинается с unix time запуска). Повторы запросов допустимы — дедупликация на стороне БД по unique `(sensor_id, request_id)` в `measurement_samples`. Повторное использование timestamp нодой-отправителем после перезапуска gateway: счётчик снова стартует с текущего unix time, поэтому коллизий со старыми значениями у одной ноды не возникает (timestamp ноды-получателя защищает от replay только в её сторону).

## 4. Полезная нагрузка: Cayenne LPP (ElectronicCats 1.6.1, как в MeshCore)

Запись: `[channel u8][type u8][data N]`. Полный список типов и размеров — в `TelemetryTypes.KnownTypes` (`src/MeshSMO.Sensors.Application/Registry/TelemetryChannelMapping.cs`) и в `CayenneLppDecoder.DataSizeOf`. Основные:

| type | метрика (TypeKey) | размер | масштаб | единица |
|---|---|---|---|---|
| `0x67` | temperature | 2 | signed 0.1 | °C |
| `0x68` | humidity | 1 | 0.5 | % |
| `0x73` | pressure | 2 | 0.1 | hPa |
| `0x74` | voltage | 2 | 0.01 | V |
| `0x75` | current | 2 | 0.001 | A |
| `0x76` | frequency | 4 | 1 | Hz |
| `0x78` | percentage | 1 | 1 | % |
| `0x79` | altitude | 2 | signed 1 | m |
| `0x64` | generic | 4 | unsigned | — |
| `0x71` / `0x86` | accel_x/y/z, gyro_x/y/z | 6 | int16 ×0.001 G / ×0.01 °/s | |
| `0x88` | gps_lat/lon/alt | 9 | int24 ×0.0001° / ×0.01 m | |
| `0x80`/`0x82`/`0x83`/`0x84`/`0x7D`/`0x65`/`0x85` | power/distance/energy/direction/concentration/luminosity/unixtime | см. декодер | | |
| `0x00`/`0x01`/`0x66`/`0x8E`/`0x02`/`0x03` | digital_input/output, presence, switch, analog_in/out | 1–2 | | |

Правила декодирования (`CayenneLppDecoder`):

- неизвестный тип → декодирование останавливается (лог с hex-префиксом);
- хвост из нулевых байтов = паддинг буфера прошивки → игнорируется;
- один тип на нескольких каналах → ключи `voltage`, `voltage_2`, `voltage_5`… (суффикс = channel).

## 5. Маппинг в метрики MeshSMO

```text
metric_key = mapping[(channel, lpp_type)]     // из registry YAML: telemetry.channels
           | mapping[(channel, '*')]          // wildcard на канал
           | lpp_type                         // фолбэк
           | lpp_type + '_' + channel         // если lpp_type уже встречался в этом ответе
```

`displayName`/`unit` из маппинга синхронизируются в `sensor_metrics` и отдаются в `/api/v1/sensors/{slug}/latest`.

## 6. Контракт payload'ов outbox gateway

SQLite (`telemetry_snapshots.payload_json`) и далее `GatewayIngestionWorker`:

**Телеметрия репитера** — raw JSON ответа `/api/stats` (плоские readings строятся из него автоматически).

**Опрос датчика** (`type: "sensor_poll"`):

```json
{
  "type": "sensor_poll",
  "sensor": "smolensk-center",
  "requestId": 1788693122,
  "protocol": "meshcore-req-lpp",
  "rssi": -25.0,
  "snr": 12.5,
  "elapsedMs": 1723,
  "responseHex": "F83D9D6A0174018C...",
  "readings": [ { "metric": "battery_voltage", "value": 3.96, "unit": "V" } ]
}
```

Ingestion: `readings[]` → `measurement_values` (numeric + unit); `rssi`/`snr` → `measurement_samples`; `responseHex` (после отрезания 4-байтового тега) → `raw_payload`. Идемпотентность — unique `(sensor_id, request_id)`; повторные снапшоты outbox — unique `gateway_snapshot_id`.

## 7. Ошибки acquisition

| Код | Значение |
|---|---|
| `InvalidDestination` / `InvalidPayload` / `InvalidTimeout` / `InvalidPassword` | валидация (HTTP 400) |
| `Busy` | у репитера уже есть in-flight запрос (HTTP 409) — gateway шлёт строго последовательно |
| `RadioUnavailable` | радио не готово (HTTP 503) |
| `status: "timeout"` | пакет ушёл в эфир, ответа нет — нормальный исход; gateway логирует и (однократно) пробует логин |
