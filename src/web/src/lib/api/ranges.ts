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
