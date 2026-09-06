import type { Config } from "@react-router/dev/config";
import { listIndexableSensors } from "./app/lib/registry";

export default {
  ssr: false,
  async prerender() {
    // Sensor URLs come from the GitOps registry in config/sensors so that the
    // build stays reproducible and CI validates route generation.
    return [
      "/",
      "/sensors",
      "/about",
      ...listIndexableSensors().map((sensor) => `/sensors/${sensor.slug}`),
    ];
  },
} satisfies Config;
