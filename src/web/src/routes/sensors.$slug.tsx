import { createFileRoute, Link } from "@tanstack/react-router";
import { lazy, Suspense, useEffect, useRef, useState } from "react";
import { format } from "date-fns";
import { ru } from "date-fns/locale";
import type { DateRange } from "react-day-picker";
import { EmptyState, PageShell, SkeletonLine } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import {
  rangeLabels,
  ranges,
  resolveRange,
  useLatest,
  useMeasurementsMany,
  useSensor,
  useStatus,
  type RangeKey,
} from "@/lib/api";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Checkbox } from "@/components/ui/checkbox";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Input } from "@/components/ui/input";
import { CalendarDays, ChevronDown, Clock } from "lucide-react";
import { getRegistrySensor } from "@/lib/registry";
import {
  formatCoordinate,
  formatDateTime,
  formatNumber,
  formatReading,
  formatValue,
  normalizeState,
} from "@/lib/format";
import { getMetric, metricLabel } from "@/lib/metrics";

const MetricChart = lazy(() => import("@/components/site/MetricChart"));
const CombinedChart = lazy(() => import("@/components/site/CombinedChart"));

type ChartMode = "separate" | "combined";

type Search = {
  metric?: string;
  metrics?: string;
  range?: RangeKey;
  from?: string;
  to?: string;
  mode?: ChartMode;
};

export const Route = createFileRoute("/sensors/$slug")({
  validateSearch: (search: Record<string, unknown>): Search => {
    const range = ranges.includes(search["range"] as RangeKey)
      ? (search["range"] as RangeKey)
      : undefined;
    const str = (key: string) =>
      typeof search[key] === "string" && search[key] ? (search[key] as string) : undefined;
    const metric = str("metric");
    const metrics = str("metrics");
    const from = str("from");
    const to = str("to");
    const mode = search["mode"] === "combined" ? ("combined" as const) : undefined;
    return {
      ...(metric ? { metric } : {}),
      ...(metrics ? { metrics } : {}),
      ...(range ? { range } : {}),
      ...(from ? { from } : {}),
      ...(to ? { to } : {}),
      ...(mode ? { mode } : {}),
    };
  },
  head: ({ params }) => {
    const sensor = getRegistrySensor(params.slug);
    const name = sensor?.displayName ?? params.slug;
    const url = `https://sensors.meshsmo.ru/sensors/${params.slug}`;
    const metricNames = (sensor?.metrics ?? []).map((m) => metricLabel(m));
    const metricsPhrase = metricNames.slice(0, 3).join(", ").toLowerCase();
    const title = metricsPhrase
      ? `Датчик «${name}» — ${metricsPhrase} | MeshSMO`
      : `Датчик «${name}» — телеметрия | MeshSMO`;
    const description =
      sensor?.description ??
      `Показания датчика ${name} в сети MeshSMO: текущие значения, история и диагностика радиоканала.`;
    return {
      meta: [
        { title },
        { name: "description", content: description },
        { property: "og:title", content: title },
        { property: "og:description", content: description },
        { property: "og:type", content: "website" },
        { property: "og:url", content: url },
        { name: "twitter:card", content: "summary_large_image" },
        ...(sensor && !sensor.indexable ? [{ name: "robots", content: "noindex,follow" }] : []),
      ],
      links: [{ rel: "canonical", href: url }],
      scripts: [
        {
          type: "application/ld+json",
          children: JSON.stringify({
            "@context": "https://schema.org",
            "@type": "BreadcrumbList",
            itemListElement: [
              {
                "@type": "ListItem",
                position: 1,
                name: "Главная",
                item: "https://sensors.meshsmo.ru/",
              },
              {
                "@type": "ListItem",
                position: 2,
                name: "Датчики",
                item: "https://sensors.meshsmo.ru/sensors",
              },
              {
                "@type": "ListItem",
                position: 3,
                name,
                item: url,
              },
            ],
          }),
        },
        ...(sensor
          ? [
              {
                type: "application/ld+json",
                children: JSON.stringify({
                  "@context": "https://schema.org",
                  "@type": "Dataset",
                  name: `Телеметрия датчика «${sensor.displayName}»`,
                  description: sensor.description,
                  url,
                  inLanguage: "ru",
                  isAccessibleForFree: true,
                  creator: { "@type": "Organization", name: "MeshSMO" },
                  measurementTechnique: sensor.protocol,
                  variableMeasured: sensor.metrics.map((m) => {
                    const meta = getMetric(m);
                    return {
                      "@type": "PropertyValue",
                      name: meta.label,
                      alternateName: m,
                      ...(meta.unit ? { unitText: meta.unit } : {}),
                    };
                  }),
                  ...(sensor.location
                    ? {
                        spatialCoverage: {
                          "@type": "Place",
                          geo: {
                            "@type": "GeoCoordinates",
                            latitude: sensor.location.latitude,
                            longitude: sensor.location.longitude,
                          },
                        },
                      }
                    : {}),
                }),
              },
            ]
          : []),
      ],
    };
  },

  component: SensorPage,
});

