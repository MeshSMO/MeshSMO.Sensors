import { createFileRoute } from "@tanstack/react-router";
import {
  createSensorHead,
  loadRegistrySensor,
  parseSensorSearch,
} from "@/features/sensor-detail/route-config";

export const Route = createFileRoute("/sensors/$slug")({
  validateSearch: parseSensorSearch,
  loader: ({ params }) => loadRegistrySensor(params.slug),
  head: ({ loaderData }) => (loaderData ? createSensorHead(loaderData) : {}),
});
