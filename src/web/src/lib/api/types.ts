import type { SensorState } from "../format";

export type SensorSummary = {
  slug: string;
  displayName: string;
  description: string | null;
  latitude: number | null;
  longitude: number | null;
  metrics: string[];
  /** Latest LPP 116 battery voltage, when the sensor reports it. */
  batteryVoltage: number | null;
  state: SensorState;
};

export type DashboardResponse = {
  summary: {
    total: number;
    online: number;
    degraded: number;
    offline: number;
    unknown: number;
  };
  sensors: SensorSummary[];
};

export type SensorDetail = {
  slug: string;
  displayName: string;
  description: string | null;
  location: {
    latitude: number;
    longitude: number;
    precision: string;
  } | null;
  metrics: string[];
  protocol: string;
  pollIntervalSeconds: number;
  state: SensorState;
};

export type SensorStatus = {
  state: SensorState;
  lastPollAt: string | null;
  lastSuccessAt: string | null;
  consecutiveFailures: number;
  lastRssi: number | null;
  lastSnr: number | null;
  updatedAt: string | null;
};

export type LatestValue = {
  metric: string;
  displayName: string | null;
  timestamp: string;
  numericValue: number | null;
  textValue: string | null;
  unit: string | null;
};

export type LatestResponse = { values: LatestValue[] };

export type MeasurementPoint = {
  timestamp: string;
  min: number | null;
  avg: number | null;
  max: number | null;
};

export type MeasurementsResponse = {
  sensor: { slug: string; displayName: string };
  metric: { key: string; unit: string | null };
  range: { from: string; to: string; resolution: string };
  points: MeasurementPoint[];
};

export const forecastHorizons = ["1h", "6h", "12h", "24h"] as const;
export type ForecastHorizon = (typeof forecastHorizons)[number];

export const forecastHorizonLabels: Record<ForecastHorizon, string> = {
  "1h": "1 ч",
  "6h": "6 ч",
  "12h": "12 ч",
  "24h": "24 ч",
};

export type ForecastAvailability =
  "ready" | "insufficient_data" | "sparse_data" | "stale_data" | "low_quality" | "disabled";

export type ForecastPoint = {
  timestamp: string;
  predicted: number;
  lower: number;
  upper: number;
};

export type ForecastResponse = {
  sensor: { slug: string; displayName: string };
  metric: { key: string; unit: string | null };
  availability: ForecastAvailability;
  reason: string | null;
  generatedAt: string;
  lastObservationAt: string | null;
  range: {
    from: string | null;
    to: string | null;
    horizon: ForecastHorizon;
    step: string;
  };
  model: {
    kind: string;
    windowSize: number;
    trainingPoints: number;
    trainingFrom: string;
    trainingTo: string;
    observedCoverage: number;
    interpolatedPoints: number;
    mae: number;
    rmse: number;
    mase: number;
    intervalCoverage: number;
    confidenceLevel: number;
  } | null;
  points: ForecastPoint[];
};
