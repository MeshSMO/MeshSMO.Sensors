import { createLazyFileRoute } from "@tanstack/react-router";
import { HomePage } from "@/features/home/HomePage";

export const Route = createLazyFileRoute("/")({
  component: HomePage,
});
