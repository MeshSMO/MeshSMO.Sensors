import { createFileRoute, Link } from "@tanstack/react-router";
import { Star } from "lucide-react";
import { EmptyState, PageShell } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useLatest, useSensors } from "@/lib/api";
import { useFavoriteMetrics } from "@/lib/favorite-metrics";
import { useFavoriteSensors } from "@/lib/favorite-sensors";
import { sensorRegistry, type RegistrySensor } from "@/lib/registry";
import { formatCoordinate, formatReading, normalizeState } from "@/lib/format";
import { getMetric, metricLabel } from "@/lib/metrics";

const title = "Публичные датчики MeshSMO — список и статусы | MeshSMO";
const description =
  "Список публичных LoRa-датчиков MeshSMO с текущими статусами: температура, влажность, давление, батарея.";

export const Route = createFileRoute("/sensors/")({
  head: () => ({
    meta: [
      { title },
      { name: "description", content: description },
      { property: "og:title", content: title },
      { property: "og:description", content: description },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
  }),
  component: SensorsPage,
});

function SensorsPage() {
  const { data } = useSensors();
  const { favorites, toggleFavorite } = useFavoriteSensors();
  const { favorites: favoriteMetrics } = useFavoriteMetrics();
  const live = new Map((data?.sensors ?? []).map((s) => [s.slug, normalizeState(s.state)]));
  const orderedSensors = [...sensorRegistry].sort(
    (left, right) => Number(favorites.has(right.slug)) - Number(favorites.has(left.slug)),
  );

  return (
    <PageShell>
      <p className="eyebrow">Реестр</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">Публичные датчики</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">
        Каждый датчик передаёт показания по радиосети MeshCore. Статус и последние значения
        подгружаются с сервера телеметрии; описание и перечень показателей доступны всегда.
      </p>

      {sensorRegistry.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="Пока ни один датчик не опубликован"
            description="Сеть разворачивается. Как только первый датчик выйдет в эфир, он появится здесь."
          />
        </div>
      ) : (
        <ul className="mt-8 grid gap-4 sm:grid-cols-2">
          {orderedSensors.map((sensor) => {
            const isFavorite = favorites.has(sensor.slug);

            return (
              <li
                key={sensor.slug}
                className="panel relative flex h-full transition-colors hover:bg-surface-raised"
              >
                <Link
                  to="/sensors/$slug"
                  params={{ slug: sensor.slug }}
                  className="flex min-w-0 flex-1 flex-col gap-3 px-5 py-5 pr-16"
                >
                  <span className="flex items-start justify-between gap-3">
                    <span className="text-lg font-medium">{sensor.displayName}</span>
                    <StatusBadge state={live.get(sensor.slug) ?? null} />
                  </span>
                  <span className="text-sm text-muted-foreground">{sensor.description}</span>
                  <span className="num text-xs text-muted-foreground">
                    {sensor.location
                      ? `${formatCoordinate(sensor.location.latitude)}, ${formatCoordinate(sensor.location.longitude)}`
                      : "Координаты не опубликованы"}
                  </span>
                  <MetricTags
                    sensor={sensor}
                    favorites={[...(favoriteMetrics.get(sensor.slug) ?? [])]}
                  />
                </Link>
                <button
                  type="button"
                  aria-label={isFavorite ? "Убрать из избранного" : "Добавить в избранное"}
                  aria-pressed={isFavorite}
                  title={isFavorite ? "Убрать из избранного" : "Добавить в избранное"}
                  className="absolute top-4 right-4 inline-flex size-9 cursor-pointer items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  onClick={() => toggleFavorite(sensor.slug)}
                >
                  <Star
                    aria-hidden="true"
                    className={isFavorite ? "fill-amber-400 text-amber-400" : undefined}
                  />
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </PageShell>
  );
}

function MetricTags({ sensor, favorites }: { sensor: RegistrySensor; favorites: string[] }) {
  if (favorites.length > 0) {
    return <MetricTagsWithFavorites sensor={sensor} favorites={favorites} />;
  }

  return <StaticMetricTags metrics={sensor.metrics} />;
}

function MetricTagsWithFavorites({
  sensor,
  favorites,
}: {
  sensor: RegistrySensor;
  favorites: string[];
}) {
  const { data } = useLatest(sensor.slug, sensor.pollIntervalSeconds ?? 60);
  const favoriteSet = new Set(favorites);
  const values = new Map((data?.values ?? []).map((value) => [value.metric, value]));
  const metrics = [...favorites, ...sensor.metrics.filter((metric) => !favoriteSet.has(metric))];

  return (
    <span className="mt-auto flex flex-wrap gap-2 pt-2">
      {metrics.map((metric) => {
        const isFavorite = favoriteSet.has(metric);
        const value = values.get(metric);
        const meta = getMetric(metric);
        const showUnit = meta.kind === "numeric";

        return (
          <span
            key={metric}
            className={
              isFavorite
                ? "inline-flex items-center gap-1.5 rounded-md border border-amber-400/40 bg-amber-400/10 px-2 py-1 text-xs text-foreground"
                : "rounded-md border border-border bg-surface-raised px-2 py-1 text-xs text-muted-foreground"
            }
          >
            {isFavorite ? (
              <Star aria-hidden="true" className="size-3 fill-amber-400 text-amber-400" />
            ) : null}
            <span>{value?.displayName ?? meta.label}</span>
            {isFavorite ? (
              <span className="num font-medium">
                {formatReading(metric, value?.numericValue, value?.textValue)}
                {showUnit ? ` ${value?.unit ?? meta.unit}` : ""}
              </span>
            ) : null}
          </span>
        );
      })}
    </span>
  );
}

function StaticMetricTags({ metrics }: { metrics: string[] }) {
  return (
    <span className="mt-auto flex flex-wrap gap-2 pt-2">
      {metrics.map((metric) => (
        <span
          key={metric}
          className="rounded-md border border-border bg-surface-raised px-2 py-1 text-xs text-muted-foreground"
        >
          {metricLabel(metric)}
        </span>
      ))}
    </span>
  );
}
