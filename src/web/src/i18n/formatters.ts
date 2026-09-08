import { defaultLocale } from "./config";

const dateTimeFormatter = new Intl.DateTimeFormat(defaultLocale, {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

const timeFormatter = new Intl.DateTimeFormat(defaultLocale, {
  hour: "2-digit",
  minute: "2-digit",
});

const chartTimeFormatter = new Intl.DateTimeFormat(defaultLocale, {
  day: "2-digit",
  month: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

const numberFormatters = new Map<number, Intl.NumberFormat>();
const decimalFormatter = new Intl.NumberFormat(defaultLocale);

export function formatLocalizedDecimal(value: number): string {
  return decimalFormatter.format(value);
}

export function formatLocalizedNumber(value: number, digits: number): string {
  let formatter = numberFormatters.get(digits);
  if (!formatter) {
    formatter = new Intl.NumberFormat(defaultLocale, {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
      useGrouping: false,
    });
    numberFormatters.set(digits, formatter);
  }
  return formatter.format(value);
}

export function formatLocalizedDateTime(date: Date): string {
  return dateTimeFormatter.format(date);
}

export function formatLocalizedTime(date: Date): string {
  return timeFormatter.format(date);
}

export function formatChartTime(value: string | number | Date): string {
  return chartTimeFormatter.format(new Date(value));
}
