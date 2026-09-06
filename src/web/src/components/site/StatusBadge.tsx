import { stateLabels, type SensorState } from "@/lib/format";

const dotColor: Record<SensorState, string> = {
  Online: "bg-online",
  Degraded: "bg-degraded",
  Offline: "bg-offline",
  Unknown: "bg-unknown",
};

const shape: Record<SensorState, string> = {
  Online: "rounded-full",
  Degraded: "rounded-none rotate-45",
  Offline: "rounded-[2px]",
  Unknown: "rounded-full opacity-60",
};

export function StatusBadge({
  state,
  loading = false,
}: {
  state: SensorState | null;
  loading?: boolean;
}) {
  if (loading || state === null) {
    return (
      <span className="inline-flex items-center gap-2 rounded-full border border-border bg-surface-raised px-3 py-1 text-xs text-muted-foreground">
        <span className="h-2 w-2 rounded-full bg-unknown" aria-hidden />
        Нет связи с API
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-2 rounded-full border border-border bg-surface-raised px-3 py-1 text-xs font-medium">
      <span className={`h-2 w-2 ${dotColor[state]} ${shape[state]}`} aria-hidden />
      {stateLabels[state]}
    </span>
  );
}
