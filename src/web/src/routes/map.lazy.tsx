import { createLazyFileRoute } from "@tanstack/react-router";
import { MapPage } from "@/features/map/MapPage";

export const Route = createLazyFileRoute("/map")({
  component: MapPage,
});
