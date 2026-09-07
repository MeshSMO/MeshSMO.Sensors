import { createFileRoute } from "@tanstack/react-router";
import { absoluteSiteUrl, createPageMeta } from "@/lib/seo";

const title = "MeshSMO Sensors — телеметрия LoRa-датчиков Смоленской области";
const description =
  "Публичные показания датчиков MeshSMO: температура, влажность, давление и заряд батареи. Данные передаются по радиосети MeshCore (LoRa).";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: createPageMeta({ title, description }),
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
              inLanguage: "ru-RU",
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
