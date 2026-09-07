# MeshSMO Sensors — спецификация прогнозирования показаний

- Статус: implemented behind feature flag; profiling/production rollout pending
- Дата: 2026-09-07
- Область: числовые показания датчиков, прогноз на 1–24 часа

## 1. Решение в одном абзаце

Для MVP добавить отдельную .NET class library `MeshSMO.Sensors.Forecasting` на ML.NET 5.0.0. Единица обучения и оценки — строго одна временная серия `(sensor_id, metric_key)`: температура одного датчика не смешивается с температурой другого, а температура не смешивается с давлением. BFF строит прогноз по запросу из фактических значений PostgreSQL, но не записывает ни прогнозные точки, ни модель в БД. Короткоживущий результат хранится только в памяти процесса; одинаковые параллельные запросы объединяются. Первый кандидат модели — одномерный `ForecastBySsa`, но пользователю прогноз показывается только после rolling backtest и только если он лучше простого baseline для этой же серии.

## 2. Зафиксированные решения

1. Скриншот из задачи — UX-макет, реализации AI-прогноза во фронтенде сейчас нет.
2. Ключ модели: `(sensor_id, metric_key)`, а не только тип метрики и не только датчик.
3. В прогноз попадают только числовые `measurement_values.numeric_value`.
4. Фактические и прогнозные точки всегда различимы в API и UI.
5. Прогнозные точки и модели не персистятся в PostgreSQL.
6. В MVP нет отдельного runtime-сервиса и нового контейнера. Forecasting остаётся отдельным проектом, подключённым к `sensor-web` как библиотека.
7. Расчёт выполняется on demand. В памяти кешируется готовый результат, а не создаётся долговечное хранилище моделей.
8. Наличие достаточного числа точек само по себе не означает пригодность модели. Решение о показе принимается отдельно для каждой серии по backtest-метрикам.
9. Поддерживаемые горизонты MVP: `1h`, `6h`, `12h`, `24h`.
10. Первый алгоритм — SSA из `Microsoft.ML.TimeSeries`; архитектура не должна привязывать API к SSA, чтобы позже добавить regression-модель с lag/calendar/exogenous features.

Текущая реализация находится в ветке `codex/sensor-forecasting`. Проекты Forecasting, BFF endpoint, in-memory coordination и frontend UI реализованы; `Forecasting:Enabled` по умолчанию остаётся `false` до проверки на реальных рядах по Phase F0/F4.

## 3. Почему это соответствует текущей архитектуре

В проекте уже есть необходимые исходные данные:

- `measurement_values` хранит `sensor_id`, `metric_key`, `timestamp` и `numeric_value`;
- индекс `ix_measurement_values_sensor_metric_timestamp` соответствует ключу и сортировке обучающей выборки;
- `sensor_metrics` задаёт допустимые метрики датчика и единицы измерения;
- `sensors.poll_interval_seconds` даёт ожидаемую частоту исходных измерений;
- BFF уже является единственным публичным API и владельцем чтения PostgreSQL;
- historical API уже разделяет ряды по датчику и метрике и агрегирует их в UTC-aligned buckets.

Поэтому отдельная библиотека даёт изоляцию ML-кода, но новый микросервис на MVP добавил бы контейнер, внутренний API и отказоустойчивость без продуктовой пользы. Граница `IForecastService` оставляет возможность вынести расчёт позже без изменения публичного API.

## 4. Цели и нецели

### 4.1. Цели MVP

- прогнозировать одно выбранное числовое показание одного датчика;
- показывать центральную оценку и интервал неопределённости;
- не выдавать слабую или недостоверную модель за AI-прогноз;
- учитывать индивидуальное поведение каждой пары датчик/метрика;
- строить результат достаточно быстро для интерактивного UI;
- не ухудшать ingestion и обычный historical API;
- дать наблюдаемые причины, почему прогноз временно недоступен.

### 4.2. Не входит в MVP

