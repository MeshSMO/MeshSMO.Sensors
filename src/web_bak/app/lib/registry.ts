import registryJson from "../generated/sensorRegistry.json";

export interface RegistrySensor {
  slug: string;
  displayName: string;
  description: string | null;
  location: { latitude: number; longitude: number; precision: string } | null;
  metrics: string[];
  enabled: boolean;
  visible: boolean;
  indexable: boolean;
}

// Generated at build time from config/sensors/*.yaml by
// scripts/generate-registry.mjs (npm prebuild). Bundled with the client so
// prerendered pages contain real content without a server loader.
const registry = registryJson as RegistrySensor[];

export function loadSensorRegistry(): RegistrySensor[] {
  return registry;
}

export function listVisibleSensors(): RegistrySensor[] {
  return registry.filter((sensor) => sensor.visible);
}

export function listIndexableSensors(): RegistrySensor[] {
  return listVisibleSensors().filter((sensor) => sensor.indexable);
}

export function findRegistrySensor(slug: string): RegistrySensor | undefined {
  return registry.find((sensor) => sensor.slug === slug);
}
