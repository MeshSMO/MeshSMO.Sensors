import { createFileRoute } from "@tanstack/react-router";
import { createPageMeta } from "@/lib/seo";

const title = "Публичные датчики MeshSMO — список и статусы | MeshSMO";
const description =
  "Список публичных LoRa-датчиков MeshSMO с текущими статусами: температура, влажность, давление, батарея.";

export const Route = createFileRoute("/sensors/")({
  head: () => ({
    meta: createPageMeta({ title, description }),
  }),
});
