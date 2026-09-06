import { createFileRoute } from "@tanstack/react-router";
import { PageShell } from "@/components/site/Shell";

const title = "О проекте MeshSMO Sensors — путь данных от эфира до страницы | MeshSMO";
const description =
  "Как устроен MeshSMO Sensors: LoRa-датчики, сеть MeshCore, опрос ретрансляторов, хранение и публикация показаний.";

export const Route = createFileRoute("/about")({
  head: () => ({
    meta: [
      { title },
      { name: "description", content: description },
      { property: "og:title", content: title },
      { property: "og:description", content: description },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
  }),
  component: AboutPage,
});

const steps = [
  {
    title: "1. Датчик в поле",
    text: "Автономный модуль с сенсорами температуры, влажности и давления. Питание от аккумулятора, напряжение публикуется как отдельный показатель.",
  },
  {
    title: "2. Радиоканал MeshCore",
    text: "Показания уходят в эфир по LoRa. Сеть ячеистая: пакет доходит до шлюза через ретрансляторы, качество канала фиксируется как RSSI и SNR.",
  },
  {
    title: "3. Опрос и хранение",
    text: "Сервис периодически опрашивает узлы (по умолчанию раз в 5 минут), сохраняет сырые значения и агрегаты, отмечает неудачные попытки подряд.",
  },
  {
    title: "4. Публикация",
    text: "Сайт читает данные из API того же домена. Если сервер молчит — страница остаётся читаемой, а вместо чисел показывается честный прочерк.",
  },
];

function AboutPage() {
  return (
    <PageShell>
      <p className="eyebrow">О проекте</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">Как данные попадают на сайт</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">
        MeshSMO — некоммерческая сеть радиотелеметрии в Смоленской области. Мы показываем только то,
        что действительно измерено, и не подставляем прогнозные или усреднённые значения вместо
        пропусков.
      </p>

      <ol className="mt-8 grid gap-4 sm:grid-cols-2">
        {steps.map((step) => (
          <li key={step.title} className="panel px-5 py-5">
            <h2 className="text-base font-semibold">{step.title}</h2>
            <p className="mt-2 text-sm text-muted-foreground">{step.text}</p>
          </li>
        ))}
      </ol>

      <section className="mt-10 panel px-5 py-5">
        <h2 className="text-base font-semibold">Точность и ограничения</h2>
        <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
          <li>Координаты датчиков публикуются приблизительно.</li>
          <li>Пропуски в истории означают отсутствие связи, а не нулевые значения.</li>
          <li>Пороговые значения и оценки «норма / не норма» на сайте не выставляются.</li>
        </ul>
      </section>
    </PageShell>
  );
}
