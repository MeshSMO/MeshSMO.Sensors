import { ChevronDown, Sparkles } from "lucide-react";
import { Checkbox } from "@/components/ui/checkbox";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import {
  forecastHorizonLabels,
  forecastHorizons,
  rangeLabels,
  ranges,
  type RangeKey,
} from "@/lib/api";
import { metricLabel } from "@/lib/metrics";
import type { ChartMode, SensorSearch } from "../route-config";
import type { SearchPatch } from "./history-preferences";

export function HistoryControls({
  metrics,
  selected,
  range,
  mode,
  forecast,
  forecastEnabled,
  onToggleMetric,
  onUpdate,
}: {
  metrics: string[];
  selected: string[];
  range: RangeKey;
  mode: ChartMode;
  forecast: SensorSearch["forecast"];
  forecastEnabled: boolean;
  onToggleMetric: (metric: string) => void;
  onUpdate: (patch: SearchPatch) => void;
}) {
  const selectedLabel =
    selected.length === 1 && selected[0]
      ? metricLabel(selected[0])
      : `Выбрано показателей: ${selected.length}`;

  return (
    <>
      <div className="mt-4 flex flex-wrap items-center gap-2">
        <Popover>
          <PopoverTrigger className="flex items-center gap-2 rounded-md border border-border bg-surface-raised px-3 py-1.5 text-xs text-foreground hover:border-accent">
            {selectedLabel}
            <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" aria-hidden />
          </PopoverTrigger>
          <PopoverContent align="start" className="w-64 p-2">
            <ul className="max-h-72 space-y-1 overflow-y-auto">
              {metrics.map((metric) => (
                <li key={metric}>
                  <label className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-surface-raised">
                    <Checkbox
                      checked={selected.includes(metric)}
                      onCheckedChange={() => onToggleMetric(metric)}
                    />
                    <span>{metricLabel(metric)}</span>
                  </label>
                </li>
              ))}
            </ul>
          </PopoverContent>
        </Popover>

        <SegmentedControl
          value={mode}
          options={[
            ["separate", "Отдельно"],
            ["combined", "Вместе"],
          ]}
          onChange={(nextMode) =>
            onUpdate({
              mode: nextMode === "separate" ? undefined : nextMode,
              forecast: nextMode === "combined" ? undefined : forecast,
            })
          }
        />

        <button
          type="button"
          aria-pressed={forecastEnabled}
          disabled={mode === "combined" || selected.length !== 1}
          onClick={() => onUpdate({ forecast: forecastEnabled ? undefined : "24h" })}
          className={`group relative inline-flex items-center gap-2 overflow-hidden rounded-md border px-3 py-1.5 text-xs font-medium transition-all disabled:cursor-not-allowed disabled:opacity-40 ${
            forecastEnabled
              ? "border-accent/70 bg-accent/15 text-foreground shadow-[0_0_18px_-6px_var(--accent)]"
              : "border-border text-muted-foreground hover:border-accent/60 hover:text-foreground"
          }`}
          title={
            selected.length !== 1
              ? "Для прогноза выберите один показатель"
              : "Прогноз строится ML-моделями по истории измерений"
          }
        >
          <span
            aria-hidden
            className={`pointer-events-none absolute inset-0 bg-[linear-gradient(110deg,transparent,color-mix(in_oklab,var(--accent)_28%,transparent),transparent)] transition-opacity motion-safe:animate-pulse ${
              forecastEnabled ? "opacity-100" : "opacity-0"
            }`}
          />
          <Sparkles
            className={`relative h-3.5 w-3.5 ${forecastEnabled ? "text-accent" : ""}`}
            aria-hidden
          />
          <span className="relative">Прогноз</span>
        </button>

        {forecastEnabled ? (
          <SegmentedControl
            value={forecast ?? "24h"}
            options={forecastHorizons.map((horizon) => [horizon, forecastHorizonLabels[horizon]])}
            onChange={(nextForecast) => onUpdate({ forecast: nextForecast })}
            numeric
          />
        ) : null}
      </div>

      <div className="mt-3 flex flex-wrap gap-2" aria-label="Период истории">
        {ranges.map((key) => (
          <button
            key={key}
            type="button"
            aria-pressed={key === range}
            onClick={() =>
              onUpdate({
                range: key,
                ...(key === "custom" ? {} : { from: undefined, to: undefined }),
              })
            }
            className={`num rounded-md border px-3 py-1.5 text-xs transition-colors ${
              key === range
                ? "border-accent bg-surface-raised text-foreground"
                : "border-border text-muted-foreground hover:text-foreground"
            }`}
          >
            {rangeLabels[key]}
          </button>
        ))}
      </div>
    </>
  );
}

function SegmentedControl<Value extends string>({
  value,
  options,
  onChange,
  numeric = false,
}: {
  value: Value;
  options: ReadonlyArray<readonly [Value, string]>;
  onChange: (value: Value) => void;
  numeric?: boolean;
}) {
  return (
    <div className="flex overflow-hidden rounded-md border border-border">
      {options.map(([key, label]) => (
        <button
          key={key}
          type="button"
          aria-pressed={value === key}
          onClick={() => onChange(key)}
          className={`${numeric ? "num " : ""}px-3 py-1.5 text-xs transition-colors ${
            value === key
              ? "bg-surface-raised text-foreground"
              : "text-muted-foreground hover:text-foreground"
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
