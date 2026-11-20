export const MAX_VOICE_SECONDS = 120;
export const MIN_VOICE_SECONDS = 1;
export const WAVEFORM_BARS = 40;

/** m:ss for a length in seconds. */
export function formatDuration(seconds: number | null | undefined) {
  const total = Math.max(0, Math.floor(Number.isFinite(seconds as number) ? (seconds as number) : 0));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

const FLAT_BARS = Array.from({ length: 32 }, (_, i) => 28 + ((i * 7) % 5) * 6);

/** Parses the server's waveformPoints (a JSON array of 0-100 numbers); falls back to neutral bars. */
export function parseWaveform(value: string | null | undefined): number[] {
  if (!value) return FLAT_BARS;
  try {
    const parsed: unknown = JSON.parse(value);
    if (!Array.isArray(parsed) || parsed.length === 0 || parsed.length > 200) return FLAT_BARS;
    const bars = parsed.map(item => (typeof item === "number" && Number.isFinite(item) ? Math.min(100, Math.max(6, Math.round(item))) : 6));
    return bars;
  } catch {
    return FLAT_BARS;
  }
}

/** Reduces microphone levels (0-1) to a fixed number of 6-100 bars, scaled so the loudest bar is full height. */
export function buildWaveform(levels: number[], bars = WAVEFORM_BARS): number[] {
  if (levels.length === 0) return Array.from({ length: bars }, (_, i) => 28 + ((i * 7) % 5) * 6);
  const buckets = Array.from({ length: bars }, (_, index) => {
    const from = Math.floor((index * levels.length) / bars);
    const to = Math.max(from + 1, Math.floor(((index + 1) * levels.length) / bars));
    return Math.max(...levels.slice(from, to));
  });
  const peak = Math.max(...buckets, 0.001);
  return buckets.map(level => Math.min(100, Math.max(6, Math.round((level / peak) * 100))));
}
