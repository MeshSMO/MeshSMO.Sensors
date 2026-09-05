import { reactRouter } from "@react-router/dev/vite";
import { defineConfig } from "vite";

const backendUrl =
  process.env.ASPNETCORE_URLS?.split(";").find((url) => url.startsWith("http://")) ??
  "http://localhost:5200";

export default defineConfig({
  plugins: [reactRouter()],
  resolve: {
    tsconfigPaths: true,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": backendUrl,
      "/health": backendUrl,
    },
  },
});