- смешивание данных разных датчиков при обучении;
- использование температуры для прогноза давления или других внешних признаков;
- прогноз текстовых значений;
- долгосрочный прогноз свыше 24 часов;
- автоматическое принятие решений или управление оборудованием;
- обнаружение аварий и аномалий — это отдельная задача;
- хранение прогнозов, артефактов модели или backtest-результатов в БД;
- обещание вероятностной достоверности интервала без эмпирической проверки coverage;
- отдельный ML runtime/container в production.

## 5. Пользовательский сценарий и UX

1. Пользователь выбирает одну числовую метрику.
2. Кнопка `AI-прогноз` включает прогноз только для режима `Отдельно`. В combined-графике одновременный прогноз нескольких единиц измерения в MVP не показывается.
3. Пользователь выбирает горизонт `1 ч`, `6 ч`, `12 ч` или `24 ч`.
4. Фактическая линия заканчивается на последнем измерении. После неё начинается пунктирная прогнозная линия; полупрозрачная область означает прогнозный интервал, а не исторические min/max.
5. UI явно пишет время расчёта, горизонт и предупреждение: прогноз является оценкой по истории, а не фактическим измерением.
6. Tooltip для будущей точки показывает `прогноз`, `нижняя граница`, `верхняя граница` и timestamp.
7. Если ряд не прошёл quality gate, вместо линии отображается конкретная причина: мало истории, слишком много пропусков, данные устарели или модель не лучше baseline.
8. При смене датчика или метрики выполняется независимый запрос. Результат одной серии никогда не переиспользуется для другой.

В макете полоса historical min/max и полоса forecast confidence могут выглядеть похоже. Их следует различать цветом/легендой и не соединять в один `band` data key.

## 6. Публичный API

### 6.1. Endpoint

```http
GET /api/v1/sensors/{slug}/forecast?metric=temperature&horizon=24h
Accept: application/json
```

Допустимые `horizon`: `1h`, `6h`, `12h`, `24h`. Шаг прогноза сервер выбирает сам и возвращает в ответе. Клиент не управляет ML-параметрами.

### 6.2. Успешный ответ

```json
{
  "sensor": {
    "slug": "garden-east",
    "displayName": "Сад, восток"
  },
  "metric": {
    "key": "temperature",
    "unit": "°C"
  },
  "availability": "ready",
  "generatedAt": "2026-09-07T08:00:00Z",
  "lastObservationAt": "2026-09-07T07:58:41Z",
  "range": {
    "from": "2026-09-07T08:00:00Z",
    "to": "2026-09-08T08:00:00Z",
    "step": "5m"
  },
  "model": {
    "kind": "ssa",
    "trainingPoints": 8064,
    "trainingFrom": "2026-08-10T08:00:00Z",
    "trainingTo": "2026-09-07T08:00:00Z",
    "mae": 0.74,
    "mase": 0.81,
    "intervalCoverage": 0.89,
    "confidenceLevel": 0.9
  },
  "points": [
    {
      "timestamp": "2026-09-07T08:05:00Z",
      "predicted": 23.1,
      "lower": 21.8,
      "upper": 24.4
    }
  ]
}
```

Числа метрик в `model` диагностические; UI может скрыть их в раскрываемом блоке. Они нужны для тестирования и объяснимости результата.

### 6.3. Валидная серия без прогноза

Недостаток или низкое качество данных — нормальное продуктовое состояние, поэтому endpoint возвращает `200`, пустой `points` и одну из причин:

```json
{
  "sensor": { "slug": "garden-east", "displayName": "Сад, восток" },
  "metric": { "key": "temperature", "unit": "°C" },
  "availability": "insufficient_data",
  "reason": "At least 14 days of sufficiently complete observations are required.",
  "generatedAt": "2026-09-07T08:00:00Z",
  "lastObservationAt": "2026-09-06T20:00:00Z",
  "points": []
}
```

Значения `availability`:

