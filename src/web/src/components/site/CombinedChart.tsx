import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { MeasurementPoint } from "@/lib/api";
import { formatChartTime } from "@/i18n/formatters";
import { getMetric } from "@/lib/metrics";
import { formatDateTime, formatValue } from "@/lib/format";

export type ChartSeries = {
  metricKey: string;
  unit: string | null;
  points: MeasurementPoint[];
};

type AxisDomain = [number | "auto", number | "auto"];

function chartDomainForUnit(
  series: ChartSeries[],
  unit: string | undefined,
): AxisDomain | undefined {
  if (unit === undefined) return undefined;
  const definitions = series
    .filter((item) => (item.unit ?? getMetric(item.metricKey).unit) === unit)
    .map((item) => getMetric(item.metricKey));
  if (definitions.length === 0) return undefined;

  const minimums = definitions.map((definition) => definition.chart?.minimum);
  const maximums = definitions.map((definition) => definition.chart?.maximum);
  const minimum = minimums.every((value) => value !== undefined)
    ? Math.min(...(minimums as number[]))
    : "auto";
  const maximum = maximums.every((value) => value !== undefined)
    ? Math.max(...(maximums as number[]))
    : "auto";
  return minimum === "auto" && maximum === "auto" ? undefined : [minimum, maximum];
}

/** Несколько метрик на одном полотне. Ось Y — до двух групп по единицам измерения. */
export default function CombinedChart({ series }: { series: ChartSeries[] }) {
  const units: string[] = [];
  for (const s of series) {
    const unit = s.unit ?? getMetric(s.metricKey).unit;
    if (!units.includes(unit)) units.push(unit);
  }
  const rightUnit = units[1];
  const leftDomain = chartDomainForUnit(series, units[0]);
  const rightDomain = chartDomainForUnit(series, rightUnit);

  type Row = { t: number } & Record<string, number | null>;
  const byTime = new Map<number, Row>();
  for (const s of series) {
    for (const p of s.points) {
      const t = new Date(p.timestamp).getTime();
      const row: Row = byTime.get(t) ?? { t };
      row[s.metricKey] = p.avg;
      byTime.set(t, row);
    }
  }
  const rows = [...byTime.values()].sort((a, b) => a.t - b.t);

  return (
    <div className="h-80 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
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
            yAxisId="left"
            {...(leftDomain ? { domain: leftDomain } : {})}
            allowDataOverflow={leftDomain !== undefined}
            stroke="var(--muted-foreground)"
            fontSize={11}
            width={52}
          />
          {rightUnit !== undefined ? (
            <YAxis
              yAxisId="right"
              {...(rightDomain ? { domain: rightDomain } : {})}
              allowDataOverflow={rightDomain !== undefined}
              orientation="right"
              stroke="var(--muted-foreground)"
              fontSize={11}
              width={52}
            />
          ) : null}
          <Tooltip
            contentStyle={{
              background: "var(--surface-raised)",
              border: "1px solid var(--border)",
              borderRadius: "8px",
              color: "var(--foreground)",
              fontSize: "12px",
            }}
            labelFormatter={(v) => formatDateTime(new Date(Number(v)).toISOString())}
            formatter={(value: unknown, name: unknown, item: unknown) => {
              const key = (item as { dataKey?: string })?.dataKey ?? "";
              const match = series.find((s) => s.metricKey === key);
              const unit = match ? (match.unit ?? getMetric(key).unit) : "";
              return [`${formatValue(key, value as number)} ${unit}`, name as string];
            }}
          />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          {series.map((s) => {
            const meta = getMetric(s.metricKey);
            const unit = s.unit ?? meta.unit;
            return (
              <Line
                key={s.metricKey}
                yAxisId={rightUnit !== undefined && unit === rightUnit ? "right" : "left"}
                dataKey={s.metricKey}
                stroke={meta.color}
                strokeWidth={2}
                dot={false}
                connectNulls
                isAnimationActive={false}
                name={`${meta.label}${unit ? `, ${unit}` : ""}`}
              />
            );
          })}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
