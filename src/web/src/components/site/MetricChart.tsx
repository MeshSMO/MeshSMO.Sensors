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
import type { MeasurementPoint } from "@/lib/api";
import { getMetric } from "@/lib/metrics";
import { formatDateTime, formatValue } from "@/lib/format";

type Row = {
  t: number;
  avg: number | null;
  band: [number, number] | null;
};

export default function MetricChart({
  points,
  metricKey,
  unit,
}: {
  points: MeasurementPoint[];
  metricKey: string;
  unit: string | null;
}) {
  const meta = getMetric(metricKey);
  const rows: Row[] = points.map((p) => ({
    t: new Date(p.timestamp).getTime(),
    avg: p.avg,
    band: p.min !== null && p.max !== null ? ([p.min, p.max] as [number, number]) : null,
  }));

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
                return [
                  `${formatValue(metricKey, value[0] as number)} … ${formatValue(metricKey, value[1] as number)} ${unit ?? meta.unit}`,
                  "мин–макс",
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
          <Line
            dataKey="avg"
            stroke={meta.color}
            strokeWidth={2}
            dot={false}
            isAnimationActive={false}
            name="среднее"
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
