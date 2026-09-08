import { getMetric } from "./metrics";
import {
  formatLocalizedDateTime,
  formatLocalizedNumber,
  formatLocalizedTime,
} from "@/i18n/formatters";
import { translate } from "@/i18n";

export type SensorState = "Online" | "Degraded" | "Offline" | "Unknown";

export function normalizeState(value: string | null | undefined): SensorState {
  if (value === "Online" || value === "Degraded" || value === "Offline") return value;
  return "Unknown";
}

export function formatValue(metricKey: string, value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return translate("common.noData");
  }
  return formatLocalizedNumber(value, getMetric(metricKey).precision);
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
    if (raw === null) return translate("common.noData");
    return raw === "true" || raw === "1" ? translate("common.yes") : translate("common.no");
  }
  if (meta.kind === "timestamp") {
    if (textValue) return formatDateTime(textValue);
    if (numericValue == null) return translate("common.noData");
    return formatDateTime(new Date(numericValue * 1000).toISOString());
  }
  if (meta.kind === "text") return textValue ?? translate("common.noData");
  return formatValue(metricKey, numericValue);
}

export function formatNumber(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return translate("common.noData");
  }
  return formatLocalizedNumber(value, digits);
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return translate("common.noData");
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return translate("common.noData");
  return formatLocalizedDateTime(date);
}

export function formatTime(iso: string | null | undefined): string {
  if (!iso) return translate("common.noData");
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return translate("common.noData");
  return formatLocalizedTime(date);
}

export function formatCoordinate(value: number): string {
  return formatLocalizedNumber(value, 4);
}
