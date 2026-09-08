import "i18next";
import type { defaultNamespace, resources } from "./resources";

declare module "i18next" {
  interface CustomTypeOptions {
    defaultNS: typeof defaultNamespace;
    resources: (typeof resources)["ru-RU"];
    returnNull: false;
    enableSelector: false;
    strictKeyChecks: true;
  }
}
