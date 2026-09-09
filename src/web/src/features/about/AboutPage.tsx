import { useTranslation } from "react-i18next";

export function AboutPage() {
  const { t } = useTranslation();
  const steps = t("about.steps", { returnObjects: true });

  return (
    <>
      <p className="eyebrow">{t("about.eyebrow")}</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">{t("about.title")}</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">{t("about.description")}</p>

      <h2 className="mt-10 text-2xl font-semibold tracking-tight">{t("about.howTitle")}</h2>
      <ol className="mt-4 grid gap-4 sm:grid-cols-2">
        {steps.map((step) => (
          <li key={step.title} className="panel px-5 py-5">
            <h2 className="text-base font-semibold">{step.title}</h2>
            <p className="mt-2 text-sm text-muted-foreground">{step.text}</p>
          </li>
        ))}
      </ol>

      <section className="panel mt-10 px-5 py-5">
        <h2 className="text-base font-semibold">{t("about.limitations.title")}</h2>
        <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
          <li>{t("about.limitations.coordinates")}</li>
          <li>{t("about.limitations.gaps")}</li>
          <li>{t("about.limitations.measurements")}</li>
          <li>{t("about.limitations.thresholds")}</li>
        </ul>
      </section>
    </>
  );
}
