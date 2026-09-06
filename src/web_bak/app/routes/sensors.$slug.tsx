import type { Route } from "./+types/sensors.$slug";
import { SiteHeader } from "../components/site";
import { findRegistrySensor } from "../lib/registry";
import { publicBaseUrl } from "../lib/baseUrl";

export const meta: Route.MetaFunction = ({ params }) => {
  const sensor = findRegistrySensor(params.slug ?? "");
  const name = sensor?.displayName ?? params.slug;
  const description =
    sensor?.description ??
    `Показания публичного датчика ${name} сети MeshSMO, передаваемые через радиосеть MeshCore.`;
  const url = `${publicBaseUrl()}/sensors/${params.slug}`;
  return [
    { title: `${name} — показания | MeshSMO` },
    { name: "description", content: description },
    { rel: "canonical", href: url },
    { property: "og:title", content: `${name} — показания | MeshSMO` },
    { property: "og:description", content: description },
    { property: "og:url", content: url },
    { property: "og:type", content: "website" },
  ];
};

const metricLabels: Record<string, string> = {
  temperature: "Температура",
  humidity: "Влажность",
  pressure: "Атмосферное давление",
  battery: "Заряд батареи",
};

export default function SensorDetail({ params }: Route.ComponentProps) {
  const sensor = findRegistrySensor(params.slug ?? "") ?? null;

  if (!sensor) {
    return (
      <div className="page-shell">
        <SiteHeader />
        <main className="error-page">
          <h1>Датчик не найден</h1>
          <p>
            Такого публичного датчика нет в реестре MeshSMO. Посмотрите{" "}
            <a href="/sensors">список доступных датчиков</a>.
          </p>
        </main>
      </div>
    );
  }

  return (
    <div className="page-shell">
      <SiteHeader />
      <main>
        <article>
          <h1>{sensor.displayName}</h1>
          {sensor.description && <p className="lede">{sensor.description}</p>}
          {sensor.location && (
            <p>
              Расположение: приблизительно {sensor.location.latitude.toFixed(4)},{" "}
              {sensor.location.longitude.toFixed(4)} ({sensor.location.precision}).
            </p>
          )}
          <section aria-labelledby="sensor-metrics">
            <h2 id="sensor-metrics">Измеряемые показатели</h2>
            {sensor.metrics.length > 0 ? (
              <ul>
                {sensor.metrics.map((metric) => (
                  <li key={metric}>{metricLabels[metric] ?? metric}</li>
                ))}
              </ul>
            ) : (
              <p>Состав показателей будет опубликован после запуска датчика.</p>
            )}
          </section>
          <section aria-labelledby="sensor-live">
            <h2 id="sensor-live">Текущие показания</h2>
            <p>
              Свежие измерения появятся здесь после подключения датчика к системе сбора. Данные передаются через
              сеть MeshCore и публикуются сервисом MeshSMO Sensors.
            </p>
          </section>
          <p>
            <a href="/sensors">← Все датчики MeshSMO</a>
          </p>
        </article>
      </main>
    </div>
  );
}
