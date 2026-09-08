import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceArea,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useId } from "react";
import { useTranslation } from "react-i18next";
import { formatChartTime } from "@/i18n/formatters";
import type { ForecastResponse, MeasurementPoint } from "@/lib/api";
import { getMetric, type MetricChartOptions, type MetricChartZone } from "@/lib/metrics";
import { formatDateTime, formatValue } from "@/lib/format";

type Row = {
  t: number;
  avg: number | null;
  band: [number, number] | null;
  forecast: number | null;
  forecastBand: [number, number] | null;
};

type NumericDomain = [number, number];

function chartDomain(
  rows: Row[],
  chart: MetricChartOptions | undefined,
): NumericDomain | undefined {
  if (!chart) return undefined;

  const values: number[] = [];
  for (const row of rows) {
    if (row.avg !== null) values.push(row.avg);
    if (row.forecast !== null) values.push(row.forecast);
    if (row.band) values.push(...row.band);
    if (row.forecastBand) values.push(...row.forecastBand);
  }
  for (const zone of chart.zones ?? []) {
    if (zone.from !== undefined) values.push(zone.from);
    if (zone.to !== undefined) values.push(zone.to);
  }
  if (chart.minimum !== undefined) values.push(chart.minimum);
  if (chart.maximum !== undefined) values.push(chart.maximum);
  if (values.length === 0) return undefined;

  const minimum = Math.min(...values);
  const maximum = Math.max(...values);
  const span = maximum - minimum || Math.max(Math.abs(maximum) * 0.1, 1);
  const padding = span * 0.08;
  const lowerBound = chart.minimum ?? minimum - padding;
  const upperBound = chart.maximum ?? maximum + padding;
  return lowerBound < upperBound ? [lowerBound, upperBound] : undefined;
}

function zoneGradientStops(zones: readonly MetricChartZone[], domain: NumericDomain) {
  const [minimum, maximum] = domain;
  const span = maximum - minimum;
  return zones.flatMap((zone, index) => {
    const from = Math.max(minimum, zone.from ?? minimum);
    const to = Math.min(maximum, zone.to ?? maximum);
    if (from >= to) return [];
    return [
      { key: `${index}-from`, offset: ((from - minimum) / span) * 100, color: zone.color },
      { key: `${index}-to`, offset: ((to - minimum) / span) * 100, color: zone.color },
    ];
  });
}

export default function MetricChart({
  points,
  metricKey,
  unit,
  forecast,
}: {
  points: MeasurementPoint[];
  metricKey: string;
  unit: string | null;
  forecast?: ForecastResponse | undefined;
}) {
  const { t } = useTranslation();
  const meta = getMetric(metricKey);
  const gradientId = `metric-zones-${useId().replace(/:/g, "")}`;
  const rows: Row[] = points.map((p) => ({
    t: new Date(p.timestamp).getTime(),
    avg: p.avg,
    band: p.min !== null && p.max !== null ? ([p.min, p.max] as [number, number]) : null,
    forecast: null,
    forecastBand: null,
  }));
  if (forecast?.availability === "ready" && forecast.points.length > 0) {
    const lastActual = rows.at(-1);
    const firstForecastTime = new Date(forecast.points[0]!.timestamp).getTime();
    const stepMilliseconds = Number.parseFloat(forecast.range.step) * 60_000;
    if (
      lastActual?.avg !== null &&
      lastActual?.avg !== undefined &&
      firstForecastTime - lastActual.t <= stepMilliseconds * 2
    ) {
      lastActual.forecast = lastActual.avg;
      lastActual.forecastBand = [lastActual.avg, lastActual.avg];
    }
    rows.push(
      ...forecast.points.map((point) => ({
        t: new Date(point.timestamp).getTime(),
        avg: null,
        band: null,
        forecast: point.predicted,
        forecastBand: [point.lower, point.upper] as [number, number],
      })),
    );
  }
  const zones = meta.chart?.zones ?? [];
  const domain = chartDomain(rows, meta.chart);
  const gradientStops = domain ? zoneGradientStops(zones, domain) : [];
  const lineColor = gradientStops.length > 0 ? `url(#${gradientId})` : meta.color;

  return (
    <div className="history-metric-chart h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          {domain ? (
            <defs>
              <linearGradient id={gradientId} x1="0" y1="100%" x2="0" y2="0%">
                {gradientStops.map((stop) => (
                  <stop key={stop.key} offset={`${stop.offset}%`} stopColor={stop.color} />
                ))}
              </linearGradient>
            </defs>
          ) : null}
          {domain
            ? zones.map((zone, index) => {
                const y1 = Math.max(domain[0], zone.from ?? domain[0]);
                const y2 = Math.min(domain[1], zone.to ?? domain[1]);
                return y1 < y2 ? (
                  <ReferenceArea
                    key={`${index}-${zone.label}`}
                    y1={y1}
                    y2={y2}
                    fill={zone.color}
                    fillOpacity={0.045}
                    stroke="none"
                    ifOverflow="hidden"
                  />
                ) : null;
              })
            : null}
          <CartesianGrid stroke="var(--border)" vertical={false} />
          <XAxis
            dataKey="t"
            type="number"
            domain={["dataMin", "dataMax"]}
            scale="time"
            tickFormatter={formatChartTime}
            stroke="var(--muted-foreground)"
            fontSize={11}
          />
          <YAxis
            {...(domain ? { domain } : {})}
            allowDataOverflow={domain !== undefined}
            stroke="var(--muted-foreground)"
            fontSize={11}
            width={48}
            tickFormatter={(v: number) => formatValue(metricKey, v)}
          />
          {domain
            ? zones
                .slice(1)
                .map((zone) =>
                  zone.from !== undefined && zone.from > domain[0] && zone.from < domain[1] ? (
                    <ReferenceLine
                      key={`${zone.from}-${zone.label}`}
                      y={zone.from}
                      stroke={zone.color}
                      strokeOpacity={0.65}
                      strokeDasharray="5 5"
                      ifOverflow="hidden"
                    />
                  ) : null,
                )
            : null}
          <Tooltip
            contentStyle={{
              background: "var(--surface-raised)",
              border: "1px solid var(--border)",
              borderRadius: "8px",
              color: "var(--foreground)",
              fontSize: "12px",
            }}
            labelFormatter={(v) => formatDateTime(new Date(Number(v)).toISOString())}
            formatter={(value: unknown, name: string) => {
              if (Array.isArray(value)) {
                const label =
                  name === t("charts.forecastInterval") ? name : t("charts.minimumMaximum");
                return [
                  `${formatValue(metricKey, value[0] as number)} … ${formatValue(metricKey, value[1] as number)} ${unit ?? meta.unit}`,
                  label,
                ];
              }
              return [`${formatValue(metricKey, value as number)} ${unit ?? meta.unit}`, name];
            }}
          />
          <Area
            dataKey="band"
            stroke="none"
            fill={lineColor}
            fillOpacity={0.15}
            isAnimationActive={false}
            name={t("charts.minimumMaximum")}
          />
          <Area
            dataKey="forecastBand"
            stroke="none"
            fill={lineColor}
            fillOpacity={0.1}
            isAnimationActive={false}
            name={t("charts.forecastInterval")}
          />
          <Line
            dataKey="avg"
            stroke={lineColor}
            strokeWidth={2}
            dot={false}
            isAnimationActive={false}
            name={t("charts.average")}
          />
          <Line
            dataKey="forecast"
            stroke={lineColor}
            strokeWidth={2}
            strokeDasharray="5 5"
            dot={false}
            isAnimationActive={false}
            name={t("charts.forecast")}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
