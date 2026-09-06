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
if (existsSync(directory)) {
  const slugs = new Set();
  for (const fileName of readdirSync(directory).filter((name) => name.endsWith(".yaml"))) {
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
      metrics: Array.isArray(raw.metrics) ? raw.metrics.filter((m) => typeof m === "string") : [],
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
