# Localization

The UI currently supports and selects one locale: `ru-RU`. There is intentionally no language
detector, persisted language preference, switcher, or English fallback.

- `config.ts` is the single source of truth for the active BCP 47 locale and related locale
  adapters.
- `locales/ru-RU/translation.ts` contains all user-facing copy, accessibility labels, SEO text,
  metric names, and units.
- `index.ts` initializes an isolated i18next instance with bundled resources so prerendering and
  the SPA use the same deterministic locale.
- `formatters.ts` owns cached `Intl` formatters. Do not use the runtime's implicit locale in UI
  code.
- `i18next.d.ts` makes translation keys and interpolation variables type-safe.

When another language is actually required, add its complete catalog first, then extend
`supportedLocales` and introduce explicit locale selection. Do not add partial fallback catalogs.
