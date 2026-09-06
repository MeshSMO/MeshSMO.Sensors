import { createFileRoute, Link } from "@tanstack/react-router";
import { PageShell, SkeletonLine } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useDashboard } from "@/lib/api";
import { sensorRegistry } from "@/lib/registry";
import { formatValue, normalizeState } from "@/lib/format";
import { getMetric } from "@/lib/metrics";

const title = "MeshSMO Sensors — телеметрия LoRa-датчиков Смоленской области";
const description =
  "Публичные показания датчиков MeshSMO: температура, влажность, давление и заряд батареи. Данные передаются по радиосети MeshCore (LoRa).";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: [
      { title },
      { name: "description", content: description },
      { property: "og:title", content: title },
      { property: "og:description", content: description },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
    scripts: [
      {
        type: "application/ld+json",
        children: JSON.stringify({
          "@context": "https://schema.org",
          "@graph": [
            {
              "@type": "WebSite",
              name: "MeshSMO Sensors",
              url: "https://sensors.meshsmo.ru/",
              inLanguage: "ru-RU",
            },
            {
              "@type": "Organization",
              name: "MeshSMO",
              url: "https://sensors.meshsmo.ru/",
            },
          ],
        }),
      },
    ],
  }),
  component: Index,
});

function Index() {
  return (
    <PageShell>
      <section className="relative overflow-hidden rounded-2xl border border-border bg-surface px-6 py-14 sm:px-10">
        <div className="mesh-grid pointer-events-none absolute inset-0 opacity-40" aria-hidden />
        <div className="relative max-w-2xl">
          <p className="eyebrow">LoRa · MeshCore · Смоленская область</p>
          <h1 className="mt-4 text-4xl font-semibold tracking-tight sm:text-5xl">
            Телеметрия датчиков в открытом доступе
          </h1>
          <p className="mt-4 text-base text-muted-foreground">
            MeshSMO собирает показания физических датчиков через радиосеть MeshCore и публикует их
            без задержек и посредников. Никаких выдуманных цифр — только то, что реально пришло из
            эфира.
          </p>
          <Link
            to="/sensors"
            className="mt-8 inline-flex items-center rounded-lg bg-accent px-5 py-2.5 text-sm font-semibold text-accent-foreground transition-opacity hover:opacity-90"
          >
            Смотреть датчики
          </Link>
        </div>
      </section>

      <LiveDashboard />
    </PageShell>
  );
}

function LiveDashboard() {
  const { data, isPending, isError } = useDashboard();
  const summary = data?.summary;
  const sensors = data?.sensors ?? [];

  const counters: Array<[string, number | undefined]> = [
    ["Всего", summary?.total],
    ["В сети", summary?.online],
    ["Нестабильны", summary?.degraded],
    ["Не отвечают", summary?.offline],
  ];

  return (
    <section className="mt-12">
      <div className="flex items-baseline justify-between gap-4">
        <h2 className="text-xl font-semibold tracking-tight">Состояние сети</h2>
        <p className="text-xs text-muted-foreground">Обновляется каждые 20 секунд</p>
      </div>

      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {counters.map(([label, value]) => (
          <div key={label} className="panel px-4 py-4">
            <p className="text-xs text-muted-foreground">{label}</p>
            <p className="num mt-2 text-3xl">
              {isPending && !isError ? <SkeletonLine className="h-8 w-12" /> : (value ?? "—")}
            </p>
          </div>
        ))}
      </div>

      <ul className="mt-6 space-y-3">
        {(sensors.length > 0
          ? sensors.map((s) => ({
              slug: s.slug,
              displayName: s.displayName,
              metrics: s.metrics,
              state: normalizeState(s.state),
            }))
          : sensorRegistry.map((s) => ({
              slug: s.slug,
              displayName: s.displayName,
              metrics: s.metrics,
              state: null,
            }))
        ).map((sensor) => (
          <li key={sensor.slug}>
            <Link
              to="/sensors/$slug"
              params={{ slug: sensor.slug }}
              className="panel flex flex-wrap items-center justify-between gap-3 px-5 py-4 transition-colors hover:bg-surface-raised"
            >
              <span className="font-medium">{sensor.displayName}</span>
              <span className="flex items-center gap-4 text-sm text-muted-foreground">
                <span className="num">
                  {sensor.metrics[0] ? getMetric(sensor.metrics[0]).label : "—"}:{" "}
                  {formatValue(sensor.metrics[0] ?? "", null)}
                </span>
                <StatusBadge state={sensor.state} />
              </span>
            </Link>
          </li>
        ))}
      </ul>

      {isError && (
        <p className="mt-4 text-xs text-muted-foreground">
          Сервер телеметрии сейчас недоступен — показан статический список датчиков.
        </p>
      )}
    </section>
  );
}
