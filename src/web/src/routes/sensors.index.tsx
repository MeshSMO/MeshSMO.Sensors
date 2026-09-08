import { createFileRoute } from "@tanstack/react-router";
import { translate } from "@/i18n";
import { createPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/sensors/")({
  head: () => ({
    meta: createPageMeta({
      title: translate("seo.sensors.title"),
      description: translate("seo.sensors.description"),
    }),
  }),
});
