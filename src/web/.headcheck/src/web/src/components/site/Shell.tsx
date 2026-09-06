import { Link } from "@tanstack/react-router";
import type { ReactNode } from "react";

export function Header() {
  return (
    <header className="sticky top-0 z-40 border-b border-border/80 bg-background/80 backdrop-blur">
      <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
        <Link to="/" className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-md border border-border bg-surface-raised">
            <span className="h-2 w-2 rounded-full bg-accent" aria-hidden />
          </span>
          <span className="text-sm font-semibold tracking-tight">
            MeshSMO <span className="text-muted-foreground">Sensors</span>
          </span>
        </Link>
        <nav className="flex items-center gap-1 text-sm">
          <NavLink to="/">Главная</NavLink>
          <NavLink to="/sensors">Датчики</NavLink>
          <NavLink to="/about">О проекте</NavLink>
        </nav>
      </div>
    </header>
  );
}

function NavLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Link
      to={to}
      activeOptions={{ exact: to === "/" }}
      activeProps={{ className: "text-foreground bg-surface" }}
      className="rounded-md px-3 py-1.5 text-muted-foreground transition-colors hover:text-foreground"
    >
      {children}
    </Link>
  );
}

export function Footer() {
  return (
    <footer className="mt-16 border-t border-border">
      <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-8 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between sm:px-6">
        <p>Данные поступают по LoRa-сети MeshCore.</p>
        <p className="num">sensors.meshsmo.ru · Смоленская область</p>
      </div>
    </footer>
  );
}

export function PageShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col">
      <Header />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10 sm:px-6">{children}</main>
      <Footer />
    </div>
  );
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="panel flex flex-col items-center gap-2 px-6 py-10 text-center">
      <p className="text-sm font-medium">{title}</p>
      <p className="max-w-md text-sm text-muted-foreground">{description}</p>
    </div>
  );
}

export function SkeletonLine({ className = "" }: { className?: string }) {
  return <span className={`block animate-pulse rounded bg-muted ${className}`} aria-hidden />;
}
