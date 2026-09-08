const siteUrl = "https://sensors.meshsmo.ru";

export type PageSeo = {
  title: string;
  description: string;
  robots?: string;
};

export function createPageMeta({ title, description, robots }: PageSeo) {
  return [
    { title },
    { name: "description", content: description },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
    { property: "og:type", content: "website" },
    { name: "twitter:card", content: "summary" },
    ...(robots ? [{ name: "robots", content: robots }] : []),
  ];
}

// Canonical URLs use the trailing-slash form: UseDefaultFiles in the BFF 301s
// "/x" to "/x/" before serving "x/index.html", so the slashed URL is the one
// that serves 200 content.
export function absoluteSiteUrl(path = "/"): string {
  const normalized = path.endsWith("/") ? path : `${path}/`;
  return new URL(normalized, siteUrl).toString();
}
