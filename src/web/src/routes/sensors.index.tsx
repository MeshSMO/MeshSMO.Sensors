import { createFileRoute, Link } from "@tanstack/react-router";
import { EmptyState, PageShell } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useSensors } from "@/lib/api";
import { sensorRegistry } from "@/lib/registry";
import { formatCoordinate, normalizeState } from "@/lib/format";
import { metricLabel } from "@/lib/metrics";

const title = "Публичные датчики MeshSMO — список и статусы | MeshSMO";
const description =
  "Список публичных LoRa-датчиков MeshSMO с текущими статусами: температура, влажность, давление, батарея.";

export const Route = createFileRoute("/sensors/")({
  head: () => ({
    meta: [
      { title },
      { name: "description", content: description },
      { property: "og:title", content: title },
      { property: "og:description", content: description },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
  }),
  component: SensorsPage,
});

function SensorsPage() {
  const { data } = useSensors();
  const live = new Map((data?.sensors ?? []).map((s) => [s.slug, normalizeState(s.state)]));

  return (
    <PageShell>
      <p className="eyebrow">Реестр</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">Публичные датчики</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">
        Каждый датчик передаёт показания по радиосети MeshCore. Статус и последние значения
        подгружаются с сервера телеметрии; описание и перечень показателей доступны всегда.
      </p>

      {sensorRegistry.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="Пока ни один датчик не опубликован"
            description="Сеть разворачивается. Как только первый датчик выйдет в эфир, он появится здесь."
          />
        </div>
      ) : (
        <ul className="mt-8 grid gap-4 sm:grid-cols-2">
          {sensorRegistry.map((sensor) => (
            <li key={sensor.slug}>
              <Link
                to="/sensors/$slug"
                params={{ slug: sensor.slug }}
                className="panel flex h-full flex-col gap-3 px-5 py-5 transition-colors hover:bg-surface-raised"
              >
                <span className="flex items-start justify-between gap-3">
                  <span className="text-lg font-medium">{sensor.displayName}</span>
                  <StatusBadge state={live.get(sensor.slug) ?? null} />
                </span>
                <span className="text-sm text-muted-foreground">{sensor.description}</span>
                <span className="num text-xs text-muted-foreground">
                  {sensor.location
                    ? `${formatCoordinate(sensor.location.latitude)}, ${formatCoordinate(sensor.location.longitude)}`
                    : "Координаты не опубликованы"}
                </span>
                <span className="mt-auto flex flex-wrap gap-2 pt-2">
                  {sensor.metrics.map((m) => (
                    <span
                      key={m}
                      className="rounded-md border border-border bg-surface-raised px-2 py-1 text-xs text-muted-foreground"
                    >
                      {metricLabel(m)}
                    </span>
                  ))}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </PageShell>
  );
}
