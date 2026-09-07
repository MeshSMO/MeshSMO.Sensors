import { notFound } from "@tanstack/react-router";
import { forecastHorizons, ranges, type ForecastHorizon, type RangeKey } from "@/lib/api";
import { getMetric, metricLabel } from "@/lib/metrics";
import { getRegistrySensor, type RegistrySensor } from "@/lib/registry";
import { absoluteSiteUrl, createPageMeta } from "@/lib/seo";

export type SensorSearch = {
  metric?: string;
  metrics?: string;
  range?: RangeKey;
  from?: string;
  to?: string;
  mode?: "separate" | "combined";
  forecast?: ForecastHorizon;
};
export type ChartMode = NonNullable<SensorSearch["mode"]>;

export function parseSensorSearch(search: Record<string, unknown>): SensorSearch {
  const metric = optionalText(search["metric"]);
  const metrics = optionalText(search["metrics"]);
  const range = includes(ranges, search["range"]);
  const from = optionalIsoDate(search["from"]);
  const to = optionalIsoDate(search["to"]);
  const mode = includes(["separate", "combined"] as const, search["mode"]);
  const forecast = includes(forecastHorizons, search["forecast"]);

  return {
    ...(metric ? { metric } : {}),
    ...(metrics ? { metrics } : {}),
    ...(range ? { range } : {}),
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
    ...(mode ? { mode } : {}),
    ...(forecast ? { forecast } : {}),
  };
}

function optionalText(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function optionalIsoDate(value: unknown): string | undefined {
  const text = optionalText(value);
  return text && /^\d{4}-\d{2}-\d{2}T/.test(text) && !Number.isNaN(Date.parse(text))
    ? text
    : undefined;
}

function includes<Value extends string>(
  values: readonly Value[],
  value: unknown,
): Value | undefined {
  return typeof value === "string" && values.includes(value as Value)
    ? (value as Value)
    : undefined;
}

export function loadRegistrySensor(slug: string): RegistrySensor {
  const sensor = getRegistrySensor(slug);
  if (!sensor) throw notFound();
  return sensor;
}

export function createSensorHead(sensor: RegistrySensor) {
  const url = absoluteSiteUrl(`/sensors/${sensor.slug}`);
  const metricNames = sensor.metrics.map(metricLabel);
  const metricsPhrase = metricNames.slice(0, 3).join(", ").toLowerCase();
  const title = metricsPhrase
    ? `Датчик «${sensor.displayName}» — ${metricsPhrase} | MeshSMO`
    : `Датчик «${sensor.displayName}» — телеметрия | MeshSMO`;
  const description =
    sensor.description ??
    `Показания датчика ${sensor.displayName} в сети MeshSMO: текущие значения, история и диагностика радиоканала.`;

  return {
    meta: [
      ...createPageMeta({
        title,
        description,
        ...(sensor.indexable ? {} : { robots: "noindex,follow" }),
      }),
      { property: "og:url", content: url },
    ],
    links: [{ rel: "canonical", href: url }],
    scripts: [createBreadcrumbSchema(sensor, url), createDatasetSchema(sensor, url)],
  };
}

function createBreadcrumbSchema(sensor: RegistrySensor, url: string) {
  return {
    type: "application/ld+json",
    children: JSON.stringify({
      "@context": "https://schema.org",
      "@type": "BreadcrumbList",
      itemListElement: [
        { "@type": "ListItem", position: 1, name: "Главная", item: absoluteSiteUrl() },
        {
          "@type": "ListItem",
          position: 2,
          name: "Датчики",
          item: absoluteSiteUrl("/sensors"),
        },
        { "@type": "ListItem", position: 3, name: sensor.displayName, item: url },
      ],
    }),
  };
}

function createDatasetSchema(sensor: RegistrySensor, url: string) {
  return {
    type: "application/ld+json",
    children: JSON.stringify({
      "@context": "https://schema.org",
      "@type": "Dataset",
      name: `Телеметрия датчика «${sensor.displayName}»`,
      description: sensor.description,
      url,
      inLanguage: "ru",
      isAccessibleForFree: true,
      creator: { "@type": "Organization", name: "MeshSMO" },
      measurementTechnique: sensor.protocol,
      variableMeasured: sensor.metrics.map((metric) => {
        const meta = getMetric(metric);
        return {
          "@type": "PropertyValue",
          name: meta.label,
          alternateName: metric,
          ...(meta.unit ? { unitText: meta.unit } : {}),
        };
      }),
      ...(sensor.location
        ? {
            spatialCoverage: {
              "@type": "Place",
              geo: {
                "@type": "GeoCoordinates",
                latitude: sensor.location.latitude,
                longitude: sensor.location.longitude,
              },
            },
          }
        : {}),
    }),
  };
}
