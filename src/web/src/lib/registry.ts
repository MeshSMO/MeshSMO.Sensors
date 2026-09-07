/**
 * Статический реестр датчиков — источник SEO-контента.
 * Генерируется на prebuild из config/sensors/*.yaml (scripts/generate-registry.mjs),
 * содержит только публичные датчики и не хранится в Git.
 * Живые данные приходят из API.
 */
import registryJson from "../generated/sensorRegistry.json";

export interface RegistrySensor {
  slug: string;
  displayName: string;
  description: string | null;
  location: { latitude: number; longitude: number; precision: string } | null;
  metrics: string[];
  protocol: string | null;
  pollIntervalSeconds: number | null;
  enabled: boolean;
  visible: boolean;
  indexable: boolean;
}

const registry = registryJson as RegistrySensor[];

/** Публичный срез реестра: только датчики, видимые на сайте. */
export const sensorRegistry: RegistrySensor[] = registry.filter((sensor) => sensor.visible);

export function listIndexableSensors(): RegistrySensor[] {
  return sensorRegistry.filter((sensor) => sensor.indexable);
}

export function getRegistrySensor(slug: string): RegistrySensor | undefined {
  return sensorRegistry.find((sensor) => sensor.slug === slug);
}
