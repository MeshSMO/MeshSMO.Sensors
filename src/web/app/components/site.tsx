export function SiteHeader() {
  return (
    <header className="site-header">
      <a className="brand" href="/" aria-label="MeshSMO Sensors — главная">
        <span className="brand-mark" aria-hidden="true">M</span>
        <span>MeshSMO Sensors</span>
      </a>
      <nav aria-label="Основная навигация">
        <a href="/sensors">Датчики</a>
        <a href="/about">О проекте</a>
      </nav>
    </header>
  );
}
