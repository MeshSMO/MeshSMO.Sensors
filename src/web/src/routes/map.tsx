import { createFileRoute } from "@tanstack/react-router";
import { translate } from "@/i18n";
import { absoluteSiteUrl, createPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/map")({
  head: () => ({
    meta: createPageMeta({
      title: translate("seo.map.title"),
      description: translate("seo.map.description"),
    }),
    links: [
      { rel: "canonical", href: absoluteSiteUrl("/map") },
      { rel: "stylesheet", href: "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" },
    ],
  }),
});
