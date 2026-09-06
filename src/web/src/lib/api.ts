import { useQueries, useQuery } from "@tanstack/react-query";
import type { SensorState } from "./format";

/** Базовый адрес BFF. Same origin по умолчанию. */
export const API_BASE = (import.meta.env["VITE_API_BASE_URL"] as string | undefined) ?? "/api/v1";

export type SensorSummary = {
  slug: string;
  displayName: string;
  description: string | null;
  latitude: number | null;
  longitude: number | null;
  metrics: string[];
  state: SensorState;
};

export type DashboardResponse = {
  summary: {
    total: number;
    online: number;
    degraded: number;
    offline: number;
    unknown: number;
  };
  sensors: SensorSummary[];
};

export type SensorDetail = {
  slug: string;
  displayName: string;
  description: string | null;
  location: {
    latitude: number;
    longitude: number;
    precision: string;
  } | null;
  metrics: string[];
  protocol: string;
  pollIntervalSeconds: number;
  state: SensorState;
};

export type SensorStatus = {
  state: SensorState;
  lastPollAt: string | null;
  lastSuccessAt: string | null;
  consecutiveFailures: number;
  lastRssi: number | null;
  lastSnr: number | null;
  updatedAt: string | null;
};

export type LatestValue = {
  metric: string;
  /** Человекочитаемое имя из BFF-реестра; фронт использует его в приоритете. */
  displayName: string | null;
  timestamp: string;
  numericValue: number | null;
  textValue: string | null;
  unit: string | null;
};

export type LatestResponse = { values: LatestValue[] };

export type MeasurementPoint = {
  timestamp: string;
  min: number | null;
  avg: number | null;
  max: number | null;
};

export type MeasurementsResponse = {
  sensor: { slug: string; displayName: string };
  metric: { key: string; unit: string | null };
  range: { from: string; to: string; resolution: string };
  points: MeasurementPoint[];
};

async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    throw new Error(`API ${response.status}`);
  }
  return (await response.json()) as T;
}

/** Живые запросы выполняются только в браузере — пререндер остаётся статичным. */
const isBrowser = typeof window !== "undefined";

export function useDashboard() {
  return useQuery({
    queryKey: ["dashboard"],
    queryFn: () => apiGet<DashboardResponse>("/dashboard"),
    enabled: isBrowser,
    refetchInterval: 20_000,
    retry: 1,
  });
}

export function useSensors() {
  return useQuery({
    queryKey: ["sensors"],
    queryFn: () => apiGet<{ sensors: SensorSummary[] }>("/sensors"),
    enabled: isBrowser,
    refetchInterval: 30_000,
    retry: 1,
  });
}

export function useSensor(slug: string) {
  return useQuery({
    queryKey: ["sensor", slug],
    queryFn: () => apiGet<SensorDetail>(`/sensors/${slug}`),
    enabled: isBrowser,
    retry: 1,
  });
}

/** Спека: интервал опроса не ниже pollIntervalSeconds датчика, но не чаще 20 с. */
function pollInterval(pollIntervalSeconds: number | undefined) {
  return Math.max(20_000, (pollIntervalSeconds ?? 0) * 1000);
}

export function useStatus(slug: string, pollIntervalSeconds?: number) {
  return useQuery({
    queryKey: ["status", slug],
    queryFn: () => apiGet<SensorStatus>(`/sensors/${slug}/status`),
    enabled: isBrowser,
    refetchInterval: pollInterval(pollIntervalSeconds),
    retry: 1,
  });
}

export function useLatest(slug: string, pollIntervalSeconds?: number) {
  return useQuery({
    queryKey: ["latest", slug],
    queryFn: () => apiGet<LatestResponse>(`/sensors/${slug}/latest`),
    enabled: isBrowser,
    refetchInterval: pollInterval(pollIntervalSeconds),
    retry: 1,
  });
}

export const ranges = ["6h", "12h", "24h", "7d", "30d", "6m", "1y", "custom"] as const;
export type RangeKey = (typeof ranges)[number];

export const rangeLabels: Record<RangeKey, string> = {
  "6h": "6 ч",
  "12h": "12 ч",
  "24h": "24 ч",
  "7d": "7 дн",
  "30d": "30 дн",
  "6m": "6 мес",
  "1y": "1 год",
  custom: "Период",
};

const rangeMs: Record<Exclude<RangeKey, "custom">, number> = {
  "6h": 6 * 3600_000,
  "12h": 12 * 3600_000,
  "24h": 24 * 3600_000,
  "7d": 7 * 24 * 3600_000,
  "30d": 30 * 24 * 3600_000,
  "6m": 182 * 24 * 3600_000,
  "1y": 365 * 24 * 3600_000,
};

export type CustomRange = { from?: string | undefined; to?: string | undefined };

/** Границы запроса для выбранного диапазона. Для «произвольного» — из формы. */
export function resolveRange(
  range: RangeKey,
  custom?: CustomRange,
): { from: string; to: string } | null {
  if (range === "custom") {
    if (!custom?.from || !custom?.to) return null;
    const from = new Date(custom.from);
    const to = new Date(custom.to);
    if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) return null;
    if (from >= to) return null;
    return { from: from.toISOString(), to: to.toISOString() };
  }
  const to = new Date();
  const from = new Date(to.getTime() - rangeMs[range]);
  return { from: from.toISOString(), to: to.toISOString() };
}

function measurementsOptions(slug: string, metric: string, range: RangeKey, custom?: CustomRange) {
  const bounds = resolveRange(range, custom);
  const bucket = range === "custom" ? `${bounds?.from ?? ""}|${bounds?.to ?? ""}` : range;
  return {
    queryKey: ["measurements", slug, metric, bucket] as const,
    queryFn: () => {
      const resolved = bounds ?? resolveRange("24h")!;
      const params = new URLSearchParams({
        metric,
        from: resolved.from,
        to: resolved.to,
        resolution: "auto",
      });
      return apiGet<MeasurementsResponse>(`/sensors/${slug}/measurements?${params.toString()}`);
    },
    enabled: isBrowser && bounds !== null,
    staleTime: 60_000,
    retry: 0,
  };
}

export function useMeasurements(
  slug: string,
  metric: string,
  range: RangeKey,
  custom?: CustomRange,
) {
  return useQuery(measurementsOptions(slug, metric, range, custom));
}

/** Несколько метрик сразу — для мультивыбора в истории. */
export function useMeasurementsMany(
  slug: string,
  metrics: string[],
  range: RangeKey,
  custom?: CustomRange,
) {
  return useQueries({
    queries: metrics.map((metric) => measurementsOptions(slug, metric, range, custom)),
  });
}
