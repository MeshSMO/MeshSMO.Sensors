import { ChevronDown, Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Checkbox } from "@/components/ui/checkbox";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { densityPercent, forecastHorizons, ranges, type RangeKey } from "@/lib/api";
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
  density,
  onToggleMetric,
  onUpdate,
}: {
  metrics: string[];
  selected: string[];
  range: RangeKey;
  mode: ChartMode;
  forecast: SensorSearch["forecast"];
  forecastEnabled: boolean;
  density: SensorSearch["density"];
  onToggleMetric: (metric: string) => void;
  onUpdate: (patch: SearchPatch) => void;
}) {
  const { t } = useTranslation();
  const selectedLabel =
    selected.length === 1 && selected[0]
      ? metricLabel(selected[0])
      : t("history.selectedMetrics", { count: selected.length });

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
            ["separate", t("history.separate")],
            ["combined", t("history.combined")],
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
              ? t("history.forecastSelectMetric")
              : t("history.forecastDescription")
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
          <span className="relative">{t("history.forecast")}</span>
        </button>

        {forecastEnabled ? (
          <SegmentedControl
            value={forecast ?? "24h"}
            options={forecastHorizons.map((horizon) => [horizon, t(`history.horizons.${horizon}`)])}
            onChange={(nextForecast) => onUpdate({ forecast: nextForecast })}
            numeric
          />
        ) : null}
      </div>

      <div
        className="mt-3 flex flex-wrap gap-2"
        aria-label={t("common.accessibility.historyPeriod")}
      >
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
            {t(`history.ranges.${key}`)}
          </button>
        ))}

        <DensityField density={density} onUpdate={onUpdate} />
      </div>
    </>
  );
}

function DensityField({
  density,
  onUpdate,
}: {
  density: SensorSearch["density"];
  onUpdate: (patch: SearchPatch) => void;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState(density === undefined ? "" : String(density));

  useEffect(() => {
    setText(density === undefined ? "" : String(density));
  }, [density]);

  const commit = (raw: string) => {
    const trimmed = raw.trim();
    if (!trimmed) {
      onUpdate({ density: undefined });
      return;
    }

    const parsed = Number.parseInt(trimmed, 10);
    if (Number.isInteger(parsed)) {
      onUpdate({
        density: Math.min(densityPercent.max, Math.max(densityPercent.min, parsed)),
      });
    }
  };

  return (
    <label
      className="ml-auto flex items-center gap-2 text-xs text-muted-foreground"
      title={t("history.densityHint")}
    >
      <span>{t("history.density")}</span>
      <span className="flex items-center rounded-md border border-border focus-within:border-accent">
        <input
          type="number"
          min={densityPercent.min}
          max={densityPercent.max}
          step={1}
          inputMode="numeric"
          value={text}
          placeholder={t("history.densityAuto")}
          aria-label={t("history.density")}
          onChange={(event) => setText(event.target.value)}
          onBlur={(event) => commit(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.currentTarget.blur();
            }
          }}
          className="num w-14 bg-transparent px-2 py-1.5 text-xs text-foreground outline-none"
        />
        <span className="pr-2">%</span>
      </span>
      <button
        type="button"
        disabled={density === undefined}
        onClick={() => onUpdate({ density: undefined })}
        className="rounded-md border border-border px-2 py-1.5 transition-colors hover:text-foreground disabled:cursor-default disabled:opacity-40"
      >
        {t("history.densityAuto")}
      </button>
    </label>
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
