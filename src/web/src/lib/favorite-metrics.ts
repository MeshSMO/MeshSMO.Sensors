import { useCallback, useSyncExternalStore } from "react";

const STORAGE_KEY = "meshsmo:metric-favorites";
const CHANGE_EVENT = "meshsmo:metric-favorites-change";
const EMPTY_FAVORITES: ReadonlyMap<string, ReadonlySet<string>> = new Map();

let cachedRawValue: string | null | undefined;
let cachedFavorites: ReadonlyMap<string, ReadonlySet<string>> = EMPTY_FAVORITES;

function parseFavorites(rawValue: string | null): ReadonlyMap<string, ReadonlySet<string>> {
  if (rawValue === null) {
    return EMPTY_FAVORITES;
  }

  try {
    const value: unknown = JSON.parse(rawValue);
    if (typeof value !== "object" || value === null || Array.isArray(value)) {
      return EMPTY_FAVORITES;
    }

    const entries = Object.entries(value)
      .filter((entry): entry is [string, unknown[]] => Array.isArray(entry[1]))
      .map(
        ([slug, metrics]) =>
          [
            slug,
            new Set(metrics.filter((metric): metric is string => typeof metric === "string")),
          ] as const,
      )
      .filter(([, metrics]) => metrics.size > 0);

    return new Map(entries);
  } catch {
    return EMPTY_FAVORITES;
  }
}

function getFavoritesSnapshot(): ReadonlyMap<string, ReadonlySet<string>> {
  if (typeof window === "undefined") {
    return EMPTY_FAVORITES;
  }

  let rawValue: string | null;
  try {
    rawValue = window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return EMPTY_FAVORITES;
  }

  if (rawValue !== cachedRawValue) {
    cachedRawValue = rawValue;
    cachedFavorites = parseFavorites(rawValue);
  }

  return cachedFavorites;
}

function subscribeToFavorites(onStoreChange: () => void) {
  const handleStorage = (event: StorageEvent) => {
    if (event.key === STORAGE_KEY || event.key === null) {
      onStoreChange();
    }
  };

  window.addEventListener("storage", handleStorage);
  window.addEventListener(CHANGE_EVENT, onStoreChange);

  return () => {
    window.removeEventListener("storage", handleStorage);
    window.removeEventListener(CHANGE_EVENT, onStoreChange);
  };
}

function toggleStoredFavorite(slug: string, metric: string) {
  const favorites = new Map(
    [...getFavoritesSnapshot()].map(([sensorSlug, metrics]) => [sensorSlug, new Set(metrics)]),
  );
  const sensorFavorites = favorites.get(slug) ?? new Set<string>();

  if (sensorFavorites.has(metric)) {
    sensorFavorites.delete(metric);
  } else {
    sensorFavorites.add(metric);
  }

  if (sensorFavorites.size > 0) {
    favorites.set(slug, sensorFavorites);
  } else {
    favorites.delete(slug);
  }

  const storedValue = Object.fromEntries(
    [...favorites].map(([sensorSlug, metrics]) => [sensorSlug, [...metrics]]),
  );

  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(storedValue));
  } catch {
    return;
  }

  cachedRawValue = undefined;
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

export function useFavoriteMetrics() {
  const favorites = useSyncExternalStore(
    subscribeToFavorites,
    getFavoritesSnapshot,
    () => EMPTY_FAVORITES,
  );
  const toggleFavorite = useCallback(
    (slug: string, metric: string) => toggleStoredFavorite(slug, metric),
    [],
  );

  return { favorites, toggleFavorite };
}
