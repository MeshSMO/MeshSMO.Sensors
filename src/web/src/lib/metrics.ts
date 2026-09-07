/**
 * Центральный реестр метрик телеметрии.
 * Покрывает все типы CayenneLPP 1.6.1, которые может передать MeshCore.
 * Неизвестные ключи не ломают UI — для них возвращается безопасный дефолт.
 */
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

function def(
  key: string,
  label: string,
  unit: string,
  precision: number,
  color: string,
  lpp?: number,
  kind: MetricKind = "numeric",
  chart?: MetricChartOptions,
): MetricDefinition {
  return {
    key,
    label,
    unit,
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
    { to: 3.7, color: RED, label: "Низкий заряд" },
    { from: 3.7, to: 4.2, color: WARM, label: "Средний заряд" },
    { from: 4.2, color: GREEN, label: "Высокий заряд" },
  ],
};
const SOLAR_PANEL_CHART: MetricChartOptions = { minimum: 0, maximum: 5 };
const PERCENT_CHART: MetricChartOptions = { minimum: 0, maximum: 100 };
const DIRECTION_CHART: MetricChartOptions = { minimum: 0, maximum: 360 };

const list: MetricDefinition[] = [
  // Штатная среда
  def("temperature", "Температура", "°C", 1, WARM, 103),
  def("humidity", "Влажность", "%", 0, GREEN, 104, "numeric", PERCENT_CHART),
  def("pressure", "Давление", "hPa", 0, ACCENT, 115),
  def("altitude", "Высота", "м", 0, GREY, 121),
  def("luminosity", "Освещённость", "лк", 0, WARM, 101),
  def("concentration", "Концентрация", "ppm", 0, RED, 125),
  def("gas_resistance", "Сопротивление газа", "Ом", 0, GREY, 100),
  def("iaq", "Качество воздуха (IAQ)", "", 0, GREEN, 100),

  // Электрика
  def("battery", "Батарея", "V", 2, GREY, 116, "numeric", BATTERY_CHART),
  def("battery_voltage", "Напряжение батареи", "В", 2, GREY, 116, "numeric", BATTERY_CHART),
  def("voltage", "Напряжение", "V", 2, GREY, 116),
  def("current", "Ток", "A", 3, WARM, 117),
  def("power", "Мощность", "Вт", 1, RED, 128),
  def("energy", "Энергия", "кВт·ч", 3, RED, 131),
  def("frequency", "Частота", "Гц", 0, ACCENT, 118),
  def("percentage", "Процент", "%", 0, GREEN, 120, "numeric", PERCENT_CHART),
  def("analog_input", "Аналоговый вход", "", 2, GREY, 2),
  def("analog_output", "Аналоговый выход", "", 2, GREY, 3),
  def("generic", "Универсальный датчик", "", 2, GREY, 100),

  // Геометрия и движение
  def("distance", "Расстояние", "м", 3, ACCENT, 130),
  def("direction", "Направление", "°", 0, ACCENT, 132, "numeric", DIRECTION_CHART),
  def("accelerometer", "Ускорение", "G", 3, WARM, 113),
  def("gyrometer", "Гироскоп", "°/с", 2, WARM, 134),

  // Дискретные и особые
  def("presence", "Присутствие", "", 0, GREEN, 102, "boolean"),
  def("digital_input", "Дискретный вход", "", 0, GREY, 0, "boolean"),
  def("digital_output", "Дискретный выход", "", 0, GREY, 1, "boolean"),
  def("switch", "Переключатель", "", 0, GREY, 142, "boolean"),
  def("colour", "Цвет", "", 0, ACCENT, 135, "text"),
  def("gps", "Координаты", "", 4, ACCENT, 136, "text"),
  def("unixtime", "Метка времени", "", 0, GREY, 133, "timestamp"),
  def("polyline", "Трек", "", 0, GREY, 240, "text"),

  // Прикладные (радиация и почва — поверх generic/frequency)
  def("radiation_cpm", "Скорость счёта", "CPM", 0, RED, 118),
  def("radiation_dose", "Мощность дозы", "мкЗв/ч", 3, RED, 2),
  def("radiation_total", "Накопленная доза", "мкЗв", 3, RED, 100),
  def(
    "solar_panel_voltage",
    "Напряжение солнечной панели",
    "В",
    2,
    WARM,
    116,
    "numeric",
    SOLAR_PANEL_CHART,
  ),
  def("soil_moisture", "Влажность почвы", "%", 0, GREEN, 120, "numeric", PERCENT_CHART),
  def("soil_temperature", "Температура почвы", "°C", 1, WARM, 103),
  def("object_temperature", "Температура объекта", "°C", 1, WARM, 103),
  def("ambient_temperature", "Температура среды", "°C", 1, WARM, 103),
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

function humanize(key: string): string {
  const text = key.replace(/[_-]+/g, " ").trim();
  return text.charAt(0).toUpperCase() + text.slice(1);
}

export function getMetric(key: string): MetricDefinition {
  const normalized = key.toLowerCase();
  const resolved = aliases[normalized] ?? normalized;
  return (
    definitions[resolved] ?? {
      key,
      label: humanize(key),
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
