import i18next from "i18next";
import { initReactI18next } from "react-i18next";
import { defaultLocale, supportedLocales } from "./config";
import { defaultNamespace, resources } from "./resources";

export const i18n = i18next.createInstance();

void i18n.use(initReactI18next).init({
  resources,
  lng: defaultLocale,
  fallbackLng: defaultLocale,
  supportedLngs: [...supportedLocales],
  ns: [defaultNamespace],
  defaultNS: defaultNamespace,
  initAsync: false,
  load: "currentOnly",
  returnNull: false,
  interpolation: {
    escapeValue: false,
  },
});

export const translate = i18n.t.bind(i18n);
