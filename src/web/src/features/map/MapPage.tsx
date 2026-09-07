import { lazy, Suspense, useEffect, useState } from "react";
import { EmptyState } from "@/components/site/Shell";
import { SensorDrawer } from "@/components/site/SensorDrawer";
import type { MapPoint } from "@/components/site/SensorMap";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useSensors } from "@/lib/api";
import { formatCoordinate, normalizeState, type SensorState } from "@/lib/format";
import { sensorRegistry } from "@/lib/registry";

const SensorMap = lazy(() => import("@/components/site/SensorMap"));

export function MapPage() {
  const { data } = useSensors();
  const [mounted, setMounted] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  useEffect(() => setMounted(true), []);

  const live = new Map(
    (data?.sensors ?? []).map((sensor) => [sensor.slug, normalizeState(sensor.state)] as const),
  );
  const points: MapPoint[] = sensorRegistry
    .filter((sensor) => sensor.location !== null)
    .map((sensor) => ({
      slug: sensor.slug,
      displayName: sensor.displayName,
      latitude: sensor.location!.latitude,
      longitude: sensor.location!.longitude,
      state: live.get(sensor.slug) ?? null,
    }));
  const selectedState: SensorState | null = selected ? (live.get(selected) ?? null) : null;

  return (
    <>
      <p className="eyebrow">Карта</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">Датчики на карте</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">
        Расположение узлов сети MeshCore в Смоленской области. Нажмите на метку — откроется панель с
        текущими показаниями, графиком и диагностикой канала.
      </p>

      {points.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="Координаты пока не опубликованы"
            description="Как только у датчиков появятся координаты, они отобразятся на карте."
          />
        </div>
      ) : (
        <div className="mt-8 grid gap-4 lg:grid-cols-[2fr_1fr]">
          <div className="panel h-[420px] overflow-hidden p-0 sm:h-[540px]">
            {mounted ? (
              <Suspense fallback={<MapPlaceholder />}>
                <SensorMap points={points} selected={selected} onSelect={setSelected} />
              </Suspense>
            ) : (
              <MapPlaceholder />
            )}
          </div>

          <ul className="grid content-start gap-3">
            {points.map((point) => (
              <li key={point.slug}>
                <button
                  type="button"
                  onClick={() => setSelected(point.slug)}
                  className="panel flex w-full flex-col gap-2 px-4 py-4 text-left transition-colors hover:bg-surface-raised"
                >
                  <span className="flex items-start justify-between gap-3">
                    <span className="font-medium">{point.displayName}</span>
                    <StatusBadge state={point.state} />
                  </span>
                  <span className="num text-xs text-muted-foreground">
                    {formatCoordinate(point.latitude)}, {formatCoordinate(point.longitude)}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      <SensorDrawer slug={selected} state={selectedState} onClose={() => setSelected(null)} />
    </>
  );
}

function MapPlaceholder() {
  return (
    <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
      Карта загружается…
    </div>
  );
}
