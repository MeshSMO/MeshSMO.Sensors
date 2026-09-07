import { useCallback, useSyncExternalStore } from "react";

const STORAGE_KEY = "meshsmo:sensor-favorites";
const CHANGE_EVENT = "meshsmo:sensor-favorites-change";
const EMPTY_FAVORITES: ReadonlySet<string> = new Set();

let cachedRawValue: string | null | undefined;
let cachedFavorites: ReadonlySet<string> = EMPTY_FAVORITES;

function parseFavorites(rawValue: string | null): ReadonlySet<string> {
  if (rawValue === null) {
    return EMPTY_FAVORITES;
  }

  try {
    const value: unknown = JSON.parse(rawValue);
    if (!Array.isArray(value)) {
      return EMPTY_FAVORITES;
    }

    return new Set(value.filter((slug): slug is string => typeof slug === "string"));
  } catch {
    return EMPTY_FAVORITES;
  }
}

function getFavoritesSnapshot(): ReadonlySet<string> {
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

function toggleStoredFavorite(slug: string) {
  const favorites = new Set(getFavoritesSnapshot());

  if (favorites.has(slug)) {
    favorites.delete(slug);
  } else {
    favorites.add(slug);
  }

  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify([...favorites]));
  } catch {
    return;
  }

  cachedRawValue = undefined;
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

export function useFavoriteSensors() {
  const favorites = useSyncExternalStore(
    subscribeToFavorites,
    getFavoritesSnapshot,
    () => EMPTY_FAVORITES,
  );
  const toggleFavorite = useCallback((slug: string) => toggleStoredFavorite(slug), []);

  return { favorites, toggleFavorite };
}
