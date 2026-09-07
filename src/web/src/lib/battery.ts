/**
 * Оценка заряда li-ion (одна банка, номинал 3.7 В) по напряжению.
 * Таблица — приблизительная кривая разряда без нагрузки: при зарядке от
 * солнца напряжение завышено, поэтому всё выше 4.2 В считается 100%.
 */

const LI_ION_CURVE: ReadonlyArray<readonly [number, number]> = [
  [4.2, 100],
  [4.06, 90],
  [3.98, 80],
  [3.92, 70],
  [3.87, 60],
  [3.82, 50],
  [3.79, 40],
  [3.77, 30],
  [3.74, 20],
  [3.68, 10],
  [3.45, 5],
  [3.0, 0],
];

/** Вне этого окна напряжение не похоже на банку li-ion — считаем показание некорректным. */
const MIN_VALID_VOLTS = 2.8;
const MAX_VALID_VOLTS = 4.5;

/** Процент заряда 0..100 или null, если напряжение отсутствует/некорректно. */
export function liIonChargePercent(volts: number | null | undefined): number | null {
  if (volts === null || volts === undefined || !Number.isFinite(volts)) return null;
  if (volts < MIN_VALID_VOLTS || volts > MAX_VALID_VOLTS) return null;
  const fullVolts = LI_ION_CURVE[0]?.[0];
  if (fullVolts !== undefined && volts >= fullVolts) return 100;
  for (let i = 1; i < LI_ION_CURVE.length; i += 1) {
    const high = LI_ION_CURVE[i - 1];
    const low = LI_ION_CURVE[i];
    if (!high || !low) return 0;
    const [highVolts, highPercent] = high;
    const [lowVolts, lowPercent] = low;
    if (volts >= lowVolts) {
      const percent =
        lowPercent + ((volts - lowVolts) / (highVolts - lowVolts)) * (highPercent - lowPercent);
      return Math.round(percent);
    }
  }
  return 0;
}

export type ChargeLevel = "high" | "medium" | "low";

export function chargeLevel(percent: number): ChargeLevel {
  if (percent >= 60) return "high";
  if (percent >= 30) return "medium";
  return "low";
}

/** Ключи метрик, считающиеся зарядом батареи (канал LPP 116 в реестре gateway). */
const batteryMetricKeys = new Set(["battery_voltage", "battery"]);

export function hasBatteryMetric(metrics: readonly string[]): boolean {
  return metrics.some((metric) => batteryMetricKeys.has(metric));
}
