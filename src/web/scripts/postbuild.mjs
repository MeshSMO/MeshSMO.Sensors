// Post-build step for the ASP.NET BFF contract (src/MeshSMO.Sensors.Web/Program.cs):
// - __spa-fallback.html is served for client-only routes (200 for registered
//   sensors, 404 for unknown ones), so it must equal the prerendered shell;
// - _headers is a Cloudflare-specific nitro artifact the BFF never uses.
import { copyFileSync, rmSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const publicDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../.output/public");

copyFileSync(path.join(publicDir, "index.html"), path.join(publicDir, "__spa-fallback.html"));
rmSync(path.join(publicDir, "_headers"), { force: true });
console.log("postbuild: wrote __spa-fallback.html, removed _headers");
