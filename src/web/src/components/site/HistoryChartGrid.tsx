import { GripVertical, MoveDiagonal2 } from "lucide-react";
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type DragEvent,
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
} from "react";

const LAYOUT_KEY = "meshsmo:sensor-chart-layout";
const MIN_CARD_WIDTH = 320;
const MIN_CARD_HEIGHT = 360;

type CardSize = {
  width: number;
  height: number;
};

type ChartLayout = {
  order: string[];
  sizes: Record<string, CardSize>;
};

export type HistoryChartGridItem = {
  id: string;
  title: string;
  unit: string | null;
  content: ReactNode;
};

const emptyLayout = (): ChartLayout => ({ order: [], sizes: {} });

function readLayout(slug: string): ChartLayout {
  try {
    const raw = window.localStorage.getItem(LAYOUT_KEY);
    if (!raw) return emptyLayout();

    const stored = JSON.parse(raw) as Record<string, Partial<ChartLayout>>;
    const layout = stored[slug];
    if (!layout) return emptyLayout();

    const order = Array.isArray(layout.order)
      ? layout.order.filter((id): id is string => typeof id === "string")
      : [];
    const sizes: Record<string, CardSize> = {};
    for (const [id, size] of Object.entries(layout.sizes ?? {})) {
      if (
        typeof size?.width === "number" &&
        Number.isFinite(size.width) &&
        typeof size.height === "number" &&
        Number.isFinite(size.height)
      ) {
        sizes[id] = {
          width: Math.min(1, Math.max(0.1, size.width)),
          height: Math.max(MIN_CARD_HEIGHT, size.height),
        };
      }
    }

    return { order, sizes };
  } catch {
    return emptyLayout();
  }
}

function writeLayout(slug: string, layout: ChartLayout): void {
  try {
    const raw = window.localStorage.getItem(LAYOUT_KEY);
    const stored = (raw ? JSON.parse(raw) : {}) as Record<string, ChartLayout>;
    stored[slug] = layout;
    window.localStorage.setItem(LAYOUT_KEY, JSON.stringify(stored));
  } catch {
    // localStorage может быть недоступен — раскладка останется рабочей до перезагрузки.
  }
}

function cardStyle(size: CardSize | undefined): CSSProperties {
  if (!size) return {};
  const percentage = Math.round(size.width * 10_000) / 100;
  return {
    "--history-card-width": `${percentage}%`,
    height: `${Math.round(size.height)}px`,
  } as CSSProperties;
}