| Значение | Смысл |
|---|---|
| `ready` | модель прошла проверку, точки можно показывать |
| `insufficient_data` | недостаточно истории или валидных точек |
| `sparse_data` | слишком много пропусков либо нет непрерывного участка |
| `stale_data` | последнее фактическое измерение слишком старое |
| `low_quality` | кандидат не превзошёл baseline или нарушил sanity checks |
| `disabled` | прогноз выключен для серии в конфигурации |

Ошибки контракта остаются HTTP-ошибками:

- `404` — датчик отсутствует, выключен или непубличный;
- `400 ValidationError` — неизвестная метрика, нечисловая метрика или неверный horizon;
- `429` — превышен отдельный лимит forecast API;
- `503` — расчёт не состоялся из-за timeout/перегрузки/внутренней ошибки, желательно с `Retry-After`.

### 6.4. HTTP cache semantics

- `Cache-Control: public, max-age=60, stale-while-revalidate=240` для публичных серий;
- `ETag` зависит от sensor id, metric key, horizon, последнего наблюдения и версии алгоритма;
- кеш BFF остаётся корректным только в пределах одного процесса; это допустимо для MVP.

## 7. Новая структура кода

```text
src/
  MeshSMO.Sensors.Forecasting/
    Abstractions/
      IForecastService.cs
      IForecastSeriesSource.cs
    Models/
      ForecastRequest.cs
      ForecastResult.cs
      ForecastPoint.cs
      ForecastAvailability.cs
    Preparation/
      SeriesResampler.cs
      SeriesQualityEvaluator.cs
    Evaluation/
      RollingOriginEvaluator.cs
      ForecastMetrics.cs
      SeasonalNaiveForecaster.cs
    MlNet/
      SsaForecastTrainer.cs
    Caching/
      ForecastResultCache.cs
      ForecastSingleFlight.cs
    ForecastingOptions.cs

src/MeshSMO.Sensors.Web/
  Api/Forecasting/
    ForecastEndpoints.cs
    PostgresForecastSeriesSource.cs
```

Зависимости:

- `MeshSMO.Sensors.Forecasting` → `MeshSMO.Sensors.Domain`, `MeshSMO.Sensors.Application`, `Microsoft.ML`, `Microsoft.ML.TimeSeries`;
- `MeshSMO.Sensors.Forecasting` не зависит от EF Core, PostgreSQL, ASP.NET или frontend;
- `MeshSMO.Sensors.Web` реализует `IForecastSeriesSource` поверх `SensorsDbContext` и маппит HTTP-контракт;
- `MeshSMO.Sensors.Web` → `MeshSMO.Sensors.Forecasting`;
- Gateway не меняется и ничего не знает о прогнозировании.

`IForecastSeriesSource` возвращает уже агрегированную UTC-серию и метаданные. Это позволяет тестировать алгоритм без БД и позже вынести runtime, не переписывая ML-код.

## 8. Подготовка временного ряда

SSA — одномерный алгоритм: на входе есть только упорядоченная последовательность значений `float`. Timestamp не является признаком модели. Поэтому все правила подготовки должны быть явными и детерминированными.

### 8.1. Отбор данных

Для ключа `(sensor_id, metric_key)`:

1. Читать только `numeric_value IS NOT NULL`.
2. Ограничить историю конфигурируемым окном; рекомендуемый старт — последние 28 дней.
3. Не включать точки из будущего относительно server clock.
4. Отбросить `NaN`/`Infinity` и значения с явно invalid quality, когда поле `quality` начнёт использоваться.
5. Сортировать только по UTC timestamp.
6. Последнюю незавершённую временную корзину не использовать для обучения.

### 8.2. Равномерная сетка

Рекомендуемый шаг MVP — 5 минут. Тогда горизонты составляют 12, 72, 144 и 288 точек, а дневная сезонность — 288 точек.

Источник должен агрегировать значения непосредственно PostgreSQL `date_bin` + `avg`, чтобы не загружать в BFF десятки тысяч сырых строк. Точка сетки принадлежит полуинтервалу `[bucket, bucket + step)`.

