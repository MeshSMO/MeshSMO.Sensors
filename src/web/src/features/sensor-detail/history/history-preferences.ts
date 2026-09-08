import { useEffect, useRef, useState } from "react";
import { parseSensorSearch, type ChartMode, type SensorSearch } from "../route-config";
import type { RangeKey } from "@/lib/api";

const preferencesKey = "meshsmo:sensor-history";

type HistoryPreferences = Pick<
  SensorSearch,
  "metrics" | "range" | "from" | "to" | "mode" | "forecast"
>;

type SearchPatch = { [Key in keyof SensorSearch]?: SensorSearch[Key] | undefined };

function readPreferences(slug: string): HistoryPreferences | null {
  try {
    const raw = window.localStorage.getItem(preferencesKey);
    if (!raw) return null;
    const all = JSON.parse(raw) as Record<string, unknown>;
    const stored = all[slug];
    if (!stored || typeof stored !== "object") return null;
    const preferences = parseSensorSearch(stored as Record<string, unknown>);
    return Object.keys(preferences).length > 0 ? preferences : null;
  } catch {
    return null;
  }
}

function writePreferences(slug: string, preferences: HistoryPreferences): void {
  try {
    const raw = window.localStorage.getItem(preferencesKey);
    const parsed = raw ? (JSON.parse(raw) as unknown) : {};
    const all = parsed && typeof parsed === "object" ? (parsed as Record<string, unknown>) : {};
    all[slug] = preferences;
    window.localStorage.setItem(preferencesKey, JSON.stringify(all));
  } catch {
    // The UI remains functional when browser storage is unavailable.
  }
}

export function useHistoryPreferences({
  slug,
  hasExplicitSearch,
  selected,
  range,
  mode,
  search,
  updateSearch,
}: {
  slug: string;
  hasExplicitSearch: boolean;
  selected: string[];
  range: RangeKey;
  mode: ChartMode;
  search: SensorSearch;
  updateSearch: (patch: SearchPatch) => Promise<void>;
}) {
  const [readySlug, setReadySlug] = useState<string | null>(null);
  const lastSaved = useRef<{ slug: string; value: string } | null>(null);

  useEffect(() => {
    let cancelled = false;

    const restore = async () => {
      if (!hasExplicitSearch) {
        const saved = readPreferences(slug);
        if (saved) await updateSearch(saved);
      }
      if (!cancelled) setReadySlug(slug);
    };

    void restore();
    return () => {
      cancelled = true;
    };
    // Run once for each sensor. Search changes caused by restoration must not restart it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [slug]);

  useEffect(() => {
    if (readySlug !== slug) return;

    const preferences: HistoryPreferences = { metrics: selected.join(","), range };
    if (search.from) preferences.from = search.from;
    if (search.to) preferences.to = search.to;
    if (mode === "combined") preferences.mode = mode;
    if (search.forecast) preferences.forecast = search.forecast;

    const value = JSON.stringify(preferences);
    if (lastSaved.current?.slug === slug && lastSaved.current.value === value) return;
    lastSaved.current = { slug, value };
    writePreferences(slug, preferences);
  }, [mode, range, readySlug, search.forecast, search.from, search.to, selected, slug]);
}

export type { SearchPatch };
