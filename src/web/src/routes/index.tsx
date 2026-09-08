import { createFileRoute } from "@tanstack/react-router";
import { defaultLocale } from "@/i18n/config";
import { translate } from "@/i18n";
import { absoluteSiteUrl, createPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: createPageMeta({
      title: translate("seo.home.title"),
      description: translate("seo.home.description"),
    }),
    scripts: [
      {
        type: "application/ld+json",
        children: JSON.stringify({
          "@context": "https://schema.org",
          "@graph": [
            {
              "@type": "WebSite",
              name: "MeshSMO Sensors",
              url: absoluteSiteUrl(),
              inLanguage: defaultLocale,
            },
            {
              "@type": "Organization",
              name: "MeshSMO",
              url: absoluteSiteUrl(),
            },
          ],
        }),
      },
    ],
  }),
});
