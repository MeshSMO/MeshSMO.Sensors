// Build-time script: flattens the GitOps sensor registry (config/sensors/*.yaml)
// into app/generated/sensorRegistry.json so that prerendered routes can render
// sensor content without a server loader (React Router ssr:false forbids
// loaders during prerender).
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parse } from "yaml";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const directory = path.join(root, "config", "sensors");
const outFile = path.join(root, "src", "web", "app", "generated", "sensorRegistry.json");

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
              precision: typeof location.precision === "string" ? location.precision : "approximate",
            }
          : null,
      metrics: Array.isArray(raw.metrics) ? raw.metrics.filter((m) => typeof m === "string") : [],
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
