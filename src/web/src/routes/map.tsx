import { createFileRoute } from "@tanstack/react-router";
import { lazy, Suspense, useEffect, useState } from "react";
import { EmptyState, PageShell } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { SensorDrawer } from "@/components/site/SensorDrawer";
import type { MapPoint } from "@/components/site/SensorMap";
import { useSensors } from "@/lib/api";
import { sensorRegistry } from "@/lib/registry";
import { formatCoordinate, normalizeState, type SensorState } from "@/lib/format";

const SensorMap = lazy(() => import("@/components/site/SensorMap"));

const title = "Карта датчиков MeshSMO — LoRa-телеметрия на карте | MeshSMO";
const description =
  "Интерактивная карта публичных LoRa-датчиков MeshSMO: расположение узлов, текущие статусы, показания и графики по клику.";

export const Route = createFileRoute("/map")({
  head: () => ({
    meta: [
      { title },
      { name: "description", content: description },
      { property: "og:title", content: title },
      { property: "og:description", content: description },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
    links: [{ rel: "canonical", href: "https://sensors.meshsmo.ru/map" }],
  }),
  component: MapPage,
});

function MapPage() {
  const { data } = useSensors();
  const [mounted, setMounted] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  useEffect(() => setMounted(true), []);

  const live = new Map(
    (data?.sensors ?? []).map((s) => [s.slug, normalizeState(s.state)] as const),
  );

  const located = sensorRegistry.filter((s) => s.location !== null);
  const points: MapPoint[] = located.map((s) => ({
    slug: s.slug,
    displayName: s.displayName,
    latitude: s.location!.latitude,
    longitude: s.location!.longitude,
    state: live.get(s.slug) ?? null,
  }));

  const selectedState: SensorState | null = selected ? (live.get(selected) ?? null) : null;

  return (
    <PageShell>
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

          <ul className="grid gap-3 content-start">
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
    </PageShell>
  );
}

function MapPlaceholder() {
  return (
    <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
      Карта загружается…
    </div>
  );
}