Шаг 5 минут является первым default, а не вечной константой. Spike должен измерить 1, 5 и 15 минут на реальных данных. Более частая сетка увеличивает стоимость SSA, более редкая теряет краткосрочную динамику.

### 8.3. Пропуски

- один пропущенный bucket можно заполнить линейной интерполяцией;
- два последовательных bucket — только если оба края известны;
- один или два trailing bucket без правого края заполняются последним фактическим значением, чтобы короткая задержка polling не сдвигала начало прогноза;
- более длинный разрыв не интерполировать;
- для обучения брать самый свежий непрерывный сегмент после длинного разрыва;
- минимальная полнота исходных bucket до интерполяции — 85%;
- минимальная длина рекомендуемого сегмента — 14 дней для 5-минутной сетки;
- последнее реальное измерение не должно быть старше `max(3 × poll_interval, 15 минут)`.

Эти числа являются начальными defaults и должны быть подтверждены data profiling. Причина отказа возвращается как `sparse_data`, `insufficient_data` или `stale_data`.

### 8.4. Выбросы и физические границы

На MVP не следует автоматически сглаживать выбросы: резкое изменение может быть реальным событием. Разрешено отбрасывать только:

- нечисловые/неfinite значения;
- значения, явно помеченные источником как invalid;
- значения за физическими границами, если эти границы явно заданы для конкретной метрики.

Рекомендуется добавить необязательные series overrides, например `minimum`, `maximum`. Для `humidity` и процентной `battery` допустим дефолт `[0, 100]`; для пользовательских метрик границы не угадывать.

Если raw forecast заметно выходит за допустимые границы, quality gate должен сначала пометить модель как слабую. Простое обрезание значений к min/max не должно скрывать плохую модель. Небольшой численный выход после успешной оценки можно clamp-ить только на этапе ответа.

## 9. Обучение и выбор модели

### 9.1. Baseline обязателен

До SSA считать как минимум два дешёвых baseline:

- `last value` — продолжение последнего известного значения;
- `seasonal naive` — значение из того же времени предыдущих суток, если доступно.

Лучший baseline выбирается на тех же backtest-folds. Если SSA его не превосходит, `AI-прогноз` не публикуется. Это защищает от красивой, но бесполезной кривой.

### 9.2. SSA candidate

Использовать `MLContext.Forecasting.ForecastBySsa`:

- input — `Single Value`;
- output — `Forecast`, `LowerBound`, `UpperBound`;
- `confidenceLevel` default `0.90`;
- `horizon` — максимальный необходимый горизонт в точках;
- `variableHorizon: true`, если один обученный candidate оценивается на нескольких горизонтах внутри одного запроса;
- `isAdaptive: false` для воспроизводимого on-demand расчёта MVP;
- `shouldStabilize: true`;
- rank сначала выбирать автоматически.

Не фиксировать один `windowSize` для всех серий без проверки. На spike проверить небольшой ограниченный набор окон, выраженных в длительности, например 1 ч, 6 ч, 12 ч и 24 ч. Недопустимые для короткой серии варианты исключаются до `Fit`. Побеждает конфигурация с лучшим средним MASE; при близких результатах выбирается более дешёвая.

### 9.3. Backtest без утечки будущего

Случайный train/test split для временного ряда запрещён. Использовать expanding-window rolling origin:

```text
fold 1: [------ train ------][validate]
fold 2: [--------- train ---------][validate]
fold 3: [------------ train ------------][validate]
```

Рекомендованный MVP:

- 3 последних folds;
- validation horizon равен запрошенному horizon, но не более доступного участка;
- вся подготовка fold выполняется только из его train-части;
- метрики агрегируются по folds и по всем точкам горизонта;
- после выбора параметров финальная модель обучается на всей доступной серии и сразу строит ответ.

### 9.4. Метрики качества per series

Хранить в результате расчёта:

