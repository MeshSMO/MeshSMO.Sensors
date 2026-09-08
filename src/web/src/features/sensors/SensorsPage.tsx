import { Link } from "@tanstack/react-router";
import { Star } from "lucide-react";
import { useTranslation } from "react-i18next";
import { EmptyState } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useLatest, useSensors } from "@/lib/api";
import { useFavoriteMetrics } from "@/lib/favorite-metrics";
import { useFavoriteSensors } from "@/lib/favorite-sensors";
import { formatCoordinate, formatReading, normalizeState } from "@/lib/format";
import { getMetric, metricLabel } from "@/lib/metrics";
import { sensorRegistry, type RegistrySensor } from "@/lib/registry";

export function SensorsPage() {
  const { t } = useTranslation();
  const { data } = useSensors();
  const { favorites, toggleFavorite } = useFavoriteSensors();
  const { favorites: favoriteMetrics } = useFavoriteMetrics();
  const live = new Map(
    (data?.sensors ?? []).map((sensor) => [sensor.slug, normalizeState(sensor.state)]),
  );
  const orderedSensors = [...sensorRegistry].sort(
    (left, right) => Number(favorites.has(right.slug)) - Number(favorites.has(left.slug)),
  );

  return (
    <>
      <p className="eyebrow">{t("sensors.eyebrow")}</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">{t("sensors.title")}</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">{t("sensors.description")}</p>

      {sensorRegistry.length === 0 ? (
        <div className="mt-8">
          <EmptyState title={t("sensors.emptyTitle")} description={t("sensors.emptyDescription")} />
        </div>
      ) : (
        <ul className="mt-8 grid gap-4 sm:grid-cols-2">
          {orderedSensors.map((sensor) => (
            <SensorCard
              key={sensor.slug}
              sensor={sensor}
              state={live.get(sensor.slug) ?? null}
              isFavorite={favorites.has(sensor.slug)}
              favoriteMetrics={[...(favoriteMetrics.get(sensor.slug) ?? [])]}
              onToggleFavorite={() => toggleFavorite(sensor.slug)}
            />
          ))}
        </ul>
      )}
    </>
  );
}

function SensorCard({
  sensor,
  state,
  isFavorite,
  favoriteMetrics,
  onToggleFavorite,
}: {
  sensor: RegistrySensor;
  state: ReturnType<typeof normalizeState> | null;
  isFavorite: boolean;
  favoriteMetrics: string[];
  onToggleFavorite: () => void;
}) {
  const { t } = useTranslation();

  return (
    <li className="panel relative flex h-full transition-colors hover:bg-surface-raised">
      <Link
        to="/sensors/$slug"
        params={{ slug: sensor.slug }}
        className="flex min-w-0 flex-1 flex-col gap-3 px-5 py-5 pr-16"
      >
        <span className="flex items-start justify-between gap-3">
          <span className="text-lg font-medium">{sensor.displayName}</span>
          <StatusBadge state={state} />
        </span>
        <span className="text-sm text-muted-foreground">{sensor.description}</span>
        <span className="num text-xs text-muted-foreground">
          {sensor.location
            ? t("common.coordinates.pair", {
                latitude: formatCoordinate(sensor.location.latitude),
                longitude: formatCoordinate(sensor.location.longitude),
              })
            : t("common.coordinates.unavailable")}
        </span>
        <MetricTags sensor={sensor} favorites={favoriteMetrics} />
      </Link>
      <button
        type="button"
        aria-label={
          isFavorite ? t("common.actions.removeFavorite") : t("common.actions.addFavorite")
        }
        aria-pressed={isFavorite}
        title={isFavorite ? t("common.actions.removeFavorite") : t("common.actions.addFavorite")}
        className="absolute top-4 right-4 inline-flex size-9 cursor-pointer items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        onClick={onToggleFavorite}
      >
        <Star
          aria-hidden="true"
          className={isFavorite ? "fill-amber-400 text-amber-400" : undefined}
        />
      </button>
    </li>
  );
}

function MetricTags({ sensor, favorites }: { sensor: RegistrySensor; favorites: string[] }) {
  return favorites.length > 0 ? (
    <MetricTagsWithFavorites sensor={sensor} favorites={favorites} />
  ) : (
    <StaticMetricTags metrics={sensor.metrics} />
  );
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
                {meta.kind === "numeric" ? ` ${value?.unit ?? meta.unit}` : ""}
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