function SensorPage() {
  const { slug } = Route.useParams();
  const search = Route.useSearch();
  const registry = getRegistrySensor(slug);

  if (!registry) return <NotFound />;

  const chartable = registry.metrics.filter((m) => getMetric(m).kind === "numeric");
  const requested = (search.metrics ?? search.metric ?? "")
    .split(",")
    .map((m) => m.trim())
    .filter((m) => chartable.includes(m));
  const selected = requested.length > 0 ? requested : chartable.slice(0, 1);
  const range: RangeKey = search.range ?? "24h";
  const mode: ChartMode = search.mode ?? "separate";

  return (
    <PageShell>
      <nav aria-label="Хлебные крошки" className="text-xs text-muted-foreground">
        <Link to="/" className="hover:text-foreground">
          Главная
        </Link>
        <span className="px-2">/</span>
        <Link to="/sensors" className="hover:text-foreground">
          Датчики
        </Link>
        <span className="px-2">/</span>
        <span className="text-foreground">{registry.displayName}</span>
      </nav>

      <SensorHeader slug={slug} />

      <p className="mt-4 max-w-2xl text-muted-foreground">{registry.description}</p>

      <p className="num mt-3 text-sm text-muted-foreground">
        {registry.location
          ? `${formatCoordinate(registry.location.latitude)}, ${formatCoordinate(registry.location.longitude)}`
          : "Координаты не опубликованы"}
        <span className="ml-2 font-sans">
          {registry.location?.precision === "approximate" ? "(координаты приблизительные)" : ""}
        </span>
      </p>

      <ul className="mt-3 flex flex-wrap gap-2">
        {registry.metrics.map((m) => (
          <li
            key={m}
            className="rounded-md border border-border bg-surface-raised px-2 py-1 text-xs text-muted-foreground"
          >
            {metricLabel(m)}
          </li>
        ))}
      </ul>

      <Readings slug={slug} metrics={registry.metrics} poll={registry.pollIntervalSeconds ?? 60} />

      <History
        slug={slug}
        metrics={chartable}
        selected={selected}
        range={range}
        mode={mode}
        from={search.from}
        to={search.to}
        hasExplicitParams={Boolean(
          search.metric ??
          search.metrics ??
          search.range ??
          search.from ??
          search.to ??
          search.mode,
        )}
      />

      <Diagnostics
        slug={slug}
        protocol={registry.protocol ?? "—"}
        pollIntervalSeconds={registry.pollIntervalSeconds ?? 300}
      />
    </PageShell>
  );
}

function SensorHeader({ slug }: { slug: string }) {
  const { data } = useSensor(slug);
  const registry = getRegistrySensor(slug);
  return (
    <div className="mt-4 flex flex-wrap items-center gap-4">
      <h1 className="text-3xl font-semibold tracking-tight">
        {data?.displayName ?? registry?.displayName ?? slug}
      </h1>
      <StatusBadge state={data ? normalizeState(data.state) : null} />
    </div>
  );
}

