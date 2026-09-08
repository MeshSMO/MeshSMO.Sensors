import type { QueryClient } from "@tanstack/react-query";
import { createRootRouteWithContext, HeadContent, Scripts } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { I18nextProvider } from "react-i18next";

import appCss from "../styles.css?url";
import { AppLayout, RouteErrorPage, RouteNotFoundPage } from "@/features/app/AppLayout";
import { htmlLanguage, openGraphLocale, textDirection } from "@/i18n/config";
import { i18n, translate } from "@/i18n";

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  head: () => ({
    meta: [
      { charSet: "utf-8" },
      { name: "viewport", content: "width=device-width, initial-scale=1" },
      { title: translate("seo.root.title") },
      {
        name: "description",
        content: translate("seo.root.description"),
      },
      { name: "author", content: "MeshSMO" },
      { property: "og:title", content: "MeshSMO Sensors" },
      { property: "og:description", content: translate("seo.root.socialDescription") },
      { property: "og:type", content: "website" },
      { property: "og:locale", content: openGraphLocale },
      { name: "twitter:card", content: "summary_large_image" },
    ],
    links: [
      {
        rel: "stylesheet",
        href: appCss,
      },
      { rel: "icon", href: "/favicon.ico", type: "image/x-icon" },
      { rel: "preconnect", href: "https://fonts.googleapis.com" },
      { rel: "preconnect", href: "https://fonts.gstatic.com", crossOrigin: "anonymous" },
      {
        rel: "stylesheet",
        href: "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap",
      },
    ],
  }),
  shellComponent: RootShell,
  component: AppLayout,
  notFoundComponent: RouteNotFoundPage,
  errorComponent: RouteErrorPage,
});

function RootShell({ children }: { children: ReactNode }) {
  return (
    <html lang={htmlLanguage} dir={textDirection}>
      <head>
        <HeadContent />
      </head>
      <body>
        <I18nextProvider i18n={i18n}>{children}</I18nextProvider>
        <Scripts />
      </body>
    </html>
  );
}
