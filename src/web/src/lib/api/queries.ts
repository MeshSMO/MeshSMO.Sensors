import { queryOptions, useQueries, useQuery } from "@tanstack/react-query";
import { apiGet, isBrowser, sensorPath, sensorPollInterval } from "./client";
import { resolveRange, type CustomRange, type RangeKey } from "./ranges";
import type {
  DashboardResponse,
  ForecastHorizon,
  ForecastResponse,
  LatestResponse,
  MeasurementsResponse,
  SensorDetail,
  SensorStatus,
  SensorSummary,
} from "./types";

export const dashboardQueryOptions = () =>
  queryOptions({
    queryKey: ["dashboard"],
    queryFn: ({ signal }) => apiGet<DashboardResponse>("/dashboard", signal),
    enabled: isBrowser,
    refetchInterval: 20_000,
    retry: 1,
  });

export const sensorsQueryOptions = () =>
  queryOptions({
    queryKey: ["sensors"],
    queryFn: ({ signal }) => apiGet<{ sensors: SensorSummary[] }>("/sensors", signal),
    enabled: isBrowser,
    refetchInterval: 30_000,
    retry: 1,
  });

export const sensorQueryOptions = (slug: string) =>
  queryOptions({
    queryKey: ["sensor", slug],
    queryFn: ({ signal }) => apiGet<SensorDetail>(sensorPath(slug), signal),
    enabled: isBrowser,
    retry: 1,
  });

export const statusQueryOptions = (slug: string, pollIntervalSeconds?: number) =>
  queryOptions({
    queryKey: ["status", slug],
    queryFn: ({ signal }) => apiGet<SensorStatus>(`${sensorPath(slug)}/status`, signal),
    enabled: isBrowser,
    refetchInterval: sensorPollInterval(pollIntervalSeconds),
    retry: 1,
  });

export const latestQueryOptions = (slug: string, pollIntervalSeconds?: number) =>
  queryOptions({
    queryKey: ["latest", slug],
    queryFn: ({ signal }) => apiGet<LatestResponse>(`${sensorPath(slug)}/latest`, signal),
    enabled: isBrowser,
    refetchInterval: sensorPollInterval(pollIntervalSeconds),
    retry: 1,
  });

export const measurementsQueryOptions = (
  slug: string,
  metric: string,
  range: RangeKey,
  custom?: CustomRange,
) => {
  const bounds = resolveRange(range, custom);
  const rangeKey = range === "custom" ? `${bounds?.from ?? ""}|${bounds?.to ?? ""}` : range;

  return queryOptions({
    queryKey: ["measurements", slug, metric, rangeKey] as const,
    queryFn: ({ signal }) => {
      const resolved = bounds ?? resolveRange("24h");
      if (!resolved) throw new Error("The default measurement range is invalid");
      const parameters = new URLSearchParams({
        metric,
        from: resolved.from,
        to: resolved.to,
        resolution: "auto",
      });
      return apiGet<MeasurementsResponse>(
        `${sensorPath(slug)}/measurements?${parameters.toString()}`,
        signal,
      );
    },
    enabled: isBrowser && bounds !== null,
    staleTime: 60_000,
    retry: 0,
  });
};

export const forecastQueryOptions = (
  slug: string,
  metric: string | undefined,
  horizon: ForecastHorizon,
  enabled: boolean,
) =>
  queryOptions({
    queryKey: ["forecast", slug, metric, horizon] as const,
    queryFn: ({ signal }) => {
      if (!metric) throw new Error("A metric is required to build a forecast");
      const parameters = new URLSearchParams({ metric, horizon });
      return apiGet<ForecastResponse>(
        `${sensorPath(slug)}/forecast?${parameters.toString()}`,
        signal,
      );
    },
    enabled: isBrowser && enabled && metric !== undefined,
    retry: 0,
  });

export function useDashboard() {
  return useQuery(dashboardQueryOptions());
}

export function useSensors() {
  return useQuery(sensorsQueryOptions());
}

export function useSensor(slug: string) {
  return useQuery(sensorQueryOptions(slug));
}

export function useStatus(slug: string, pollIntervalSeconds?: number) {
  return useQuery(statusQueryOptions(slug, pollIntervalSeconds));
}

export function useLatest(slug: string, pollIntervalSeconds?: number) {
  return useQuery(latestQueryOptions(slug, pollIntervalSeconds));
}

export function useMeasurements(
  slug: string,
  metric: string,
  range: RangeKey,
  custom?: CustomRange,
) {
  return useQuery(measurementsQueryOptions(slug, metric, range, custom));
}

export function useMeasurementsMany(
  slug: string,
  metrics: string[],
  range: RangeKey,
  custom?: CustomRange,
) {
  return useQueries({
    queries: metrics.map((metric) => measurementsQueryOptions(slug, metric, range, custom)),
  });
}

export function useForecast(
  slug: string,
  metric: string | undefined,
  horizon: ForecastHorizon,
  enabled: boolean,
) {
  return useQuery(forecastQueryOptions(slug, metric, horizon, enabled));
}
