import { createFileRoute } from "@tanstack/react-router";
import { createPageMeta } from "@/lib/seo";

const title = "О проекте MeshSMO Sensors — путь данных от эфира до страницы | MeshSMO";
const description =
  "Как устроен MeshSMO Sensors: LoRa-датчики, сеть MeshCore, опрос ретрансляторов, хранение и публикация показаний.";

export const Route = createFileRoute("/about")({
  head: () => ({
    meta: createPageMeta({ title, description }),
  }),
});
