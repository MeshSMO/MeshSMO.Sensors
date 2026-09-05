import type { MetaFunction } from "react-router";
import { SiteHeader } from "../components/site";

export const meta: MetaFunction = () => [
  { title: "О проекте MeshSMO Sensors" },
  {
    name: "description",
    content:
      "MeshSMO Sensors — открытый сервис сбора и публикации показаний датчиков, доступных через радиосеть MeshCore (LoRa).",
  },
];

export default function About() {
  return (
    <div className="page-shell">
      <SiteHeader />
      <main>
        <section className="hero">
          <p className="eyebrow">О проекте</p>
          <h1>MeshSMO Sensors</h1>
          <p className="lede">
            Открытый сервис сбора и публикации показаний физических датчиков, доступных через радиосеть
            MeshCore в Смоленской области.
          </p>
        </section>
        <section className="status-panel" aria-labelledby="how-it-works">
          <div>
            <p className="eyebrow">Как это работает</p>
            <h2 id="how-it-works">От эфира до страницы</h2>
          </div>
          <p>
            Датчики передают измерения по LoRa-радиоканалу в сеть MeshCore. Шлюз проекта собирает телеметрию
            с повторителя, временно сохраняет её локально и публикует в основное хранилище, откуда показания
            становятся доступны на этом сайте.
          </p>
          <p>
            <a className="primary-link" href="/sensors">
              Смотреть датчики
            </a>
          </p>
        </section>
      </main>
    </div>
  );
}
