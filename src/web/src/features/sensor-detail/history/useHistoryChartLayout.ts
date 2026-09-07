import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type DragEvent,
  type KeyboardEvent,
  type PointerEvent,
} from "react";
import {
  emptyLayout,
  minimumCardHeight,
  minimumCardWidth,
  readLayout,
  toCardStyle,
  writeLayout,
  type ChartLayout,
} from "./chart-layout-storage";

export function useHistoryChartLayout<Item extends { id: string }>(slug: string, items: Item[]) {
  const [layout, setLayout] = useState<ChartLayout>(emptyLayout);
  const [readySlug, setReadySlug] = useState<string | null>(null);
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
    setReadySlug(slug);
  }, [slug]);

  useEffect(() => {
    if (readySlug !== slug) return;
    const timeout = window.setTimeout(() => writeLayout(slug, layout), 150);
    return () => window.clearTimeout(timeout);
  }, [layout, readySlug, slug]);

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
      .sort((left, right) => {
        const leftPosition = positions.get(left.item.id) ?? Number.POSITIVE_INFINITY;
        const rightPosition = positions.get(right.item.id) ?? Number.POSITIVE_INFINITY;
        return leftPosition - rightPosition || left.index - right.index;
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
    clearDragState();
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

    const targetId = chartCardAt(event.clientX, event.clientY);
    drag.targetId = targetId !== drag.id ? targetId : null;
    setDragOverId(drag.targetId);
    event.preventDefault();
  };

  const handleDragEnd = (event: PointerEvent<HTMLButtonElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    const moved = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= 8;
    const targetId = drag.targetId ?? chartCardAt(event.clientX, event.clientY);
    if ((drag.active || moved) && targetId && targetId !== drag.id) {
      changePosition(drag.id, targetId);
    }
    dragRef.current = null;
    releasePointer(event);
    clearDocumentInteractionStyles();
    clearDragState();
  };

  const resetSize = (id: string) => {
    setLayout((current) => {
      const sizes = { ...current.sizes };
      delete sizes[id];
      return { ...current, sizes };
    });
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
    updateSize(
      resize.id,
      resize.startWidth + event.clientX - resize.startX,
      resize.startHeight + event.clientY - resize.startY,
      resize.containerWidth,
    );
    event.preventDefault();
  };

  const handleResizeEnd = (event: PointerEvent<HTMLButtonElement>) => {
    if (!resizeRef.current) return;
    resizeRef.current = null;
    releasePointer(event);
    clearDocumentInteractionStyles();
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
    const widthDelta = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
    const heightDelta = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
    updateSize(
      id,
      cardRect.width + widthDelta,
      cardRect.height + heightDelta,
      container.getBoundingClientRect().width,
    );
    event.preventDefault();
  };

  const updateSize = (id: string, width: number, height: number, containerWidth: number) => {
    const constrainedWidth = Math.min(
      containerWidth,
      Math.max(Math.min(minimumCardWidth, containerWidth), width),
    );
    setLayout((current) => ({
      ...current,
      sizes: {
        ...current.sizes,
        [id]: {
          width: constrainedWidth / containerWidth,
          height: Math.max(minimumCardHeight, height),
        },
      },
    }));
  };

  const clearDragState = () => {
    setDraggedId(null);
    setDragOverId(null);
  };

  return {
    orderedItems,
    draggedId,
    dragOverId,
    resizingId,
    cardStyle: (id: string) => toCardStyle(layout.sizes[id]),
    moveByKeyboard,
    handlePointerDragStart,
    handleNativeDragStart,
    handleNativeDrop,
    handleDragMove,
    handleDragEnd,
    handleResizeStart,
    handleResizeMove,
    handleResizeEnd,
    handleResizeKeyDown,
    resetSize,
    clearDragState,
    setDragOverId,
  };
}

function chartCardAt(x: number, y: number): string | null {
  const card = document.elementFromPoint(x, y)?.closest<HTMLElement>("[data-history-chart-card]");
  return card?.dataset["historyChartCard"] || null;
}

function releasePointer(event: PointerEvent<HTMLButtonElement>): void {
  if (event.currentTarget.hasPointerCapture(event.pointerId)) {
    event.currentTarget.releasePointerCapture(event.pointerId);
  }
}

function clearDocumentInteractionStyles(): void {
  document.body.style.removeProperty("cursor");
  document.body.style.removeProperty("user-select");
}
