export const ranges = ["6h", "12h", "24h", "7d", "30d", "6m", "1y", "custom"] as const;
export type RangeKey = (typeof ranges)[number];

const rangeMilliseconds: Record<Exclude<RangeKey, "custom">, number> = {
  "6h": 6 * 3_600_000,
  "12h": 12 * 3_600_000,
  "24h": 24 * 3_600_000,
  "7d": 7 * 24 * 3_600_000,
  "30d": 30 * 24 * 3_600_000,
  "6m": 182 * 24 * 3_600_000,
  "1y": 365 * 24 * 3_600_000,
};

export type CustomRange = { from?: string | undefined; to?: string | undefined };

export function resolveRange(
  range: RangeKey,
  custom?: CustomRange,
  now = new Date(),
): { from: string; to: string } | null {
  if (range === "custom") {
    if (!custom?.from || !custom.to) return null;
    const from = new Date(custom.from);
    const to = new Date(custom.to);
    if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime()) || from >= to) return null;
    return { from: from.toISOString(), to: to.toISOString() };
  }

  return {
    from: new Date(now.getTime() - rangeMilliseconds[range]).toISOString(),
    to: now.toISOString(),
  };
}

export const densityPercent = { min: 1, max: 100 } as const;

/** Server-side hard ceiling for the maxPoints query parameter (MeasurementResolutionPolicy). */
export const maxPointsAbsolute = 50_000;

/**
 * Translates a density percentage (share of the raw measurement cadence kept in the
 * chart) into an explicit resolution plus a point budget. 100% keeps almost every
 * measurement, 50% roughly every second one; the coarsest step is one day.
 */
export function resolveDensity(
  densityPercentValue: number,
  rangeMilliseconds: number,
  pollIntervalSeconds: number | undefined,
): { resolution: string; maxPoints: number } {
  const clamped = Math.min(
    densityPercent.max,
    Math.max(densityPercent.min, Math.round(densityPercentValue)),
  );
  const intervalMs = Math.max(1, pollIntervalSeconds ?? 300) * 1000;
  const rawPoints = Math.max(1, Math.ceil(rangeMilliseconds / intervalMs));
  const target = Math.min(
    maxPointsAbsolute,
    Math.max(
      100,
      Math.ceil(rangeMilliseconds / 86_400_000),
      Math.ceil((rawPoints * clamped) / 100),
    ),
  );

  // Raw is only offered inside the server's 24-hour raw window; otherwise buckets apply.
  const candidates: ReadonlyArray<readonly [string, number]> = [
    ["raw", rangeMilliseconds <= 24 * 3_600_000 ? rawPoints : Number.POSITIVE_INFINITY],
    ["5m", rangeMilliseconds / 300_000],
    ["15m", rangeMilliseconds / 900_000],
    ["1h", rangeMilliseconds / 3_600_000],
    ["6h", rangeMilliseconds / 21_600_000],
    ["1d", rangeMilliseconds / 86_400_000],
  ];
  const resolution = candidates.find(([, points]) => points <= target)?.[0] ?? "1d";
  return { resolution, maxPoints: target };
}
