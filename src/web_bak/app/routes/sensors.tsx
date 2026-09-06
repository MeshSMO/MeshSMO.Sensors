import type { Route } from "./+types/sensors";
import { SiteHeader } from "../components/site";
import { listVisibleSensors, type RegistrySensor } from "../lib/registry";
import { publicBaseUrl } from "../lib/baseUrl";

export const meta: Route.MetaFunction = () => [
  { title: "Датчики MeshSMO" },
  {
    name: "description",
    content:
      "Список публичных датчиков сети MeshSMO: температура, влажность, давление и заряд батареи узлов MeshCore в Смоленской области.",
  },
  { rel: "canonical", href: `${publicBaseUrl()}/sensors` },
  { property: "og:title", content: "Датчики MeshSMO" },
  { property: "og:description", content: "Публичные датчики сети MeshSMO и их показания." },
  { property: "og:url", content: `${publicBaseUrl()}/sensors` },
  { property: "og:type", content: "website" },
];

const metricLabels: Record<string, string> = {
  temperature: "температура",
  humidity: "влажность",
  pressure: "давление",
  battery: "батарея",
};

export default function Sensors() {
  const sensors = listVisibleSensors();

  return (
    <div className="page-shell">
      <SiteHeader />
      <main>
        <section className="hero">
          <p className="eyebrow">Сеть MeshSMO</p>
          <h1>Публичные датчики</h1>
          <p className="lede">
            Датчики передают показания через радиосеть MeshCore. Страница обновляется по мере добавления новых
            узлов в реестр проекта.
          </p>
        </section>
        <section aria-labelledby="sensor-list">
          <h2 id="sensor-list">Датчики ({sensors.length})</h2>
          {sensors.length === 0 ? (
            <p>Пока ни один датчик не опубликован. Мы добавим их в этот список по мере запуска узлов.</p>
          ) : (
            <ul className="sensor-list">
              {sensors.map((sensor) => (
                <li key={sensor.slug} className="sensor-card">
                  <h3>
                    <a href={`/sensors/${sensor.slug}`}>{sensor.displayName}</a>
                  </h3>
                  {sensor.description && <p>{sensor.description}</p>}
                  {sensor.metrics.length > 0 && (
                    <p className="eyebrow">
                      {sensor.metrics.map((metric) => metricLabels[metric] ?? metric).join(" · ")}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      </main>
    </div>
  );
}
