import type { CSSProperties } from "react";

const layoutKey = "meshsmo:sensor-chart-layout";

export const minimumCardWidth = 320;
export const minimumCardHeight = 360;

export type CardSize = { width: number; height: number };
export type ChartLayout = { order: string[]; sizes: Record<string, CardSize> };

export const emptyLayout = (): ChartLayout => ({ order: [], sizes: {} });

export function readLayout(slug: string): ChartLayout {
  try {
    const raw = window.localStorage.getItem(layoutKey);
    if (!raw) return emptyLayout();
    const stored = JSON.parse(raw) as Record<string, Partial<ChartLayout>>;
    const layout = stored[slug];
    if (!layout) return emptyLayout();

    const order = Array.isArray(layout.order)
      ? layout.order.filter((id): id is string => typeof id === "string")
      : [];
    const sizes: Record<string, CardSize> = {};
    for (const [id, size] of Object.entries(layout.sizes ?? {})) {
      if (
        typeof size?.width === "number" &&
        Number.isFinite(size.width) &&
        typeof size.height === "number" &&
        Number.isFinite(size.height)
      ) {
        sizes[id] = {
          width: Math.min(1, Math.max(0.1, size.width)),
          height: Math.max(minimumCardHeight, size.height),
        };
      }
    }
    return { order, sizes };
  } catch {
    return emptyLayout();
  }
}

export function writeLayout(slug: string, layout: ChartLayout): void {
  try {
    const raw = window.localStorage.getItem(layoutKey);
    const parsed = raw ? (JSON.parse(raw) as unknown) : {};
    const stored = parsed && typeof parsed === "object" ? (parsed as Record<string, unknown>) : {};
    stored[slug] = layout;
    window.localStorage.setItem(layoutKey, JSON.stringify(stored));
  } catch {
    // The in-memory layout remains usable when browser storage is unavailable.
  }
}

export function toCardStyle(size: CardSize | undefined): CSSProperties {
  if (!size) return {};
  const percentage = Math.round(size.width * 10_000) / 100;
  return {
    "--history-card-width": `${percentage}%`,
    minHeight: `${Math.round(size.height)}px`,
  } as CSSProperties;
}
