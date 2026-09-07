import { Link } from "@tanstack/react-router";
import { SkeletonLine } from "@/components/site/Shell";
import { StatusBadge } from "@/components/site/StatusBadge";
import { useDashboard } from "@/lib/api";
import { chargeLevel, hasBatteryMetric, liIonChargePercent, type ChargeLevel } from "@/lib/battery";
import { normalizeState } from "@/lib/format";
import { sensorRegistry } from "@/lib/registry";

export function HomePage() {
  return (
    <>
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
    </>
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
  const visibleSensors = sensors.length
    ? sensors.map((sensor) => ({ ...sensor, state: normalizeState(sensor.state) }))
    : sensorRegistry.map((sensor) => ({ ...sensor, batteryVoltage: null, state: null }));

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
        {visibleSensors.map((sensor) => (
          <li key={sensor.slug}>
            <Link
              to="/sensors/$slug"
              params={{ slug: sensor.slug }}
              className="panel flex flex-wrap items-center justify-between gap-3 px-5 py-4 transition-colors hover:bg-surface-raised"
            >
              <span className="font-medium">{sensor.displayName}</span>
              <span className="flex items-center gap-4 text-sm text-muted-foreground">
                {hasBatteryMetric(sensor.metrics) ? (
                  <BatteryReadout volts={sensor.batteryVoltage} />
                ) : null}
                <StatusBadge state={sensor.state} />
              </span>
            </Link>
          </li>
        ))}
      </ul>

      {isError ? (
        <p className="mt-4 text-xs text-muted-foreground">
          Сервер телеметрии сейчас недоступен — показан статический список датчиков.
        </p>
      ) : null}
    </section>
  );
}

const chargeLevelStyles: Record<ChargeLevel, string> = {
  high: "text-online",
  medium: "text-degraded",
  low: "text-offline",
};

function BatteryReadout({ volts }: { volts: number | null }) {
  const percent = liIonChargePercent(volts);
  if (percent === null) {
    return (
      <span className="num" title="Нет корректных данных о заряде батареи">
        Батарея: —
      </span>
    );
  }

  return (
    <span
      className={`num ${chargeLevelStyles[chargeLevel(percent)]}`}
      title={`Li-ion, напряжение ${volts?.toFixed(2)} В`}
    >
      Батарея: {percent}%
    </span>
  );
}
