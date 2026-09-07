import { format } from "date-fns";
import { ru } from "date-fns/locale";
import { CalendarDays, Clock } from "lucide-react";
import { useEffect, useState } from "react";
import type { DateRange } from "react-day-picker";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Input } from "@/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";

export function CustomRangeForm({
  from,
  to,
  onApply,
}: {
  from?: string | undefined;
  to?: string | undefined;
  onApply: (from: string, to: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [dates, setDates] = useState<DateRange | undefined>(() => ({
    from: dateFromIso(from),
    to: dateFromIso(to),
  }));
  const [startTime, setStartTime] = useState(() => timeFromIso(from, "00:00"));
  const [endTime, setEndTime] = useState(() => timeFromIso(to, "23:59"));

  useEffect(() => {
    setDates({ from: dateFromIso(from), to: dateFromIso(to) });
    setStartTime(timeFromIso(from, "00:00"));
    setEndTime(timeFromIso(to, "23:59"));
  }, [from, to]);

  const start = dateWithTime(dates?.from, startTime);
  const end = dateWithTime(dates?.to, endTime);
  const invalid = Boolean(start && end && start >= end);
  const complete = Boolean(start && end);

  return (
    <form
      className="mt-3 flex flex-wrap items-end gap-3 rounded-md border border-border bg-surface px-3 py-3"
      onSubmit={(event) => {
        event.preventDefault();
        if (!start || !end || invalid) return;
        onApply(start.toISOString(), end.toISOString());
      }}
    >
      <div className="min-w-0 flex-1 basis-full sm:basis-72">
        <p className="mb-1 text-xs text-muted-foreground">Даты</p>
        <Popover open={open} onOpenChange={setOpen}>
          <PopoverTrigger asChild>
            <Button
              type="button"
              variant="outline"
              className="w-full justify-start bg-surface-raised px-3 text-left font-normal"
            >
              <CalendarDays aria-hidden />
              {dates?.from ? (
                dates.to ? (
                  <span className="truncate">
                    {format(dates.from, "d MMM yyyy", { locale: ru })} —{" "}
                    {format(dates.to, "d MMM yyyy", { locale: ru })}
                  </span>
                ) : (
                  format(dates.from, "d MMMM yyyy", { locale: ru })
                )
              ) : (
                <span className="text-muted-foreground">Выберите начало и конец</span>
              )}
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-auto p-0" align="start">
            <Calendar
              mode="range"
              selected={dates}
              onSelect={setDates}
              locale={ru}
              {...(dates?.from ? { defaultMonth: dates.from } : {})}
              className="pointer-events-auto p-3"
            />
          </PopoverContent>
        </Popover>
      </div>

      <TimeInput label="Время с" value={startTime} onChange={setStartTime} />
      <TimeInput label="Время по" value={endTime} onChange={setEndTime} />
      <Button type="submit" size="sm" disabled={!complete || invalid}>
        Показать
      </Button>
      {invalid ? (
        <p className="basis-full text-xs text-offline">Начало должно быть раньше конца.</p>
      ) : null}
    </form>
  );
}

function TimeInput({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex min-w-28 flex-col gap-1 text-xs text-muted-foreground">
      {label}
      <span className="relative">
        <Clock
          className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2"
          aria-hidden
        />
        <Input
          inputMode="numeric"
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={label === "Время с" ? "00:00" : "23:59"}
          aria-label={label}
          className="num bg-surface-raised pl-9"
        />
      </span>
    </label>
  );
}

function dateFromIso(iso: string | undefined): Date | undefined {
  if (!iso) return undefined;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function timeFromIso(iso: string | undefined, fallback: string): string {
  const date = dateFromIso(iso);
  if (!date) return fallback;
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function dateWithTime(date: Date | undefined, value: string): Date | undefined {
  if (!date || !/^([01]\d|2[0-3]):[0-5]\d$/.test(value)) return undefined;
  const [hours, minutes] = value.split(":").map(Number);
  const next = new Date(date);
  next.setHours(hours ?? 0, minutes ?? 0, 0, 0);
  return next;
}