export default function HistoryChartGrid({
  slug,
  items,
}: {
  slug: string;
  items: HistoryChartGridItem[];
}) {
  const [layout, setLayout] = useState<ChartLayout>(emptyLayout);
  const [layoutReady, setLayoutReady] = useState(false);
  const [draggedId, setDraggedId] = useState<string | null>(null);
  const [dragOverId, setDragOverId] = useState<string | null>(null);
  const [resizingId, setResizingId] = useState<string | null>(null);
  const resizeRef = useRef<{
    id: string;
    startX: number;
    startY: number;
    startWidth: number;
    startHeight: number;
    containerWidth: number;
  } | null>(null);
  const dragRef = useRef<{
    id: string;
    startX: number;
    startY: number;
    active: boolean;
    targetId: string | null;
  } | null>(null);

  useEffect(() => {
    setLayout(readLayout(slug));
    setLayoutReady(true);
  }, [slug]);

  useEffect(() => {
    if (!layoutReady) return;
    const timeout = window.setTimeout(() => writeLayout(slug, layout), 150);
    return () => window.clearTimeout(timeout);
  }, [layout, layoutReady, slug]);

  useEffect(
    () => () => {
      document.body.style.removeProperty("cursor");
      document.body.style.removeProperty("user-select");
    },
    [],
  );

  const orderedItems = useMemo(() => {
    const positions = new Map(layout.order.map((id, index) => [id, index]));
    return items
      .map((item, index) => ({ item, index }))
      .sort((a, b) => {
        const aPosition = positions.get(a.item.id) ?? Number.POSITIVE_INFINITY;
        const bPosition = positions.get(b.item.id) ?? Number.POSITIVE_INFINITY;
        return aPosition - bPosition || a.index - b.index;
      })
      .map(({ item }) => item);
  }, [items, layout.order]);

  const changePosition = (sourceId: string, targetId: string) => {
    if (sourceId === targetId) return;
    const visibleOrder = orderedItems.map((item) => item.id);
    const sourceIndex = visibleOrder.indexOf(sourceId);
    const targetIndex = visibleOrder.indexOf(targetId);
    if (sourceIndex < 0 || targetIndex < 0) return;

    [visibleOrder[sourceIndex], visibleOrder[targetIndex]] = [
      visibleOrder[targetIndex] as string,
      visibleOrder[sourceIndex] as string,
    ];
    setLayout((current) => ({
      ...current,
      order: [...visibleOrder, ...current.order.filter((id) => !visibleOrder.includes(id))],
    }));
  };

  const moveByKeyboard = (id: string, delta: number) => {
    const visibleOrder = orderedItems.map((item) => item.id);
    const index = visibleOrder.indexOf(id);
    const target = Math.min(visibleOrder.length - 1, Math.max(0, index + delta));
    if (index >= 0 && target !== index) changePosition(id, visibleOrder[target] as string);
  };

  const handlePointerDragStart = (event: PointerEvent<HTMLButtonElement>, id: string) => {
    if (event.pointerType === "mouse" || event.button !== 0) return;
    dragRef.current = {
      id,
      startX: event.clientX,
      startY: event.clientY,
      active: false,
      targetId: null,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const handleNativeDragStart = (event: DragEvent<HTMLButtonElement>, id: string) => {
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", id);
    setDraggedId(id);
  };

  const handleNativeDrop = (event: DragEvent<HTMLElement>, targetId: string) => {
    event.preventDefault();
    const sourceId = draggedId ?? event.dataTransfer.getData("text/plain");
    if (sourceId) changePosition(sourceId, targetId);
    setDraggedId(null);
    setDragOverId(null);
  };

  const handleDragMove = (event: PointerEvent<HTMLButtonElement>) => {
    const drag = dragRef.current;
    if (!drag) return;

    if (!drag.active && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= 8) {
      drag.active = true;
      document.body.style.cursor = "grabbing";
      document.body.style.userSelect = "none";
      setDraggedId(drag.id);
    }
    if (!drag.active) return;

    const card = document
      .elementFromPoint(event.clientX, event.clientY)
      ?.closest<HTMLElement>("[data-history-chart-card]");
    const targetId = card?.dataset["historyChartCard"] || null;
    drag.targetId = targetId !== drag.id ? targetId : null;
    setDragOverId(drag.targetId);
    event.preventDefault();
  };

  const handleDragEnd = (event: PointerEvent<HTMLButtonElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    const moved = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= 8;
    const card = document
      .elementFromPoint(event.clientX, event.clientY)
      ?.closest<HTMLElement>("[data-history-chart-card]");
    const targetAtPointer = card?.dataset["historyChartCard"] || null;
    const targetId = drag.targetId ?? targetAtPointer;
    if ((drag.active || moved) && targetId && targetId !== drag.id) {
      changePosition(drag.id, targetId);
    }
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    document.body.style.removeProperty("cursor");
    document.body.style.removeProperty("user-select");
    setDraggedId(null);
    setDragOverId(null);
  };

  const handleResizeStart = (event: PointerEvent<HTMLButtonElement>, id: string) => {
    if (event.detail > 1) {
      resetSize(id);
      event.preventDefault();
      return;
    }
    if (event.button !== 0) return;
    const card = event.currentTarget.closest<HTMLElement>("[data-history-chart-card]");
    const container = card?.parentElement;
    if (!card || !container) return;

    const cardRect = card.getBoundingClientRect();
    resizeRef.current = {
      id,
      startX: event.clientX,
      startY: event.clientY,
      startWidth: cardRect.width,
      startHeight: cardRect.height,
      containerWidth: container.getBoundingClientRect().width,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
    document.body.style.cursor = "nwse-resize";
    document.body.style.userSelect = "none";
    setResizingId(id);
    event.preventDefault();
  };

  const handleResizeMove = (event: PointerEvent<HTMLButtonElement>) => {
    const resize = resizeRef.current;
    if (!resize) return;

    const minimumWidth = Math.min(MIN_CARD_WIDTH, resize.containerWidth);
    const width = Math.min(
      resize.containerWidth,
      Math.max(minimumWidth, resize.startWidth + event.clientX - resize.startX),
    );
    const height = Math.max(MIN_CARD_HEIGHT, resize.startHeight + event.clientY - resize.startY);
    setLayout((current) => ({
      ...current,
      sizes: {
        ...current.sizes,
        [resize.id]: { width: width / resize.containerWidth, height },
      },
    }));
    event.preventDefault();
  };

  const handleResizeEnd = (event: PointerEvent<HTMLButtonElement>) => {
    if (!resizeRef.current) return;
    resizeRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    document.body.style.removeProperty("cursor");
    document.body.style.removeProperty("user-select");
    setResizingId(null);
  };

  const handleResizeKeyDown = (event: KeyboardEvent<HTMLButtonElement>, id: string) => {
    if (event.key === "Home") {
      resetSize(id);
      event.preventDefault();
      return;
    }
    if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
    const card = event.currentTarget.closest<HTMLElement>("[data-history-chart-card]");
    const container = card?.parentElement;
    if (!card || !container) return;

    const step = event.shiftKey ? 64 : 24;
    const cardRect = card.getBoundingClientRect();
    const containerWidth = container.getBoundingClientRect().width;
    const minimumWidth = Math.min(MIN_CARD_WIDTH, containerWidth);
    const widthDelta = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
    const heightDelta = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
    const width = Math.min(containerWidth, Math.max(minimumWidth, cardRect.width + widthDelta));
    const height = Math.max(MIN_CARD_HEIGHT, cardRect.height + heightDelta);
    setLayout((current) => ({
      ...current,
      sizes: {
        ...current.sizes,
        [id]: { width: width / containerWidth, height },
      },
    }));
    event.preventDefault();
  };

  const resetSize = (id: string) => {
    setLayout((current) => {
      const sizes = { ...current.sizes };
      delete sizes[id];
      return { ...current, sizes };
    });
  };

  return (
    <div className="history-chart-grid mt-4">
      {orderedItems.map((item) => (
        <article
          key={item.id}
          data-history-chart-card={item.id}
          className={`panel history-chart-card relative flex min-h-90 flex-col px-4 py-4 ${
            draggedId === item.id ? "opacity-50" : ""
          } ${dragOverId === item.id ? "ring-2 ring-accent/70" : ""}`}
          style={cardStyle(layout.sizes[item.id])}
          onDragOver={(event) => {
            if (draggedId && draggedId !== item.id) {
              event.preventDefault();
              event.dataTransfer.dropEffect = "move";
              setDragOverId(item.id);
            }
          }}
          onDragLeave={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
              setDragOverId((current) => (current === item.id ? null : current));
            }
          }}
          onDrop={(event) => handleNativeDrop(event, item.id)}
        >
          <header className="mb-2 flex shrink-0 items-center gap-2">
            <button
              type="button"
              draggable
              className="-ml-1 flex min-w-0 flex-1 touch-none cursor-grab items-center gap-1 rounded-sm text-left active:cursor-grabbing"
              aria-label={`Переместить карточку «${item.title}». Для клавиатуры используйте стрелки.`}
              onDragStart={(event) => handleNativeDragStart(event, item.id)}
              onDragEnd={() => {
                setDraggedId(null);
                setDragOverId(null);
              }}
              onPointerDown={(event) => handlePointerDragStart(event, item.id)}
              onPointerMove={handleDragMove}
              onPointerUp={handleDragEnd}
              onPointerCancel={handleDragEnd}
              onLostPointerCapture={handleDragEnd}
              onKeyDown={(event) => {
                if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
                  event.preventDefault();
                  moveByKeyboard(item.id, -1);
                }
                if (event.key === "ArrowRight" || event.key === "ArrowDown") {
                  event.preventDefault();
                  moveByKeyboard(item.id, 1);
                }
              }}
            >
              <GripVertical className="h-4 w-4 shrink-0 text-muted-foreground/70" aria-hidden />
              <span className="truncate text-sm font-medium">{item.title}</span>
              {item.unit ? (
                <span className="shrink-0 text-xs text-muted-foreground">{item.unit}</span>
              ) : null}
            </button>
          </header>

          <div className="flex min-h-0 flex-1 flex-col">{item.content}</div>

          <button
            type="button"
            className={`absolute right-1 bottom-1 z-10 flex h-7 w-7 touch-none items-end justify-end rounded-sm p-1 text-muted-foreground transition-colors hover:bg-surface-raised hover:text-accent ${
              resizingId === item.id ? "bg-surface-raised text-accent" : ""
            }`}
            aria-label={`Изменить размер карточки «${item.title}». Для клавиатуры используйте стрелки.`}
            title="Потяните или используйте стрелки. Home или двойной щелчок — сбросить."
            onPointerDown={(event) => handleResizeStart(event, item.id)}
            onPointerMove={handleResizeMove}
            onPointerUp={handleResizeEnd}
            onPointerCancel={handleResizeEnd}
            onLostPointerCapture={handleResizeEnd}
            onKeyDown={(event) => handleResizeKeyDown(event, item.id)}
            onDoubleClick={() => resetSize(item.id)}
          >
            <MoveDiagonal2 className="h-4 w-4" aria-hidden />
          </button>
        </article>
      ))}
    </div>
  );
}
