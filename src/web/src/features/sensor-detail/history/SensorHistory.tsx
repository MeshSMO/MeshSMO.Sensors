import { getRouteApi } from "@tanstack/react-router";
import { useCallback } from "react";
import { useTranslation } from "react-i18next";
import { EmptyState } from "@/components/site/Shell";
import {
  resolveRange,
  useForecast,
  useMeasurementsMany,
  type ForecastAvailability,
} from "@/lib/api";
import { getMetric } from "@/lib/metrics";
import type { SensorSearch } from "../route-config";
import { CustomRangeForm } from "./CustomRangeForm";
import { HistoryCharts } from "./HistoryCharts";
import { HistoryControls } from "./HistoryControls";
import { useHistoryPreferences, type SearchPatch } from "./history-preferences";

const sensorRoute = getRouteApi("/sensors/$slug");

export function SensorHistory({
  slug,
  metrics,
  selected,
  search,
}: {
  slug: string;
  metrics: string[];
  selected: string[];
  search: SensorSearch;
}) {
  const { t } = useTranslation();
  const navigate = sensorRoute.useNavigate();
  const range = search.range ?? "24h";
  const mode = search.mode ?? "separate";
  const updateSearch = useCallback(
    (patch: SearchPatch) =>
      navigate({
        search: (previous) => {
          const next: Record<string, unknown> = { ...previous };
          for (const [key, value] of Object.entries(patch)) {
            if (value === undefined) delete next[key];
            else next[key] = value;
          }
          return next as SensorSearch;
        },
        replace: true,
        resetScroll: false,
      }),
    [navigate],
  );

  useHistoryPreferences({
    slug,
    hasExplicitSearch: Object.values(search).some((value) => value !== undefined),
    selected,
    range,
    mode,
    search,
    updateSearch,
  });

  const customRange = { from: search.from, to: search.to };
  const results = useMeasurementsMany(slug, selected, range, customRange);
  const bounds = resolveRange(range, customRange);
  const series = selected.map((metricKey, index) => ({
    metricKey,
    unit: results[index]?.data?.metric.unit ?? getMetric(metricKey).unit,
    points: results[index]?.data?.points ?? [],
    isPending: results[index]?.isPending ?? false,
  }));
  const forecastEnabled =
    search.forecast !== undefined && mode === "separate" && selected.length === 1;
  const forecastQuery = useForecast(slug, selected[0], search.forecast ?? "24h", forecastEnabled);

  const toggleMetric = (metric: string) => {
    const next = selected.includes(metric)
      ? selected.filter((candidate) => candidate !== metric)
      : [...selected, metric];
    void updateSearch({
      metrics: (next.length > 0 ? next : [metric]).join(","),
      metric: undefined,
    });
  };

  return (
    <section className="mt-10">
      <h2 className="text-xl font-semibold tracking-tight">{t("history.title")}</h2>
      <HistoryControls
        metrics={metrics}
        selected={selected}
        range={range}
        mode={mode}
        forecast={search.forecast}
        forecastEnabled={forecastEnabled}
        onToggleMetric={toggleMetric}
        onUpdate={(patch) => void updateSearch(patch)}
      />

      {forecastEnabled ? (
        <p className="mt-2 text-xs text-muted-foreground" aria-live="polite">
          {forecastQuery.isPending
            ? t("history.forecastPending")
            : forecastQuery.isError
              ? t("history.forecastError")
              : forecastQuery.data?.availability === "ready"
                ? t("history.forecastReady")
                : forecastAvailabilityMessage(forecastQuery.data?.availability, t)}
        </p>
      ) : null}

      {range === "custom" ? (
        <CustomRangeForm
          from={search.from}
          to={search.to}
          onApply={(from, to) => void updateSearch({ from, to })}
        />
      ) : null}

      {range === "custom" && !bounds ? (
        <div className="panel mt-4 px-4 py-4">
          <EmptyState
            title={t("history.custom.emptyTitle")}
            description={t("history.custom.emptyDescription")}
          />
        </div>
      ) : (
        <HistoryCharts
          slug={slug}
          mode={mode}
          series={series}
          forecast={forecastEnabled ? forecastQuery.data : undefined}
        />
      )}
    </section>
  );
}

function forecastAvailabilityMessage(
  availability: ForecastAvailability | undefined,
  t: ReturnType<typeof useTranslation>["t"],
): string {
  switch (availability) {
    case "insufficient_data":
      return t("history.forecastAvailability.insufficient_data");
    case "sparse_data":
      return t("history.forecastAvailability.sparse_data");
    case "stale_data":
      return t("history.forecastAvailability.stale_data");
    case "low_quality":
      return t("history.forecastAvailability.low_quality");
    case "disabled":
      return t("history.forecastAvailability.disabled");
    default:
      return t("history.forecastAvailability.unknown");
  }
}
