import { getMetric } from "./metrics";

export type SensorState = "Online" | "Degraded" | "Offline" | "Unknown";

export const stateLabels: Record<SensorState, string> = {
  Online: "В сети",
  Degraded: "Нестабилен",
  Offline: "Не отвечает",
  Unknown: "Статус неизвестен",
};

export function normalizeState(value: string | null | undefined): SensorState {
  if (value === "Online" || value === "Degraded" || value === "Offline") return value;
  return "Unknown";
}

export function formatValue(metricKey: string, value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return value.toFixed(getMetric(metricKey).precision);
}

/** Универсальное отображение показания: числа, флаги, текст, метки времени. */
export function formatReading(
  metricKey: string,
  numericValue: number | null | undefined,
  textValue?: string | null,
): string {
  const meta = getMetric(metricKey);
  if (meta.kind === "boolean") {
    const raw = textValue ?? (numericValue == null ? null : String(numericValue));
    if (raw === null) return "—";
    return raw === "true" || raw === "1" ? "да" : "нет";
  }
  if (meta.kind === "timestamp") {
    if (textValue) return formatDateTime(textValue);
    if (numericValue == null) return "—";
    return formatDateTime(new Date(numericValue * 1000).toISOString());
  }
  if (meta.kind === "text") return textValue ?? "—";
  return formatValue(metricKey, numericValue);
}

export function formatNumber(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return value.toFixed(digits);
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

export function formatTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("ru-RU", {
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

export function formatCoordinate(value: number): string {
  return value.toFixed(4);
}
