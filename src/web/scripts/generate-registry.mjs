// Build-time script: flattens the GitOps sensor registry (config/sensors/*.yaml)
// into src/generated/sensorRegistry.json so that prerendered routes can render
// sensor content without a server loader. Bundled into the client build and
// committed, so typecheck and prerender work before the first build too.
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parse } from "yaml";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "../../..");
const directory = path.join(root, "config", "sensors");
const outFile = path.resolve(scriptDir, "../src/generated/sensorRegistry.json");

const durationSeconds = {
  ms: (value) => value / 1000,
  s: (value) => value,
  m: (value) => value * 60,
  h: (value) => value * 3600,
};

function parseIntervalSeconds(raw) {
  if (typeof raw !== "string") return null;
  const match = /^(\d+)(ms|s|m|h)$/.exec(raw.trim());
  if (!match) return null;
  const convert = durationSeconds[match[2]];
  return Math.round(convert(Number(match[1])));
}

const sensors = [];
const yamlFiles = existsSync(directory)
  ? readdirSync(directory).filter((name) => name.endsWith(".yaml"))
  : [];
if (yamlFiles.length === 0) {
  // Sensor YAMLs are deployment-local (gitignored). On a checkout without
  // them (CI), keep the committed snapshot so prerender routes survive.
  console.log("No local sensor YAML files; keeping the committed sensorRegistry.json");
  process.exit(0);
}
{
  const slugs = new Set();
  for (const fileName of yamlFiles) {
    const raw = parse(readFileSync(path.join(directory, fileName), "utf8"));
    if (typeof raw?.slug !== "string") {
      continue;
    }
    if (slugs.has(raw.slug)) {
      throw new Error(`Duplicate sensor slug in registry: ${raw.slug}`);
    }
    slugs.add(raw.slug);

    const publicSection = raw.public ?? {};
    const polling = raw.polling ?? {};
    const location = raw.location ?? {};
    const declaredMetrics = Array.isArray(raw.metrics)
      ? raw.metrics.filter((metric) => typeof metric === "string")
      : [];
    const channelMetrics = Array.isArray(raw.telemetry?.channels)
      ? raw.telemetry.channels
          .map((channel) => channel?.metric)
          .filter((metric) => typeof metric === "string")
      : [];
    sensors.push({
      slug: raw.slug,
      displayName: typeof raw.displayName === "string" ? raw.displayName : raw.slug,
      description: typeof raw.description === "string" ? raw.description : null,
      location:
        typeof location.latitude === "number" && typeof location.longitude === "number"
          ? {
              latitude: location.latitude,
              longitude: location.longitude,
              precision:
                typeof location.precision === "string" ? location.precision : "approximate",
            }
          : null,
      // Keep the prerendered registry in sync with SensorRegistrySynchronizer:
      // mapped channel targets are public metrics too, even when they are not
      // repeated in the top-level metrics list.
      metrics: [...new Set([...declaredMetrics, ...channelMetrics])].sort(),
      protocol: typeof raw.mesh?.protocol === "string" ? raw.mesh.protocol : null,
      pollIntervalSeconds: parseIntervalSeconds(polling.interval),
      enabled: Boolean(polling.enabled),
      visible: Boolean(publicSection.visible),
      indexable: Boolean(publicSection.indexable),
    });
  }
}

sensors.sort((a, b) => a.slug.localeCompare(b.slug));
mkdirSync(path.dirname(outFile), { recursive: true });
writeFileSync(outFile, `${JSON.stringify(sensors, null, 2)}\n`);
console.log(`Wrote ${sensors.length} sensor(s) to ${path.relative(root, outFile)}`);
