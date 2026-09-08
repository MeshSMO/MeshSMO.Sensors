/**
 * Центральный реестр метрик телеметрии.
 * Покрывает все типы CayenneLPP 1.6.1, которые может передать MeshCore.
 * Неизвестные ключи не ломают UI — для них возвращается безопасный дефолт.
 */
import { translate } from "@/i18n";
import type { translation } from "@/i18n/locales/ru-RU/translation";

export type MetricKind = "numeric" | "boolean" | "text" | "timestamp";

export type MetricChartZone = {
  from?: number;
  to?: number;
  color: string;
  label: string;
};

export type MetricChartOptions = {
  minimum?: number;
  maximum?: number;
  zones?: readonly MetricChartZone[];
};

export type MetricDefinition = {
  key: string;
  label: string;
  unit: string;
  color: string;
  precision: number;
  kind: MetricKind;
  /** Номер типа CayenneLPP (LPP_*), если он определён. */
  lpp?: number;
  /** Необязательные границы и цветовые зоны графика. */
  chart?: MetricChartOptions;
};

type MetricKey = keyof typeof translation.metrics.labels;
type MetricUnitKey = keyof typeof translation.metrics.units;

function def(
  key: MetricKey,
  unitKey: MetricUnitKey | null,
  precision: number,
  color: string,
  lpp?: number,
  kind: MetricKind = "numeric",
  chart?: MetricChartOptions,
): MetricDefinition {
  return {
    key,
    label: translate(`metrics.labels.${key}` as const),
    unit: unitKey === null ? "" : translate(`metrics.units.${unitKey}` as const),
    precision,
    color,
    kind,
    ...(lpp === undefined ? {} : { lpp }),
    ...(chart === undefined ? {} : { chart }),
  };
}

const ACCENT = "var(--accent)";
const WARM = "var(--status-degraded)";
const GREEN = "var(--status-online)";
const RED = "var(--status-offline)";
const GREY = "var(--status-unknown)";

const BATTERY_CHART: MetricChartOptions = {
  minimum: 3,
  maximum: 4.3,
  zones: [
    { to: 3.7, color: RED, label: translate("metrics.zones.batteryLow") },
    { from: 3.7, to: 4.2, color: WARM, label: translate("metrics.zones.batteryMedium") },
    { from: 4.2, color: GREEN, label: translate("metrics.zones.batteryHigh") },
  ],
};
const SOLAR_PANEL_CHART: MetricChartOptions = { minimum: 0, maximum: 5 };
const PERCENT_CHART: MetricChartOptions = { minimum: 0, maximum: 100 };
const DIRECTION_CHART: MetricChartOptions = { minimum: 0, maximum: 360 };

const list: MetricDefinition[] = [
  // Штатная среда
  def("temperature", "celsius", 1, WARM, 103),
  def("humidity", "percent", 0, GREEN, 104, "numeric", PERCENT_CHART),
  def("pressure", "hectopascal", 0, ACCENT, 115),
  def("altitude", "meter", 0, GREY, 121),
  def("luminosity", "lux", 0, WARM, 101),
  def("concentration", "ppm", 0, RED, 125),
  def("gas_resistance", "ohm", 0, GREY, 100),
  def("iaq", null, 0, GREEN, 100),

  // Электрика
  def("battery", "volt", 2, GREY, 116, "numeric", BATTERY_CHART),
  def("battery_voltage", "volt", 2, GREY, 116, "numeric", BATTERY_CHART),
  def("voltage", "volt", 2, GREY, 116),
  def("current", "ampere", 3, WARM, 117),
  def("power", "watt", 1, RED, 128),
  def("energy", "kilowattHour", 3, RED, 131),
  def("frequency", "hertz", 0, ACCENT, 118),
  def("percentage", "percent", 0, GREEN, 120, "numeric", PERCENT_CHART),
  def("analog_input", null, 2, GREY, 2),
  def("analog_output", null, 2, GREY, 3),
  def("generic", null, 2, GREY, 100),

  // Геометрия и движение
  def("distance", "meter", 3, ACCENT, 130),
  def("direction", "degree", 0, ACCENT, 132, "numeric", DIRECTION_CHART),
  def("accelerometer", "gravity", 3, WARM, 113),
  def("gyrometer", "degreesPerSecond", 2, WARM, 134),

  // Дискретные и особые
  def("presence", null, 0, GREEN, 102, "boolean"),
  def("digital_input", null, 0, GREY, 0, "boolean"),
  def("digital_output", null, 0, GREY, 1, "boolean"),
  def("switch", null, 0, GREY, 142, "boolean"),
  def("colour", null, 0, ACCENT, 135, "text"),
  def("gps", null, 4, ACCENT, 136, "text"),
  def("unixtime", null, 0, GREY, 133, "timestamp"),
  def("polyline", null, 0, GREY, 240, "text"),

  // Прикладные (радиация и почва — поверх generic/frequency)
  def("radiation_cpm", "countsPerMinute", 0, RED, 118),
  def("radiation_dose", "microsievertPerHour", 3, RED, 2),
  def("radiation_total", "microsievert", 3, RED, 100),
  def("solar_panel_voltage", "volt", 2, WARM, 116, "numeric", SOLAR_PANEL_CHART),
  def("soil_moisture", "percent", 0, GREEN, 120, "numeric", PERCENT_CHART),
  def("soil_temperature", "celsius", 1, WARM, 103),
  def("object_temperature", "celsius", 1, WARM, 103),
  def("ambient_temperature", "celsius", 1, WARM, 103),
];

const definitions: Record<string, MetricDefinition> = Object.fromEntries(
  list.map((d) => [d.key, d]),
);

/** Частые синонимы ключей из прошивок и BFF. */
const aliases: Record<string, string> = {
  temp: "temperature",
  temperature_c: "temperature",
  mcu_temperature: "temperature",
  rel_humidity: "humidity",
  relative_humidity: "humidity",
  barometric_pressure: "pressure",
  baro: "pressure",
  batt: "battery",
  bus_voltage: "voltage",
  lux: "luminosity",
  light: "luminosity",
  illuminance: "luminosity",
  co2: "concentration",
  gas: "gas_resistance",
  air_quality: "iaq",
  accel: "accelerometer",
  gyro: "gyrometer",
  location: "gps",
  cpm: "radiation_cpm",
  dose_rate: "radiation_dose",
  usvh: "radiation_dose",
};

export function getMetric(key: string): MetricDefinition {
  const normalized = key.toLowerCase();
  const resolved = aliases[normalized] ?? normalized;
  return (
    definitions[resolved] ?? {
      key,
      label: translate("metrics.unknown", { key }),
      unit: "",
      color: ACCENT,
      precision: 2,
      kind: "numeric",
    }
  );
}

export function metricLabel(key: string): string {
  return getMetric(key).label;
}

export const knownMetrics = list;