function Readings({ slug, metrics, poll }: { slug: string; metrics: string[]; poll: number }) {
  const { data, isPending, isError } = useLatest(slug, poll);
  const values = new Map((data?.values ?? []).map((v) => [v.metric, v]));
  // Ключи: реестр + всё, что реально прислал BFF (маппнутые ключи вроде
  // battery_voltage / solar_panel_voltage могут отсутствовать в YAML).
  const keys = [
    ...metrics,
    ...(data?.values ?? []).map((v) => v.metric).filter((m) => !metrics.includes(m)),
  ];

  return (
    <section className="mt-10">
      <h2 className="text-xl font-semibold tracking-tight">Показания</h2>
      <div className="mt-4 grid grid-cols-2 gap-3 lg:grid-cols-4" aria-live="polite">
        {keys.map((key) => {
          const meta = getMetric(key);
          const value = values.get(key);
          const showUnit = meta.kind === "numeric";
          return (
            <div key={key} className="panel px-4 py-4">
              <p className="text-xs text-muted-foreground">{value?.displayName ?? meta.label}</p>
              <p className="num mt-2 break-words text-3xl">
                {isPending && !isError ? (
                  <SkeletonLine className="h-8 w-20" />
                ) : (
                  <>
                    {formatReading(key, value?.numericValue, value?.textValue)}{" "}
                    {showUnit ? (
                      <span className="text-base text-muted-foreground">
                        {value?.unit ?? meta.unit}
                      </span>
                    ) : null}
                  </>
                )}
              </p>
              <p className="num mt-2 text-xs text-muted-foreground">
                {value ? formatDateTime(value.timestamp) : "нет измерений"}
              </p>
            </div>
          );
        })}
      </div>
    </section>
  );
}

const PREFS_KEY = "meshsmo:sensor-history";

type HistoryPrefs = Pick<Search, "metrics" | "range" | "from" | "to" | "mode">;

function readHistoryPrefs(slug: string): HistoryPrefs | null {
  try {
    const raw = window.localStorage.getItem(PREFS_KEY);
    if (!raw) return null;
    const all = JSON.parse(raw) as Record<string, HistoryPrefs>;
    return all[slug] ?? null;
  } catch {
    return null;
  }
}

function writeHistoryPrefs(slug: string, prefs: HistoryPrefs): void {
  try {
    const raw = window.localStorage.getItem(PREFS_KEY);
    const all = (raw ? JSON.parse(raw) : {}) as Record<string, HistoryPrefs>;
    all[slug] = prefs;
    window.localStorage.setItem(PREFS_KEY, JSON.stringify(all));
  } catch {
    // localStorage может быть недоступен (приватный режим) — просто пропускаем.
  }
}

