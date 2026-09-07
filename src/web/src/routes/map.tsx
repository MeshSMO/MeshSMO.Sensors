import { createFileRoute } from "@tanstack/react-router";
import { absoluteSiteUrl, createPageMeta } from "@/lib/seo";

const title = "Карта датчиков MeshSMO — LoRa-телеметрия на карте | MeshSMO";
const description =
  "Интерактивная карта публичных LoRa-датчиков MeshSMO: расположение узлов, текущие статусы, показания и графики по клику.";

export const Route = createFileRoute("/map")({
  head: () => ({
    meta: createPageMeta({ title, description }),
    links: [
      { rel: "canonical", href: absoluteSiteUrl("/map") },
      { rel: "stylesheet", href: "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" },
    ],
  }),
});
