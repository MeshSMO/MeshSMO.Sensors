import { createFileRoute } from "@tanstack/react-router";
import { translate } from "@/i18n";
import { createPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/about")({
  head: () => ({
    meta: createPageMeta({
      title: translate("seo.about.title"),
      description: translate("seo.about.description"),
    }),
  }),
});
