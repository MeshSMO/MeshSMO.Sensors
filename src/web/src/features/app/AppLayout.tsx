import { QueryClientProvider } from "@tanstack/react-query";
import {
  getRouteApi,
  Link,
  Outlet,
  useRouter,
  type ErrorComponentProps,
} from "@tanstack/react-router";
import { useEffect } from "react";
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
  return (
    <PageShell>
      <div className="mx-auto max-w-md py-16 text-center">
        <p className="eyebrow">Ошибка 404</p>
        <h1 className="mt-4 text-3xl font-semibold tracking-tight">Страница не найдена</h1>
        <p className="mt-3 text-sm text-muted-foreground">
          Такой страницы нет или она была перемещена.
        </p>
        <Link
          to="/"
          className="mt-6 inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
        >
          На главную
        </Link>
      </div>
    </PageShell>
  );
}

export function RouteErrorPage({ error, reset }: ErrorComponentProps) {
  const router = useRouter();

  useEffect(() => {
    console.error(error);
    reportLovableError(error, { boundary: "tanstack_root_error_component" });
  }, [error]);

  return (
    <PageShell>
      <div className="mx-auto max-w-md py-16 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">Страница не загрузилась</h1>
        <p className="mt-3 text-sm text-muted-foreground">
          Что-то пошло не так. Попробуйте обновить данные или вернуться на главную.
        </p>
        <div className="mt-6 flex flex-wrap justify-center gap-2">
          <button
            type="button"
            onClick={() => {
              reset();
              void router.invalidate();
            }}
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            Попробовать снова
          </button>
          <Link
            to="/"
            className="inline-flex items-center justify-center rounded-md border border-input bg-background px-4 py-2 text-sm font-medium text-foreground transition-colors hover:bg-accent"
          >
            На главную
          </Link>
        </div>
      </div>
    </PageShell>
  );
}
