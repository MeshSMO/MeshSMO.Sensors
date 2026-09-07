import { createLazyFileRoute } from "@tanstack/react-router";
import { SensorNotFoundPage, SensorPage } from "@/features/sensor-detail/SensorPage";

export const Route = createLazyFileRoute("/sensors/$slug")({
  component: SensorPage,
  notFoundComponent: SensorNotFoundPage,
});
