import { Star } from "lucide-react";
import { SkeletonLine } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useLatest, useSensor, useStatus } from "@/lib/api";
import { useFavoriteMetrics } from "@/lib/favorite-metrics";
import { formatDateTime, formatNumber, formatReading, normalizeState } from "@/lib/format";
import { getMetric } from "@/lib/metrics";

export function SensorHeader({ slug, fallbackName }: { slug: string; fallbackName: string }) {
  const { data } = useSensor(slug);

  return (
    <div className="mt-4 flex flex-wrap items-center gap-4">
      <h1 className="text-3xl font-semibold tracking-tight">{data?.displayName ?? fallbackName}</h1>
      <StatusBadge state={data ? normalizeState(data.state) : null} />
    </div>
  );
}

export function SensorReadings({
  slug,
  metrics,
  pollIntervalSeconds,
}: {
  slug: string;
  metrics: string[];
  pollIntervalSeconds: number;
}) {
  const { data, isPending, isError } = useLatest(slug, pollIntervalSeconds);
  const { favorites, toggleFavorite } = useFavoriteMetrics();
  const sensorFavorites = favorites.get(slug) ?? new Set<string>();
  const values = new Map((data?.values ?? []).map((value) => [value.metric, value]));
  const keys = [
    ...metrics,
    ...(data?.values ?? [])
      .map((value) => value.metric)
      .filter((metric) => !metrics.includes(metric)),
  ].sort((left, right) => Number(sensorFavorites.has(right)) - Number(sensorFavorites.has(left)));

  return (
    <section className="mt-10">
      <h2 className="text-xl font-semibold tracking-tight">Показания</h2>
      <div className="mt-4 grid grid-cols-2 gap-3 lg:grid-cols-4" aria-live="polite">
        {keys.map((key) => {
          const meta = getMetric(key);
          const value = values.get(key);
          const isFavorite = sensorFavorites.has(key);
          const label = value?.displayName ?? meta.label;

          return (
            <div key={key} className="panel relative px-4 py-4 pr-12">
              <p className="text-xs text-muted-foreground">{label}</p>
              <button
                type="button"
                aria-label={`${isFavorite ? "Убрать" : "Добавить"} показатель «${label}» ${isFavorite ? "из" : "в"} избранного`}
                aria-pressed={isFavorite}
                title={isFavorite ? "Убрать из избранного" : "Добавить в избранное"}
                className="absolute top-2.5 right-2.5 inline-flex size-8 cursor-pointer items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                onClick={() => toggleFavorite(slug, key)}
              >
                <Star
                  aria-hidden="true"
                  className={isFavorite ? "fill-amber-400 text-amber-400" : undefined}
                />
              </button>
              <p className="num mt-2 break-words text-3xl">
                {isPending && !isError ? (
                  <SkeletonLine className="h-8 w-20" />
                ) : (
                  <>
                    {formatReading(key, value?.numericValue, value?.textValue)}{" "}
                    {meta.kind === "numeric" ? (
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

export function SensorDiagnostics({
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
