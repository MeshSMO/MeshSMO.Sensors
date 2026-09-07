import { lazy, Suspense, useState } from "react";
import { Link } from "@tanstack/react-router";
import {
  Drawer,
  DrawerContent,
  DrawerDescription,
  DrawerHeader,
  DrawerTitle,
} from "@/components/ui/drawer";
import { StatusBadge } from "@/components/site/StatusBadge";
import { SkeletonLine } from "@/components/site/Shell";
import { rangeLabels, useLatest, useMeasurements, useStatus, type RangeKey } from "@/lib/api";
import { getRegistrySensor } from "@/lib/registry";
import { getMetric, metricLabel } from "@/lib/metrics";
import {
  formatCoordinate,
  formatDateTime,
  formatNumber,
  formatReading,
  normalizeState,
  type SensorState,
} from "@/lib/format";

const MetricChart = lazy(() => import("@/components/site/MetricChart"));

const drawerRanges: RangeKey[] = ["24h", "7d", "30d"];

export function SensorDrawer({
  slug,
  state,
  onClose,
}: {
  slug: string | null;
  state: SensorState | null;
  onClose: () => void;
}) {
  return (
    <Drawer open={slug !== null} onOpenChange={(open) => !open && onClose()}>
      <DrawerContent className="max-h-[88vh]">
        {slug ? <DrawerBody slug={slug} state={state} /> : null}
      </DrawerContent>
    </Drawer>
  );
}

function DrawerBody({ slug, state }: { slug: string; state: SensorState | null }) {
  const registry = getRegistrySensor(slug);
  const poll = registry?.pollIntervalSeconds ?? undefined;
  const latest = useLatest(slug, poll);
  const status = useStatus(slug, poll);
  const metrics = registry?.metrics ?? [];
  const numericMetrics = metrics.filter((m) => getMetric(m).kind === "numeric");
  const [metric, setMetric] = useState(numericMetrics[0] ?? metrics[0] ?? "temperature");
  const [range, setRange] = useState<RangeKey>("24h");
  const history = useMeasurements(slug, metric, range);
  const liveState = status.data ? normalizeState(status.data.state) : state;
  const values = new Map((latest.data?.values ?? []).map((v) => [v.metric, v]));
  const shownMetrics = metrics.slice(0, 6);

  return (
    <div className="mx-auto w-full max-w-3xl overflow-y-auto px-4 pb-8 sm:px-6">
      <DrawerHeader className="px-0">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <DrawerTitle className="text-xl">{registry?.displayName ?? slug}</DrawerTitle>
          <StatusBadge state={liveState} />
        </div>
        <DrawerDescription className="text-left">
          {registry?.description ?? "Датчик сети MeshSMO."}
        </DrawerDescription>
      </DrawerHeader>

      {registry?.location ? (
        <p className="num text-xs text-muted-foreground">
          {formatCoordinate(registry.location.latitude)},{" "}
          {formatCoordinate(registry.location.longitude)}
        </p>
      ) : null}

      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
        {shownMetrics.map((key) => {
          const meta = getMetric(key);
          const value = values.get(key);
          return (
            <div key={key} className="panel px-3 py-3">
              <p className="text-xs text-muted-foreground">{value?.displayName ?? meta.label}</p>
              <p className="num mt-1 break-words text-xl">
                {latest.isPending && !latest.isError ? (
                  <SkeletonLine className="h-6 w-16" />
                ) : (
                  <>
                    {formatReading(key, value?.numericValue, value?.textValue)}{" "}
                    {meta.kind === "numeric" ? (
                      <span className="text-sm text-muted-foreground">
                        {value?.unit ?? meta.unit}
                      </span>
                    ) : null}
                  </>
                )}
              </p>
            </div>
          );
        })}
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <select
          value={metric}
          onChange={(event) => setMetric(event.target.value)}
          className="rounded-md border border-border bg-surface-raised px-2 py-1.5 text-sm"
          aria-label="Показатель для графика"
        >
          {(numericMetrics.length > 0 ? numericMetrics : metrics).map((key) => (
            <option key={key} value={key}>
              {metricLabel(key)}
            </option>
          ))}
        </select>
        <div className="flex gap-1">
          {drawerRanges.map((key) => (
            <button
              key={key}
              type="button"
              onClick={() => setRange(key)}
              aria-pressed={range === key}
              className={`rounded-md border px-2.5 py-1.5 text-xs transition-colors ${
                range === key
                  ? "border-accent bg-surface text-foreground"
                  : "border-border text-muted-foreground hover:text-foreground"
              }`}
            >
              {rangeLabels[key]}
            </button>
          ))}
        </div>
      </div>

      <div className="panel mt-3 h-64 px-2 py-3">
        {history.isPending ? (
          <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
            Загружаем историю…
          </div>
        ) : history.isError || (history.data?.points.length ?? 0) === 0 ? (
          <div className="flex h-full items-center justify-center px-4 text-center text-sm text-muted-foreground">
            История пока недоступна — измерений за выбранный период нет.
          </div>
        ) : (
          <Suspense fallback={<div className="h-full" />}>
            <MetricChart
              points={history.data!.points}
              metricKey={metric}
              unit={history.data!.metric.unit}
            />
          </Suspense>
        )}
      </div>

      <dl className="mt-6 grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <Fact label="Последний опрос" value={formatDateTime(status.data?.lastPollAt)} />
        <Fact label="RSSI" value={formatNumber(status.data?.lastRssi ?? null)} />
        <Fact label="SNR" value={formatNumber(status.data?.lastSnr ?? null)} />
        <Fact
          label="Неудач подряд"
          value={formatNumber(status.data?.consecutiveFailures ?? null)}
        />
      </dl>

      <div className="mt-6">
        <Link
          to="/sensors/$slug"
          params={{ slug }}
          className="inline-flex items-center justify-center rounded-md border border-accent bg-surface px-4 py-2 text-sm font-medium transition-colors hover:bg-surface-raised"
        >
          Открыть страницу датчика
        </Link>
      </div>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="panel px-3 py-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="num mt-1">{value}</dd>
    </div>
  );
}
