import { lazy, Suspense } from "react";
import { useTranslation } from "react-i18next";
import { TriangleAlert } from "lucide-react";
import { EmptyState, SkeletonLine } from "@/components/site/Shell";
import HistoryChartGrid from "@/components/site/HistoryChartGrid";
import type { ForecastResponse, MeasurementPoint } from "@/lib/api";
import { formatNumber, formatValue } from "@/lib/format";
import { getMetric, metricLabel } from "@/lib/metrics";
import type { ChartMode } from "../route-config";

const MetricChart = lazy(() => import("@/components/site/MetricChart"));
const CombinedChart = lazy(() => import("@/components/site/CombinedChart"));

type HistorySeries = {
  metricKey: string;
  unit: string | null;
  points: MeasurementPoint[];
  isPending: boolean;
};

export function HistoryCharts({
  slug,
  mode,
  series,
  forecast,
}: {
  slug: string;
  mode: ChartMode;
  series: HistorySeries[];
  forecast?: ForecastResponse | undefined;
}) {
  const populatedSeries = series.filter(({ points }) => points.length > 0);

  if (mode === "combined") {
    return (
      <>
        <div className="panel mt-4 px-4 py-4">
          {populatedSeries.length > 0 ? (
            <Suspense fallback={<SkeletonLine className="h-80 w-full" />}>
              <CombinedChart series={populatedSeries} />
            </Suspense>
          ) : (
            <ChartPlaceholder pending={series.some((item) => item.isPending)} />
          )}
        </div>
        {populatedSeries.length > 0 ? (
          <div className="mt-3 space-y-1">
            {populatedSeries.map((item) => (
              <div key={item.metricKey}>
                <ChartSummary
                  metricKey={item.metricKey}
                  points={item.points}
                  prefix={`${metricLabel(item.metricKey)}: `}
                />
                <AnomalySummary points={item.points} />
              </div>
            ))}
          </div>
        ) : null}
      </>
    );
  }

  return (
    <HistoryChartGrid
      slug={slug}
      items={series.map((item) => ({
        id: item.metricKey,
        title: metricLabel(item.metricKey),
        unit: item.unit,
        content:
          item.points.length > 0 ? (
            <>
              <Suspense fallback={<SkeletonLine className="h-72 min-h-72 w-full flex-1" />}>
                <MetricChart
                  points={item.points}
                  metricKey={item.metricKey}
                  unit={item.unit}
                  forecast={forecast}
                />
              </Suspense>
              <ChartSummary metricKey={item.metricKey} points={item.points} />
              <AnomalySummary points={item.points} />
            </>
          ) : (
            <ChartPlaceholder pending={item.isPending} />
          ),
      }))}
    />
  );
}

function AnomalySummary({ points }: { points: MeasurementPoint[] }) {
  const { t } = useTranslation();
  const code = points.find((point) => point.anomaly)?.anomaly?.code;
  if (code === undefined) return null;

  const count = points.filter((point) => point.anomaly?.code === code).length;

  const message = (() => {
    switch (code) {
      case "temperature_outlier":
        return t("history.temperatureAnomalyDetected", { count });
      case "humidity_outlier":
        return t("history.humidityAnomalyDetected", { count });
      default:
        return t("history.nightVoltageAnomalyDetected", { count });
    }
  })();
  const hint =
    code === "unexpected_night_voltage"
      ? t("history.unexpectedNightVoltageHint")
      : t("history.robustAnomalyHint");

  return (
    <div className="mt-3 flex items-start gap-2.5 rounded-lg border border-amber-400/25 bg-gradient-to-r from-amber-400/10 to-amber-400/3 px-3 py-2.5 text-xs leading-relaxed">
      <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-amber-400/15 text-amber-400">
        <TriangleAlert aria-hidden="true" className="size-3.5" />
      </span>
      <div className="min-w-0">
        <p className="font-medium text-foreground">{message}</p>
        <p className="mt-0.5 text-muted-foreground">{hint}</p>
      </div>
    </div>
  );
}

function ChartPlaceholder({ pending }: { pending: boolean }) {
  const { t } = useTranslation();

  return pending ? (
    <SkeletonLine className="h-72 w-full" />
  ) : (
    <EmptyState title={t("history.emptyTitle")} description={t("history.emptyDescription")} />
  );
}

function ChartSummary({
  metricKey,
  points,
  prefix = "",
}: {
  metricKey: string;
  points: MeasurementPoint[];
  prefix?: string;
}) {
  const { t } = useTranslation();
  const meta = getMetric(metricKey);
  const values = (key: "min" | "avg" | "max") =>
    points.map((point) => point[key]).filter((value): value is number => value !== null);
  const averages = values("avg");
  const minimums = values("min");
  const maximums = values("max");
  if (averages.length === 0 || minimums.length === 0 || maximums.length === 0) return null;

  return (
    <p className="num mt-3 text-xs text-muted-foreground">
      {t("charts.summary", {
        prefix,
        average: formatNumber(
          averages.reduce((sum, value) => sum + value, 0) / averages.length,
          meta.precision,
        ),
        unit: meta.unit,
        minimum: formatValue(metricKey, Math.min(...minimums)),
        maximum: formatValue(metricKey, Math.max(...maximums)),
      })}
    </p>
  );
}
