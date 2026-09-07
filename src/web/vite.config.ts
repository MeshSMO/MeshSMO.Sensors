// @lovable.dev/vite-tanstack-config already includes the following — do NOT add them manually
// or the app will break with duplicate plugins:
//   - TanStack devtools (dev-only, first), tanstackStart, viteReact, tailwindcss, tsConfigPaths,
//     nitro (build-only using cloudflare as a default target), VITE_* env injection, @ path alias,
//     React/TanStack dedupe, error logger plugins, and sandbox detection (port/host/strictPort).
// You can pass additional config via defineConfig({ vite: { ... }, etc... }) if needed.
import { defineConfig } from "@lovable.dev/vite-tanstack-config";
import { listIndexableSensors } from "./src/lib/registry";

const backendUrl =
  process.env["ASPNETCORE_URLS"]?.split(";").find((url) => url.startsWith("http://")) ??
  "http://localhost:5200";

// Prerendered set must stay in sync with the BFF: static files exist only for
// these paths, everything else is handled by the SPA fallback (or 404).
const prerenderPaths = [
  "/",
  "/sensors",
  "/map",
  "/about",
  ...listIndexableSensors().map((sensor) => `/sensors/${sensor.slug}`),
];

export default defineConfig({
  tanstackStart: {
    // Redirect TanStack Start's bundled server entry to src/server.ts.
    // nitro/vite builds from this.
    server: { entry: "server" },
    // SPA mode: no SSR at runtime — the build prerenders static HTML for the
    // pages above and the app hydrates client-side. Only .output/public
    // ships: the ASP.NET BFF serves it as wwwroot, so Node.js is not required
    // (or present) in production.
    spa: {
      enabled: true,
      prerender: {
        outputPath: "/index.html",
      },
    },
    prerender: {
      enabled: true,
      // Link crawling would also pick up non-indexable routes; keep the
      // static set exactly equal to the BFF-visible, indexable pages.
      filter: (page: { path: string }) => prerenderPaths.includes(page.path),
    },
    pages: prerenderPaths.map((path) => ({ path, prerender: { enabled: true } })),
  },
  vite: {
    server: {
      host: "127.0.0.1",
      port: 5173,
      strictPort: true,
      proxy: {
        "/api": backendUrl,
        "/health": backendUrl,
      },
    },
  },
});
