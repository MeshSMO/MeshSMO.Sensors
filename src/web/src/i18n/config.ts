import { ru } from "date-fns/locale";

export const supportedLocales = ["ru-RU"] as const;
export type AppLocale = (typeof supportedLocales)[number];

export const defaultLocale: AppLocale = "ru-RU";
export const htmlLanguage = defaultLocale;
export const openGraphLocale = "ru_RU";
export const textDirection = "ltr";
export const dateFnsLocale = ru;