function History({
  slug,
  metrics,
  selected,
  range,
  mode,
  from,
  to,
  hasExplicitParams,
}: {
  slug: string;
  metrics: string[];
  selected: string[];
  range: RangeKey;
  mode: ChartMode;
  from?: string | undefined;
  to?: string | undefined;
  hasExplicitParams: boolean;
}) {
  const navigate = Route.useNavigate();
  const update = (next: { [K in keyof Search]?: Search[K] | undefined }) =>
    navigate({
      search: (prev) => {
        const merged: Record<string, unknown> = { ...prev };
        for (const [key, value] of Object.entries(next)) {
          if (value === undefined) delete merged[key];
          else merged[key] = value;
        }
        return merged as Search;
      },
      replace: true,
      resetScroll: false,
    });

  // Восстановление сохранённых настроек: только после гидрации и только
  // если в адресе нет явных параметров. Пока restore не отработал (включая
  // навигацию с сохранёнными параметрами), запись в storage заблокирована:
  // эффект сохранения на чистом URL записал бы дефолтный выбор поверх ещё
  // не прочитанных настроек, а роутер может смонтировать компонент повторно —
  // и тогда повторный restore прочитает уже затёртый дефолт.
  const [prefsReady, setPrefsReady] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const restore = async () => {
      if (!hasExplicitParams) {
        const saved = readHistoryPrefs(slug);
        if (saved) {
          const next: { [K in keyof Search]?: Search[K] | undefined } = {};
          if (saved.metrics) next.metrics = saved.metrics;
          if (saved.range) next.range = saved.range;
          if (saved.from) next.from = saved.from;
          if (saved.to) next.to = saved.to;
          if (saved.mode) next.mode = saved.mode;
          if (Object.keys(next).length > 0) await update(next);
        }
      }
      if (!cancelled) setPrefsReady(true);
    };
    void restore();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [slug]);

  // Сохранение текущего выбора для этого датчика.
  const lastSavedRef = useRef<{ slug: string; json: string } | null>(null);
  useEffect(() => {
    if (!prefsReady) return;

    const prefs: HistoryPrefs = { metrics: selected.join(","), range };
    if (from) prefs.from = from;
    if (to) prefs.to = to;
    if (mode === "combined") prefs.mode = "combined";

    // selected — новый массив на каждый рендер; пишем только при реальном
    // изменении значений, иначе effect срабатывает после каждого рендера.
    const json = JSON.stringify(prefs);
    const last = lastSavedRef.current;
    if (last && last.slug === slug && last.json === json) return;
    lastSavedRef.current = { slug, json };

    writeHistoryPrefs(slug, prefs);
  }, [prefsReady, slug, selected, range, from, to, mode]);

  const custom = { from, to };
  const results = useMeasurementsMany(slug, selected, range, custom);
  const bounds = resolveRange(range, custom);

  const series = selected.map((key, index) => ({
    metricKey: key,
    unit: results[index]?.data?.metric.unit ?? getMetric(key).unit,
    points: results[index]?.data?.points ?? [],
    isPending: results[index]?.isPending ?? false,
    isError: results[index]?.isError ?? false,
  }));
  const withData = series.filter((s) => s.points.length > 0);

  const toggleMetric = (key: string) => {
    const next = selected.includes(key) ? selected.filter((m) => m !== key) : [...selected, key];
    update({ metrics: (next.length > 0 ? next : [key]).join(","), metric: undefined });
  };

  return (
    <section className="mt-10">
      <h2 className="text-xl font-semibold tracking-tight">История</h2>

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <Popover>
          <PopoverTrigger className="flex items-center gap-2 rounded-md border border-border bg-surface-raised px-3 py-1.5 text-xs text-foreground hover:border-accent">
            {selected.length === 1
              ? metricLabel(selected[0] as string)
              : `Выбрано показателей: ${selected.length}`}
            <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" aria-hidden />
          </PopoverTrigger>
          <PopoverContent align="start" className="w-64 p-2">
            <ul className="max-h-72 space-y-1 overflow-y-auto">
              {metrics.map((key) => (
                <li key={key}>
                  <label className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-surface-raised">
                    <Checkbox
                      checked={selected.includes(key)}
                      onCheckedChange={() => toggleMetric(key)}
                    />
                    <span>{metricLabel(key)}</span>
                  </label>
                </li>
              ))}
            </ul>
          </PopoverContent>
        </Popover>

        <div className="flex overflow-hidden rounded-md border border-border">
          {(
            [
              ["separate", "Отдельно"],
              ["combined", "Вместе"],
            ] as Array<[ChartMode, string]>
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              aria-pressed={mode === key}
              onClick={() => update({ mode: key === "combined" ? "combined" : undefined })}
              className={`px-3 py-1.5 text-xs transition-colors ${
                mode === key
                  ? "bg-surface-raised text-foreground"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        {ranges.map((key) => (
          <button
            key={key}
            type="button"
            aria-pressed={key === range}
            onClick={() => update({ range: key })}
            className={`num rounded-md border px-3 py-1.5 text-xs transition-colors ${
              key === range
                ? "border-accent bg-surface-raised text-foreground"
                : "border-border text-muted-foreground hover:text-foreground"
            }`}
          >
            {rangeLabels[key]}
          </button>
        ))}
      </div>

      {range === "custom" ? (
        <CustomRangeForm
          from={from}
          to={to}
          onApply={(nextFrom, nextTo) => update({ from: nextFrom, to: nextTo })}
        />
      ) : null}

      {range === "custom" && !bounds ? (
        <div className="panel mt-4 px-4 py-4">
          <EmptyState
            title="Укажите период"
            description="Задайте дату и время начала и конца — история загрузится по этому интервалу."
          />
        </div>
      ) : mode === "combined" ? (
        <div className="panel mt-4 px-4 py-4">
          {withData.length > 0 ? (
            <Suspense fallback={<SkeletonLine className="h-80 w-full" />}>
              <CombinedChart series={withData} />
            </Suspense>
          ) : (
            <ChartPlaceholder pending={series.some((s) => s.isPending)} />
          )}
        </div>
      ) : (
        <div className="mt-4 grid gap-4 xl:grid-cols-2">
          {series.map((s) => (
            <div key={s.metricKey} className="panel px-4 py-4">
              <p className="text-sm font-medium">
                {metricLabel(s.metricKey)}
                <span className="ml-2 text-xs text-muted-foreground">{s.unit}</span>
              </p>
              {s.points.length > 0 ? (
                <>
                  <Suspense fallback={<SkeletonLine className="h-72 w-full" />}>
                    <MetricChart points={s.points} metricKey={s.metricKey} unit={s.unit} />
                  </Suspense>
                  <ChartSummary metricKey={s.metricKey} points={s.points} />
                </>
              ) : (
                <ChartPlaceholder pending={s.isPending} />
              )}
            </div>
          ))}
        </div>
      )}

      {mode === "combined" && withData.length > 0 ? (
        <div className="mt-3 space-y-1">
          {withData.map((s) => (
            <ChartSummary
              key={s.metricKey}
              metricKey={s.metricKey}
              points={s.points}
              prefix={`${metricLabel(s.metricKey)}: `}
            />
          ))}
        </div>
      ) : null}
    </section>
  );
}

function ChartPlaceholder({ pending }: { pending: boolean }) {
  if (pending) return <SkeletonLine className="h-72 w-full" />;
  return (
    <EmptyState
      title="История пока пуста"
      description="История появится, когда датчик начнёт передавать данные."
    />
  );
}

function ChartSummary({
  metricKey,
  points,
  prefix = "",
}: {
  metricKey: string;
  points: Array<{ min: number | null; avg: number | null; max: number | null }>;
  prefix?: string;
}) {
  const meta = getMetric(metricKey);
  const nums = (key: "min" | "avg" | "max") =>
    points.map((p) => p[key]).filter((v): v is number => v !== null && v !== undefined);
  const avg = nums("avg");
  const min = nums("min");
  const max = nums("max");
  if (avg.length === 0) return null;
  return (
    <p className="num mt-3 text-xs text-muted-foreground">
      {prefix}среднее {formatNumber(avg.reduce((a, b) => a + b, 0) / avg.length, meta.precision)}{" "}
      {meta.unit} · минимум {formatValue(metricKey, Math.min(...min))} · максимум{" "}
      {formatValue(metricKey, Math.max(...max))}
    </p>
  );
}

function dateFromIso(iso: string | undefined): Date | undefined {
  if (!iso) return undefined;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function timeFromIso(iso: string | undefined, fallback: string): string {
  const date = dateFromIso(iso);
  if (!date) return fallback;
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function dateWithTime(date: Date | undefined, value: string): Date | undefined {
  if (!date || !/^([01]\d|2[0-3]):[0-5]\d$/.test(value)) return undefined;
  const [hours, minutes] = value.split(":").map(Number);
  const next = new Date(date);
  next.setHours(hours ?? 0, minutes ?? 0, 0, 0);
  return next;
}

function CustomRangeForm({
  from,
  to,
  onApply,
}: {
  from?: string | undefined;
  to?: string | undefined;
  onApply: (from: string, to: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [dates, setDates] = useState<DateRange | undefined>(() => ({
    from: dateFromIso(from),
    to: dateFromIso(to),
  }));
  const [startTime, setStartTime] = useState(() => timeFromIso(from, "00:00"));
  const [endTime, setEndTime] = useState(() => timeFromIso(to, "23:59"));

  useEffect(() => {
    setDates({ from: dateFromIso(from), to: dateFromIso(to) });
    setStartTime(timeFromIso(from, "00:00"));
    setEndTime(timeFromIso(to, "23:59"));
  }, [from, to]);

  const start = dateWithTime(dates?.from, startTime);
  const end = dateWithTime(dates?.to, endTime);
  const invalid = Boolean(start && end && start >= end);
  const complete = Boolean(start && end);

  return (
    <form
      className="mt-3 flex flex-wrap items-end gap-3 rounded-md border border-border bg-surface px-3 py-3"
      onSubmit={(event) => {
        event.preventDefault();
        if (!start || !end || invalid) return;
        onApply(start.toISOString(), end.toISOString());
      }}
    >
      <div className="min-w-0 flex-1 basis-full sm:basis-72">
        <p className="mb-1 text-xs text-muted-foreground">Даты</p>
        <Popover open={open} onOpenChange={setOpen}>
          <PopoverTrigger asChild>
            <Button
              type="button"
              variant="outline"
              className="w-full justify-start bg-surface-raised px-3 text-left font-normal"
            >
              <CalendarDays aria-hidden />
              {dates?.from ? (
                dates.to ? (
                  <span className="truncate">
                    {format(dates.from, "d MMM yyyy", { locale: ru })} —{" "}
                    {format(dates.to, "d MMM yyyy", { locale: ru })}
                  </span>
                ) : (
                  format(dates.from, "d MMMM yyyy", { locale: ru })
                )
              ) : (
                <span className="text-muted-foreground">Выберите начало и конец</span>
              )}
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-auto p-0" align="start">
            <Calendar
              mode="range"
              selected={dates}
              onSelect={setDates}
              locale={ru}
              {...(dates?.from ? { defaultMonth: dates.from } : {})}
              className="pointer-events-auto p-3"
            />
          </PopoverContent>
        </Popover>
      </div>

      <label className="flex min-w-28 flex-col gap-1 text-xs text-muted-foreground">
        Время с
        <span className="relative">
          <Clock
            className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2"
            aria-hidden
          />
          <Input
            inputMode="numeric"
            value={startTime}
            onChange={(event) => setStartTime(event.target.value)}
            placeholder="00:00"
            aria-label="Время начала"
            className="num bg-surface-raised pl-9"
          />
        </span>
      </label>
      <label className="flex min-w-28 flex-col gap-1 text-xs text-muted-foreground">
        Время по
        <span className="relative">
          <Clock
            className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2"
            aria-hidden
          />
          <Input
            inputMode="numeric"
            value={endTime}
            onChange={(event) => setEndTime(event.target.value)}
            placeholder="23:59"
            aria-label="Время окончания"
            className="num bg-surface-raised pl-9"
          />
        </span>
      </label>
      <Button type="submit" size="sm" disabled={!complete || invalid}>
        Показать
      </Button>
      {invalid ? (
        <p className="basis-full text-xs text-offline">Начало должно быть раньше конца.</p>
      ) : null}
    </form>
  );
}

function Diagnostics({
  slug,
  protocol,
  pollIntervalSeconds,
}: {
  slug: string;
  protocol: string;
  pollIntervalSeconds: number;
}) {
  const { data } = useStatus(slug, pollIntervalSeconds);

  const rows: Array<[string, string]> = [
    ["RSSI", data?.lastRssi != null ? `${formatNumber(data.lastRssi, 1)} dBm` : "—"],
    ["SNR", data?.lastSnr != null ? `${formatNumber(data.lastSnr, 2)} dB` : "—"],
    ["Последний опрос", formatDateTime(data?.lastPollAt)],
    ["Последний успешный", formatDateTime(data?.lastSuccessAt)],
    ["Неудач подряд", data ? String(data.consecutiveFailures) : "—"],
    ["Интервал опроса", `${pollIntervalSeconds} с`],
    ["Протокол", protocol],
  ];

  return (
    <section className="mt-12 rounded-xl border border-dashed border-border bg-surface/50 px-5 py-5">
      <h2 className="text-sm font-semibold uppercase tracking-widest text-muted-foreground">
        Диагностика канала
      </h2>
      <dl className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="num mt-1 text-sm">{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function NotFound() {
  return (
    <PageShell>
      <p className="eyebrow">404</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">Датчик не найден</h1>
      <p className="mt-3 text-muted-foreground">Такого датчика нет в публичном реестре MeshSMO.</p>
      <Link
        to="/sensors"
        className="mt-6 inline-flex rounded-lg bg-accent px-5 py-2.5 text-sm font-semibold text-accent-foreground"
      >
        Ко всем датчикам
      </Link>
    </PageShell>
  );
}