- `MAE` в родной единице метрики — понятен человеку, но несопоставим между температурой и давлением;
- `RMSE` — сильнее штрафует большие промахи;
- `MASE` — ошибка относительно naive baseline и основной межсерийный quality gate;
- empirical interval coverage — доля фактов, попавших в заявленный интервал;
- средняя ширина интервала в родной единице.

Не использовать единый порог MAE для всех метрик. Именно MASE позволяет отдельно решить, хорош ли прогноз для температуры этого датчика и хорош ли он для давления другого.

Начальный quality gate:

```text
ready = finite output
     && MASE <= 0.90
     && MAE <= optional series-specific maxMae
     && coverage >= 0.80 for a nominal 90% interval
     && no material physical-bound violation
```

`MASE <= 0.90` означает минимум 10% улучшения против baseline. Если denominator MASE равен нулю у почти постоянного ряда, использовать MAE/RMSE и отдельную проверку variance; не делить на ноль.

Порог coverage нельзя считать статистически надёжным при слишком малом числе backtest-точек. В таком случае результат должен быть `insufficient_data`, а не автоматически `ready`.

### 9.5. Интервалы — не гарантия

Границы, возвращаемые SSA, следует называть `forecast interval` / `интервал прогноза`, а не гарантированным диапазоном. Номинальный уровень 90% обязательно проверяется фактическим coverage на rolling backtest. Если coverage систематически хуже порога, модель не публикуется либо интервал в будущей версии калибруется по остаткам.

## 10. On-demand lifecycle и защита ресурсов

Запрос обрабатывается так:

```text
HTTP request
  -> validate sensor/metric/horizon
  -> look up result cache by (sensor, metric, horizon, algorithmVersion)
  -> if miss: join/create one single-flight calculation for this key
  -> acquire global training semaphore
  -> read/resample history
  -> profile quality
  -> rolling backtest baseline + SSA candidates
  -> train winner on full series
  -> produce forecast
  -> cache immutable result
  -> HTTP response
```

Рекомендуемые ограничения MVP:

- result-cache TTL: 5 минут;
- cache size limit: 64 series/horizon entries;
- один активный расчёт на точный cache key;
- не более 1–2 одновременных ML-расчётов на весь BFF;
- общий timeout запроса: начать с 10 секунд, уточнить после benchmark;
- отдельный rate limit: начать с 20 запросов/мин на IP;
- cancellation клиента отменяет только ожидание; общий single-flight расчёт не отменяется, пока его ждёт другой клиент;
- исключение/timeout не кешировать надолго; допустим negative cache 15–30 секунд против retry storm.

Cache key должен включать timestamp последнего завершённого 5-минутного bucket либо короткий TTL должен гарантировать обновление. Версия алгоритма обязательна, иначе rollout новой подготовки может отдать старый результат.

CPU-bound обучение нельзя выполнять параллельно без лимита через обычный public rate limiter. Семафор и single-flight являются частью correctness, а не преждевременной оптимизацией.

## 11. Конфигурация

Базовые operational defaults — в `sensor-web` configuration:

```json
{
  "Forecasting": {
    "Enabled": false,
    "Step": "5m",
    "TrainingWindow": "28d",
    "MinimumHistory": "14d",
    "MinimumCoverage": 0.85,
    "ConfidenceLevel": 0.90,
    "ResultCacheTtl": "5m",
    "MaximumConcurrentTrainings": 1,
    "CalculationTimeout": "10s",
    "Series": {
      "garden-east:temperature": {
        "Enabled": true,
        "Minimum": -50,
        "Maximum": 60,
        "MaximumMae": 2.0
      },
      "garden-east:pressure": {
        "Enabled": true,
        "MaximumMae": 4.0
      }
    }
  }
}
```

Формат duration должен переиспользовать существующий подход `RegistryDuration`, а не вводить второй несовместимый парсер.

После spike возможен перенос per-series product settings в GitOps registry. Для MVP app configuration проще: она не требует менять публичный registry snapshot и frontend generator до подтверждения алгоритма.

