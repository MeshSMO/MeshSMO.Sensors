import { useEffect, useRef } from "react";
import type { SensorState } from "@/lib/format";

export type MapPoint = {
  slug: string;
  displayName: string;
  latitude: number;
  longitude: number;
  state: SensorState | null;
};

const dotClass: Record<SensorState, string> = {
  Online: "bg-online rounded-full",
  Degraded: "bg-degraded rotate-45",
  Offline: "bg-offline rounded-[2px]",
  Unknown: "bg-unknown rounded-full opacity-60",
};

/**
 * Карта на Leaflet. Библиотека грузится динамически уже после гидрации,
 * поэтому пререндер остаётся статичным и без сетевых запросов.
 */
export default function SensorMap({
  points,
  selected,
  onSelect,
}: {
  points: MapPoint[];
  selected: string | null;
  onSelect: (slug: string) => void;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<import("leaflet").Map | null>(null);
  const leafletRef = useRef<typeof import("leaflet") | null>(null);
  const markersRef = useRef<Map<string, import("leaflet").Marker>>(new Map());
  const pointsRef = useRef(points);
  const selectedRef = useRef(selected);
  const selectRef = useRef(onSelect);
  pointsRef.current = points;
  selectedRef.current = selected;
  selectRef.current = onSelect;

  useEffect(() => {
    let disposed = false;
    const markers = markersRef.current;

    void (async () => {
      const L = await import("leaflet");
      if (disposed || !containerRef.current || mapRef.current) return;
      leafletRef.current = L;

      const map = L.map(containerRef.current, {
        scrollWheelZoom: true,
        attributionControl: true,
      });
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        className: "map-dark-tiles",
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      }).addTo(map);
      mapRef.current = map;
      render();
    })();

    return () => {
      disposed = true;
      mapRef.current?.remove();
      mapRef.current = null;
      markers.clear();
    };
  }, []);

  function render() {
    const L = leafletRef.current;
    const map = mapRef.current;
    if (!L || !map) return;

    const current = pointsRef.current;
    const seen = new Set<string>();

    for (const point of current) {
      seen.add(point.slug);
      const state = point.state ?? "Unknown";
      const isActive = selectedRef.current === point.slug;
      const icon = L.divIcon({
        className: "",
        html: `<span class="flex h-6 w-6 items-center justify-center rounded-full border ${
          isActive ? "border-accent bg-accent/25" : "border-border bg-surface/90"
        }"><span class="h-2.5 w-2.5 ${dotClass[state]}"></span></span>`,
        iconSize: [24, 24],
        iconAnchor: [12, 12],
      });

      const existing = markersRef.current.get(point.slug);
      if (existing) {
        existing.setLatLng([point.latitude, point.longitude]);
        existing.setIcon(icon);
      } else {
        const marker = L.marker([point.latitude, point.longitude], {
          icon,
          title: point.displayName,
          keyboard: true,
          alt: point.displayName,
        })
          .addTo(map)
          .on("click", () => selectRef.current(point.slug))
          .on("keypress", () => selectRef.current(point.slug));
        markersRef.current.set(point.slug, marker);
      }
    }

    for (const [slug, marker] of markersRef.current) {
      if (!seen.has(slug)) {
        marker.remove();
        markersRef.current.delete(slug);
      }
    }

    if (!map.getZoom() && current.length > 0) {
      const bounds = L.latLngBounds(
        current.map((p) => [p.latitude, p.longitude] as [number, number]),
      );
      map.fitBounds(bounds.pad(0.4), { maxZoom: 13 });
    }
  }

  useEffect(render);

  useEffect(() => {
    const map = mapRef.current;
    const point = points.find((p) => p.slug === selected);
    if (!map || !point) return;
    map.panTo([point.latitude, point.longitude], { animate: true });
  }, [selected, points]);

  return <div ref={containerRef} className="h-full w-full" aria-label="Карта датчиков" />;
}
