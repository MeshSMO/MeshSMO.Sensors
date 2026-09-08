// Post-build step for the ASP.NET BFF contract (src/MeshSMO.Sensors.Web/Program.cs):
// - __spa-fallback.html is served for client-only routes (200 for registered
//   sensors, 404 body for unknown ones), so it must equal the prerendered shell;
//   the shell must NOT carry the home page's canonical link — the BFF serves it
//   under other URLs, and a wrong canonical in the raw HTML would override the
//   per-route canonical that only appears after hydration;
// - _headers is a Cloudflare-specific nitro artifact the BFF never uses.
import { copyFileSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const publicDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../.output/public");

copyFileSync(path.join(publicDir, "index.html"), path.join(publicDir, "__spa-fallback.html"));
const fallbackPath = path.join(publicDir, "__spa-fallback.html");
const shell = readFileSync(fallbackPath, "utf8").replace(/<link rel="canonical"[^>]*>\s*/g, "");
writeFileSync(fallbackPath, shell);
rmSync(path.join(publicDir, "_headers"), { force: true });
console.log("postbuild: wrote __spa-fallback.html (canonical stripped), removed _headers");
