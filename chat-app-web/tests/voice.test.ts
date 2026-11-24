import { describe, expect, it } from "vitest";
import { MAX_VOICE_SECONDS, WAVEFORM_BARS, buildWaveform, formatDuration, parseWaveform } from "../src/utils/voice";

describe("formatDuration", () => {
  it("formats seconds as m:ss", () => {
    expect(formatDuration(0)).toBe("0:00");
    expect(formatDuration(7)).toBe("0:07");
    expect(formatDuration(65)).toBe("1:05");
    expect(formatDuration(MAX_VOICE_SECONDS)).toBe("2:00");
  });
  it("is safe for missing or invalid values", () => {
    expect(formatDuration(null)).toBe("0:00");
    expect(formatDuration(undefined)).toBe("0:00");
    expect(formatDuration(Number.NaN)).toBe("0:00");
    expect(formatDuration(-4)).toBe("0:00");
  });
});

describe("parseWaveform", () => {
  it("reads the JSON array sent by the server and clamps it to 6-100", () => {
    expect(parseWaveform("[0,50,150,12.4]")).toEqual([6, 50, 100, 12]);
  });
  it("falls back to neutral bars for missing or malformed data", () => {
    for (const bad of [null, undefined, "", "not json", "{}", "[]", '"x"', JSON.stringify(Array(500).fill(10))]) {
      const bars = parseWaveform(bad as string | null | undefined);
      expect(bars.length).toBeGreaterThan(0);
      expect(bars.every(b => b >= 6 && b <= 100)).toBe(true);
    }
  });
  it("treats non-numeric entries as quiet", () => {
    expect(parseWaveform('[10,"x",null]')).toEqual([10, 6, 6]);
  });
});

describe("buildWaveform", () => {
  it("always returns the requested number of bars within 6-100", () => {
    for (const length of [0, 1, 7, 40, 1200]) {
      const bars = buildWaveform(Array.from({ length }, (_, i) => (i % 10) / 10));
      expect(bars).toHaveLength(WAVEFORM_BARS);
      expect(bars.every(b => Number.isInteger(b) && b >= 6 && b <= 100)).toBe(true);
    }
  });
  it("scales so the loudest bar is full height", () => {
    const bars = buildWaveform([0.01, 0.02, 0.2, 0.02], 4);
    expect(Math.max(...bars)).toBe(100);
    expect(bars[2]).toBe(100);
    expect(bars[0]).toBeLessThan(bars[2]);
  });
  it("keeps the shape of the recording (JSON stays well under the 2000-character server limit)", () => {
    const json = JSON.stringify(buildWaveform(Array.from({ length: 1200 }, () => Math.random())));
    expect(json.length).toBeLessThan(400);
  });
});
