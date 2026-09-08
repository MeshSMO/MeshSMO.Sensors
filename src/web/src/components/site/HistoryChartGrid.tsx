import { GripVertical, MoveDiagonal2 } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useHistoryChartLayout } from "@/features/sensor-detail/history/useHistoryChartLayout";

export type HistoryChartGridItem = {
  id: string;
  title: string;
  unit: string | null;
  content: ReactNode;
};

export default function HistoryChartGrid({
  slug,
  items,
}: {
  slug: string;
  items: HistoryChartGridItem[];
}) {
  const { t } = useTranslation();
  const layout = useHistoryChartLayout(slug, items);

  return (
    <div className="history-chart-grid mt-4">
      {layout.orderedItems.map((item) => (
        <article
          key={item.id}
          data-history-chart-card={item.id}
          className={`panel history-chart-card relative flex min-h-90 flex-col px-4 py-4 ${
            layout.draggedId === item.id ? "opacity-50" : ""
          } ${layout.dragOverId === item.id ? "ring-2 ring-accent/70" : ""}`}
          style={layout.cardStyle(item.id)}
          onDragOver={(event) => {
            if (layout.draggedId && layout.draggedId !== item.id) {
              event.preventDefault();
              event.dataTransfer.dropEffect = "move";
              layout.setDragOverId(item.id);
            }
          }}
          onDragLeave={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
              layout.setDragOverId((current) => (current === item.id ? null : current));
            }
          }}
          onDrop={(event) => layout.handleNativeDrop(event, item.id)}
        >
          <header className="mb-2 flex shrink-0 items-center gap-2">
            <button
              type="button"
              draggable
              className="-ml-1 flex min-w-0 flex-1 touch-none cursor-grab items-center gap-1 rounded-sm text-left active:cursor-grabbing"
              aria-label={t("common.accessibility.moveCard", { title: item.title })}
              onDragStart={(event) => layout.handleNativeDragStart(event, item.id)}
              onDragEnd={layout.clearDragState}
              onPointerDown={(event) => layout.handlePointerDragStart(event, item.id)}
              onPointerMove={layout.handleDragMove}
              onPointerUp={layout.handleDragEnd}
              onPointerCancel={layout.handleDragEnd}
              onLostPointerCapture={layout.handleDragEnd}
              onKeyDown={(event) => {
                if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
                  event.preventDefault();
                  layout.moveByKeyboard(item.id, -1);
                }
                if (event.key === "ArrowRight" || event.key === "ArrowDown") {
                  event.preventDefault();
                  layout.moveByKeyboard(item.id, 1);
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
              layout.resizingId === item.id ? "bg-surface-raised text-accent" : ""
            }`}
            aria-label={t("common.accessibility.resizeCard", { title: item.title })}
            title={t("common.accessibility.resizeCardHelp")}
            onPointerDown={(event) => layout.handleResizeStart(event, item.id)}
            onPointerMove={layout.handleResizeMove}
            onPointerUp={layout.handleResizeEnd}
            onPointerCancel={layout.handleResizeEnd}
            onLostPointerCapture={layout.handleResizeEnd}
            onKeyDown={(event) => layout.handleResizeKeyDown(event, item.id)}
            onDoubleClick={() => layout.resetSize(item.id)}
          >
            <MoveDiagonal2 className="h-4 w-4" aria-hidden />
          </button>
        </article>
      ))}
    </div>
  );
}
