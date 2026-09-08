import { createLazyFileRoute } from "@tanstack/react-router";
import { SensorsPage } from "@/features/sensors/SensorsPage";

export const Route = createLazyFileRoute("/sensors/")({
  component: SensorsPage,
});
