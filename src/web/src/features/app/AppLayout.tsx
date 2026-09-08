import { QueryClientProvider } from "@tanstack/react-query";
import {
  getRouteApi,
  Link,
  Outlet,
  useRouter,
  type ErrorComponentProps,
} from "@tanstack/react-router";
import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { PageShell } from "@/components/site/Shell";
import { reportLovableError } from "@/lib/lovable-error-reporting";

const rootRoute = getRouteApi("__root__");

export function AppLayout() {
  const { queryClient } = rootRoute.useRouteContext();

  return (
    <QueryClientProvider client={queryClient}>
      <PageShell>
        <Outlet />
      </PageShell>
    </QueryClientProvider>
  );
}

export function RouteNotFoundPage() {
  const { t } = useTranslation();

  return (
    <PageShell>
      <div className="mx-auto max-w-md py-16 text-center">
        <p className="eyebrow">{t("errors.notFound.eyebrow")}</p>
        <h1 className="mt-4 text-3xl font-semibold tracking-tight">{t("errors.notFound.title")}</h1>
        <p className="mt-3 text-sm text-muted-foreground">{t("errors.notFound.description")}</p>
        <Link
          to="/"
          className="mt-6 inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
        >
          {t("common.actions.goHome")}
        </Link>
      </div>
    </PageShell>
  );
}

export function RouteErrorPage({ error, reset }: ErrorComponentProps) {
  const router = useRouter();
  const { t } = useTranslation();

  useEffect(() => {
    console.error(error);
    reportLovableError(error, { boundary: "tanstack_root_error_component" });
  }, [error]);

  return (
    <PageShell>
      <div className="mx-auto max-w-md py-16 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">{t("errors.route.title")}</h1>
        <p className="mt-3 text-sm text-muted-foreground">{t("errors.route.description")}</p>
        <div className="mt-6 flex flex-wrap justify-center gap-2">
          <button
            type="button"
            onClick={() => {
              reset();
              void router.invalidate();
            }}
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            {t("common.actions.tryAgain")}
          </button>
          <Link
            to="/"
            className="inline-flex items-center justify-center rounded-md border border-input bg-background px-4 py-2 text-sm font-medium text-foreground transition-colors hover:bg-accent"
          >
            {t("common.actions.goHome")}
          </Link>
        </div>
      </div>
    </PageShell>
  );
}
