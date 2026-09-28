import { afterEach, expect, it, vi } from "vitest";
import { startReconciliation } from "../src/realtime/reconciliation";

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

it("reconciles missed events without a reconnect and cleans up the timer", async () => {
  vi.useFakeTimers();
  const doc = Object.assign(new EventTarget(), { visibilityState: "visible" });
  vi.stubGlobal("document", doc);
  const reconcile = vi.fn().mockRejectedValueOnce(new Error("Redis outage")).mockResolvedValue(undefined);
  const stop = startReconciliation(reconcile, 1000);
  await vi.advanceTimersByTimeAsync(2000);
  expect(reconcile).toHaveBeenCalledTimes(2);
  stop();
  doc.dispatchEvent(new Event("visibilitychange"));
  await vi.advanceTimersByTimeAsync(2000);
  expect(reconcile).toHaveBeenCalledTimes(2);
});

it("pauses hidden tabs, resumes on visibility, and does not overlap requests", async () => {
  vi.useFakeTimers();
  const doc = Object.assign(new EventTarget(), { visibilityState: "hidden" });
  vi.stubGlobal("document", doc);
  let finish!: () => void;
  const reconcile = vi.fn(() => new Promise<void>(resolve => { finish = resolve; }));
  const stop = startReconciliation(reconcile, 1000);
  await vi.advanceTimersByTimeAsync(1000);
  expect(reconcile).not.toHaveBeenCalled();
  doc.visibilityState = "visible";
  doc.dispatchEvent(new Event("visibilitychange"));
  await vi.advanceTimersByTimeAsync(2000);
  expect(reconcile).toHaveBeenCalledTimes(1);
  finish();
  await vi.advanceTimersByTimeAsync(1000);
  expect(reconcile).toHaveBeenCalledTimes(2);
  finish(); stop();
});