`Enabled` по умолчанию должен быть `false`, пока benchmark и backtest не проведены на реальных данных.

## 12. Наблюдаемость

Нельзя логировать все обучающие значения. Для каждого расчёта достаточно structured fields:

- sensor id/slug и metric key;
- horizon, step, training range и число точек;
- observed coverage и число интерполированных bucket;
- candidate count, выбранная модель/параметры;
- MAE, RMSE, MASE и interval coverage;
- durations: DB read, preparation, backtest, final fit, total;
- cache hit/miss, single-flight join;
- availability/reason;
- timeout/exception type.

Желаемые метрики, когда будет добавлен OTel:

- `forecast_requests_total{availability,horizon}`;
- `forecast_calculation_duration_seconds`;
- `forecast_training_duration_seconds`;
- `forecast_cache_hits_total` / `forecast_cache_misses_total`;
- `forecast_active_calculations`;
- `forecast_quality_mase{metric}` без sensor slug label, чтобы не создавать high cardinality.

## 13. Тестирование

### 13.1. Unit

- UTC bucket boundaries и исключение незавершённого bucket;
- сортировка, duplicate timestamps, null/NaN/Infinity;
- интерполяция одного/двух пропусков и разрыв длинного gap;
- coverage и minimum-history decisions;
- horizons → число и timestamps точек;
- MAE/RMSE/MASE, включая константный ряд;
- seasonal-naive baseline;
- rolling-origin не использует future data;
- physical bounds и clamp policy;
- cache key, expiry, single-flight и cancellation;
- deterministic result при фиксированных данных и seed.

### 13.2. Integration

- PostgreSQL `date_bin` query использует `(sensor_id, metric_key, timestamp)` и не смешивает серии;
- public/disabled/unknown sensor rules совпадают с historical API;
- нечисловая или отсутствующая metric отвергается;
- API schema для `ready` и каждого unavailable-state;
- параллельные одинаковые HTTP-запросы запускают один расчёт;
- rate limit и training semaphore работают независимо;
- отмена одного клиента не ломает результат других;
- никакая forecast-операция не делает INSERT/UPDATE/DELETE.

EF InMemory не подходит для проверки PostgreSQL bucketing. Нужен PostgreSQL integration test (Testcontainers либо отдельный CI service), потому что текущий SQLite fallback historical reader не доказывает поведение `date_bin`.

### 13.3. Golden datasets

Добавить небольшие синтетические наборы:

- постоянный ряд;
- линейный тренд;
- суточная сезонность + шум;
- изменение уровня;
- редкие и длинные пропуски;
- выброс;
- ряд, где SSA хуже last-value;
- ряд с физической границей.

Тест не должен утверждать точные float по всей кривой между версиями ML.NET. Проверять форму, finite values, timestamps, воспроизводимый диапазон метрик и ожидаемое решение quality gate.

## 14. Performance spike до продуктовой реализации

На копии реальных данных, без публикации самих значений:

1. Выбрать минимум 3 разные серии: температура с суточным циклом, давление и слабо меняющаяся battery/иная метрика.
2. Снять data profile: длительность истории, cadence, coverage, gaps, variance, выбросы.
3. Сравнить step `1m`, `5m`, `15m`.
4. Для горизонтов 1/6/12/24 ч сравнить last-value, seasonal-naive и SSA candidates.
5. Измерить cold latency, steady-state latency, allocation/peak memory и CPU.
6. Повторить при двух параллельных разных series keys.
7. Зафиксировать таблицу MAE/RMSE/MASE/coverage и выбранные defaults.

Предварительный performance budget:

- p95 cache hit ≤ 100 мс;
- p95 cold calculation ≤ 3 с на production hardware;
- hard timeout ≤ 10 с;
- peak additional memory одного расчёта ≤ 256 MB;
- ingestion/history API не должны заметно деградировать при одном training slot.

