export function meta() {
  return [
    { title: "Датчики MeshSMO" },
    {
      name: "description",
      content: "Публичные показания датчиков сети MeshSMO в Смоленской области.",
    },
  ];
}

export default function Home() {
  return (
    <div className="page-shell">
      <header className="site-header">
        <a className="brand" href="/" aria-label="MeshSMO Sensors — главная">
          <span className="brand-mark" aria-hidden="true">M</span>
          <span>MeshSMO Sensors</span>
        </a>
        <nav aria-label="Основная навигация">
          <a href="/sensors">Датчики</a>
        </nav>
      </header>

      <main>
        <section className="hero">
          <p className="eyebrow">LoRa · MeshCore · Смоленская область</p>
          <h1>Показания датчиков MeshSMO</h1>
          <p className="lede">
            Открытая телеметрия датчиков, доступных через радиосеть MeshCore.
            Сейчас мы готовим первый узел и надёжный путь данных от эфира до сайта.
          </p>
          <a className="primary-link" href="/sensors">Смотреть датчики</a>
        </section>

        <section className="status-panel" aria-labelledby="project-status">
          <div>
            <p className="eyebrow">Статус проекта</p>
            <h2 id="project-status">Система разворачивается</h2>
          </div>
          <p>
            Реестр датчиков и хранилище уже заложены в архитектуру. Публичные
            измерения появятся после подключения первого MeshCoreTel Repeater.
          </p>
        </section>
      </main>

      <footer>
        <span>MeshSMO</span>
        <span>Данные поступают по LoRa-сети</span>
      </footer>
    </div>
  );
}
