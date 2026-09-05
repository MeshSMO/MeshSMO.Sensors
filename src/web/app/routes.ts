import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/home.tsx"),
  route("sensors", "routes/sensors.tsx"),
  route("sensors/:slug", "routes/sensors.$slug.tsx"),
  route("about", "routes/about.tsx"),
] satisfies RouteConfig;