Если cold calculation не укладывается, сначала сократить candidate grid и/или перейти на 15-минутный step. Персистентный model cache или отдельный worker рассматриваются только после измерений.

## 15. План реализации

### Phase F0 — data profiling и ML spike

- [ ] Создать console benchmark вне production path или test project.
- [ ] Реализовать read-only выгрузку одной `(sensor, metric)` серии.
- [ ] Построить равномерную сетку и отчёт по gaps/coverage.
- [ ] Реализовать два baseline и rolling-origin evaluator.
- [ ] Проверить ML.NET 5.0.0 SSA на реальных сериях.
- [ ] Сравнить шаги, окна и горизонты.
- [ ] Зафиксировать измеренные defaults и решить go/no-go.

Результат: воспроизводимый отчёт, подтверждающий, что хотя бы для части серий SSA лучше baseline и укладывается в latency/memory budget.

### Phase F1 — Forecasting library

- [ ] Добавить `MeshSMO.Sensors.Forecasting` в solution.
- [ ] Добавить central package versions `Microsoft.ML` и `Microsoft.ML.TimeSeries` 5.0.0.
- [ ] Описать request/result types и `IForecastSeriesSource`.
- [ ] Реализовать preparation, baselines, metrics, rolling backtest и SSA adapter.
- [ ] Реализовать quality gates и reason codes.
- [ ] Покрыть синтетическими unit/golden tests.

Результат: чистая библиотека без EF/ASP.NET, принимающая ряд и возвращающая `ForecastResult`.

### Phase F2 — BFF integration

- [ ] Реализовать PostgreSQL series source с server-side bucketing.
- [ ] Добавить options validation с `ValidateOnStart`.
- [ ] Добавить size-limited memory cache, single-flight и global semaphore.
- [ ] Добавить forecast-specific rate limiter и timeout.
- [ ] Добавить endpoint и OpenAPI contract.
- [ ] Добавить integration/API/concurrency tests.
- [ ] Добавить feature flag `Forecasting:Enabled=false` по умолчанию.

Результат: API строит прогноз в реальном времени, не выполняет DB writes и безопасно деградирует.

### Phase F3 — frontend по макету

- [ ] Добавить API types/query hook.
- [ ] Добавить toggle `AI-прогноз` и whitelist горизонтов.
- [ ] Запрашивать forecast только для одной выбранной метрики.
- [ ] Отдельно отрисовать actual min/max, forecast line и forecast interval.
- [ ] Добавить loading, unavailable, timeout и retry states.
- [ ] Не соединять последний actual и первый forecast через большой скрытый gap.
- [ ] Добавить disclaimer и доступную legend/tooltip.
- [ ] Сохранять forecast preferences вместе с текущими history preferences.
- [ ] Добавить component/e2e tests.

Результат: UX соответствует смыслу макета, но не маскирует отсутствие качественного прогноза.

### Phase F4 — production validation

- [ ] Включить feature flag только для явно выбранных series overrides.
- [ ] Наблюдать latency, cache ratio, low-quality rate и влияние на BFF.
- [ ] Сравнивать опубликованный прогноз с пришедшими позже фактами офлайн/в логическом audit tool, не сохраняя forecast в продуктовой БД.
- [ ] Через 2–4 недели пересмотреть thresholds и candidate grid.
- [ ] После подтверждения включать следующие серии по одной.

## 16. Acceptance criteria MVP

Функция считается готовой, если:

1. Ни один код-путь не смешивает sensor id или metric key.
2. Forecast endpoint не пишет в БД и не меняет gateway/outbox.
3. Все точки ответа строго позже последнего завершённого bucket, идут с одинаковым шагом и заканчиваются на выбранном горизонте.
4. Для `ready` есть минимум 3 rolling-origin folds и зафиксированные MAE/RMSE/MASE/coverage.
5. SSA как минимум на 10% лучше выбранного baseline по MASE либо для серии задан и пройден осознанный override.
6. Некачественные/редкие/устаревшие данные возвращают объяснимый unavailable-state без 500.
7. Одинаковые конкурентные запросы не запускают повторное обучение.
8. Глобальный лимит обучения не позволяет forecast API занять весь CPU.
9. Cache hit и cold calculation укладываются в подтверждённые budgets.
10. Фронтенд визуально и семантически различает факты, historical aggregation band и forecast interval.
11. Feature flag можно выключить без миграции и redeploy БД.
12. `dotnet build`, unit/integration tests, frontend checks проходят без warnings.

