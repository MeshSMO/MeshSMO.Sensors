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
    { name: "twitter:card", content: "summary_large_image" },
    ...(robots ? [{ name: "robots", content: robots }] : []),
  ];
}

export function absoluteSiteUrl(path = "/"): string {
  return new URL(path, siteUrl).toString();
}
