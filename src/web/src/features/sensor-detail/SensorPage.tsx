import { getRouteApi, Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { SensorDiagnostics, SensorHeader, SensorReadings } from "./SensorOverview";
import { SensorHistory } from "./history/SensorHistory";
import { formatCoordinate } from "@/lib/format";
import { getMetric, metricLabel } from "@/lib/metrics";

const sensorRoute = getRouteApi("/sensors/$slug");

export function SensorPage() {
  const { t } = useTranslation();
  const { slug } = sensorRoute.useParams();
  const search = sensorRoute.useSearch();
  const sensor = sensorRoute.useLoaderData();
  const chartableMetrics = sensor.metrics.filter((metric) => getMetric(metric).kind === "numeric");
  const requestedMetrics = (search.metrics ?? search.metric ?? "")
    .split(",")
    .map((metric) => metric.trim())
    .filter((metric) => chartableMetrics.includes(metric));
  const selectedMetrics =
    requestedMetrics.length > 0 ? [...new Set(requestedMetrics)] : chartableMetrics.slice(0, 1);

  return (
    <>
      <nav
        aria-label={t("common.accessibility.breadcrumbs")}
        className="text-xs text-muted-foreground"
      >
        <Link to="/" className="hover:text-foreground">
          {t("common.navigation.home")}
        </Link>
        <span className="px-2" aria-hidden>
          /
        </span>
        <Link to="/sensors" className="hover:text-foreground">
          {t("common.navigation.sensors")}
        </Link>
        <span className="px-2" aria-hidden>
          /
        </span>
        <span aria-current="page" className="text-foreground">
          {sensor.displayName}
        </span>
      </nav>

      <SensorHeader slug={slug} fallbackName={sensor.displayName} />
      <p className="mt-4 max-w-2xl text-muted-foreground">{sensor.description}</p>
      <p className="num mt-3 text-sm text-muted-foreground">
        {sensor.location
          ? t("common.coordinates.pair", {
              latitude: formatCoordinate(sensor.location.latitude),
              longitude: formatCoordinate(sensor.location.longitude),
            })
          : t("common.coordinates.unavailable")}
        {sensor.location?.precision === "approximate" ? (
          <span className="ml-2 font-sans">{t("common.coordinates.approximate")}</span>
        ) : null}
      </p>

      <ul className="mt-3 flex flex-wrap gap-2">
        {sensor.metrics.map((metric) => (
          <li
            key={metric}
            className="rounded-md border border-border bg-surface-raised px-2 py-1 text-xs text-muted-foreground"
          >
            {metricLabel(metric)}
          </li>
        ))}
      </ul>

      <SensorReadings
        slug={slug}
        metrics={sensor.metrics}
        pollIntervalSeconds={sensor.pollIntervalSeconds ?? 60}
      />
      <SensorHistory
        slug={slug}
        metrics={chartableMetrics}
        selected={selectedMetrics}
        search={search}
        pollIntervalSeconds={sensor.pollIntervalSeconds ?? 300}
      />
      <SensorDiagnostics
        slug={slug}
        protocol={sensor.protocol ?? t("common.noData")}
        pollIntervalSeconds={sensor.pollIntervalSeconds ?? 300}
      />
    </>
  );
}

export function SensorNotFoundPage() {
  const { t } = useTranslation();

  return (
    <div className="py-12">
      <p className="eyebrow">{t("sensor.notFound.eyebrow")}</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">{t("sensor.notFound.title")}</h1>
      <p className="mt-3 text-muted-foreground">{t("sensor.notFound.description")}</p>
      <Link
        to="/sensors"
        className="mt-6 inline-flex rounded-lg bg-accent px-5 py-2.5 text-sm font-semibold text-accent-foreground"
      >
        {t("common.actions.allSensors")}
      </Link>
    </div>
  );
}