## 17. Риски и решения

| Риск | Последствие | Митигация |
|---|---|---|
| Нерегулярный polling и outages | SSA воспринимает пропуск как сдвиг времени | UTC resampling, ограниченная интерполяция, latest contiguous segment |
| Суточной истории мало | модель рисует тренд без подтверждения | minimum history + rolling backtest + `insufficient_data` |
| SSA хуже простого baseline | ложное ощущение «ИИ лучше» | обязательные last-value/seasonal-naive и MASE gate |
| On-demand training перегружает BFF | растёт latency всех API | cache, single-flight, global semaphore, timeout, отдельный rate limit |
| Интервал выглядит как гарантия | пользователь переоценивает точность | disclaimer + empirical coverage gate |
| Общий порог не подходит разным величинам | давление и температура оцениваются неверно | MASE per series + optional `MaximumMae` override |
| Смена/ремонт датчика меняет распределение | старая история ухудшает модель | ограниченное training window; позже change-point reset |
| Несколько BFF replicas | cache и расчёты дублируются | допустимо MVP; позже отдельный forecasting service/distributed cache |
| Версия ML.NET меняет численные результаты | brittle golden tests | pin 5.0.0, versioned algorithm id, tolerant metric assertions |

## 18. Возможные следующие версии

Переходить к ним только по данным production validation:

- regression-модель ML.NET с lag, rolling mean, hour-of-day/day-of-year и погодными признаками;
- multivariate prediction, где внешние признаки явно объявлены, а не случайно смешаны;
- adaptive online update между полными retrain;
- калибровка forecast intervals по backtest residuals;
- автоматический выбор step/training window per series;
- change-point detection и автоматическое отбрасывание старого режима;
- отдельный `sensor-forecasting` runtime при горизонтальном масштабировании BFF;
- файловый/object-storage cache моделей, если cold training действительно станет проблемой;
- offline audit store вне продуктовой БД для долговременного сравнения forecast versus actual.

## 19. Открытые продуктовые вопросы

Эти вопросы не блокируют spike, но должны быть решены до включения production flag:

1. Какие серии включаем первыми и есть ли для них экспертные `MaximumMae` в родных единицах?
2. Нужно ли показывать baseline, если SSA не прошёл gate, или честнее скрывать весь `AI-прогноз`?
3. Достаточен ли горизонт 24 часа для всех метрик?
4. Должны ли физические границы жить в registry как свойства metric, а не в Forecasting config?
5. Разрешён ли публичный диагностический блок с MAE/MASE, или эти поля оставить только в internal/debug режиме?

## 20. Официальные источники

- [Microsoft Learn: ForecastBySsa API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.timeseriescatalog.forecastbyssa) — SSA является univariate time-series forecaster; API задаёт window/series/train sizes, horizon и confidence bounds.
- [Microsoft Learn: ML.NET time-series forecasting tutorial](https://learn.microsoft.com/en-us/dotnet/machine-learning/tutorials/time-series-demand-forecasting) — официальный пример подготовки последовательности, holdout-проверки, `ForecastBySsa` и confidence interval.
- [NuGet: Microsoft.ML.TimeSeries 5.0.0](https://www.nuget.org/packages/Microsoft.ML.TimeSeries/5.0.0) — актуальная стабильная версия на момент investigation; preview 6.0.0 в MVP не используется.
- [dotnet/machinelearning](https://github.com/dotnet/machinelearning) — официальный исходный код и release notes ML.NET.
