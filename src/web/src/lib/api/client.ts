export const API_BASE = (import.meta.env["VITE_API_BASE_URL"] as string | undefined) ?? "/api/v1";

export const isBrowser = typeof window !== "undefined";

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    path: string,
  ) {
    super(`API request failed with status ${status}: ${path}`);
    this.name = "ApiError";
  }
}

export async function apiGet<Response>(path: string, signal?: AbortSignal): Promise<Response> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { Accept: "application/json" },
    ...(signal ? { signal } : {}),
  });
  if (!response.ok) throw new ApiError(response.status, path);
  return (await response.json()) as Response;
}

export function sensorPollInterval(pollIntervalSeconds: number | undefined): number {
  return Math.max(20_000, (pollIntervalSeconds ?? 0) * 1000);
}

export function sensorPath(slug: string): string {
  return `/sensors/${encodeURIComponent(slug)}`;
}
