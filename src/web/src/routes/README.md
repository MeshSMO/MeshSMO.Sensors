# Routes

TanStack Start uses **file-based routing**. Every `.tsx` file in this directory
defines a route. Do **not** create `src/pages/`, `src/routes/_app/index.tsx`, or
`app/layout.tsx` — those are Next.js / Remix conventions. The only root layout
is `src/routes/__root.tsx`.

## Conventions

| File                     | URL                                                     |
| ------------------------ | ------------------------------------------------------- |
| `index.tsx`              | `/`                                                     |
| `about.tsx`              | `/about`                                                |
| `map.tsx`                | `/map`                                                  |
| `users/index.tsx`        | `/users`                                                |
| `users/$id.tsx`          | `/users/:id` (dynamic — bare `$`, no curly braces)      |
| `posts/{-$category}.tsx` | `/posts/:category?` (optional segment)                  |
| `files/$.tsx`            | `/files/*` (splat — read via `_splat` param, never `*`) |
| `_layout.tsx`            | layout route (renders children via `<Outlet />`)        |
| `__root.tsx`             | app shell — wraps every page; preserve `<Outlet />`     |

`routeTree.gen.ts` is auto-generated. Don't edit it by hand.

## Route structure

- Keep route files thin: path params, validated search params, loaders, metadata, and other
  navigation-critical configuration belong in `<route>.tsx`.
- Put the component and route-level error/not-found UI in `<route>.lazy.tsx` with
  `createLazyFileRoute`. This keeps page code out of the initial bundle while leaving loaders
  available for intent preloading.
- Put product UI and state in `src/features`; a lazy route should normally only connect typed route
  data to a feature component.
- Validate every search parameter at the route boundary. Prefer recoverable defaults for public
  URLs so malformed or stale bookmarks do not take down the page.
- Signal missing resources from a loader with `notFound()` and render them with the nearest
  `notFoundComponent`; don't emulate a 404 from inside a normal page component.
