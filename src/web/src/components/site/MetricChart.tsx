import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { ForecastResponse, MeasurementPoint } from "@/lib/api";
import { getMetric } from "@/lib/metrics";
import { formatDateTime, formatValue } from "@/lib/format";

type Row = {
  t: number;
  avg: number | null;
  band: [number, number] | null;
  forecast: number | null;
  forecastBand: [number, number] | null;
};

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
  const meta = getMetric(metricKey);
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

  return (
    <div className="h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid stroke="var(--border)" vertical={false} />
          <XAxis
            dataKey="t"
            type="number"
            domain={["dataMin", "dataMax"]}
            scale="time"
            tickFormatter={(v: number) =>
              new Intl.DateTimeFormat("ru-RU", {
                day: "2-digit",
                month: "2-digit",
                hour: "2-digit",
                minute: "2-digit",
              }).format(new Date(v))
            }
            stroke="var(--muted-foreground)"
            fontSize={11}
          />
          <YAxis
            stroke="var(--muted-foreground)"
            fontSize={11}
            width={48}
            tickFormatter={(v: number) => formatValue(metricKey, v)}
          />
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
                const label = name === "интервал прогноза" ? name : "мин–макс";
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
            fill={meta.color}
            fillOpacity={0.15}
            isAnimationActive={false}
            name="мин–макс"
          />
          <Area
            dataKey="forecastBand"
            stroke="none"
            fill={meta.color}
            fillOpacity={0.1}
            isAnimationActive={false}
            name="интервал прогноза"
          />
          <Line
            dataKey="avg"
            stroke={meta.color}
            strokeWidth={2}
            dot={false}
            isAnimationActive={false}
            name="среднее"
          />
          <Line
            dataKey="forecast"
            stroke={meta.color}
            strokeWidth={2}
            strokeDasharray="5 5"
            dot={false}
            isAnimationActive={false}
            name="прогноз"
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
