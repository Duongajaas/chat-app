import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { SendQueue, type QueueTask } from "../src/realtime/sendQueue";

let version = 1;
let queue: SendQueue<string>;
const task = (id: string, conversationId = "room"): QueueTask => ({ accountId: "a", session: 1, conversationId,
  payload: { clientMessageId: id, content: "😀 @an", replyToMessageId: "original", mentions: [{ userId: "an", start: 3, length: 3 }] } });
function setup() {
  const deps = { current: (t: QueueTask) => t.session === version,
    send: vi.fn(async (_t: QueueTask) => "saved"),
    reconcile: vi.fn(async (_t: QueueTask): Promise<{ state: string; message?: string }> => ({ state: "NotFound" })),
    confirm: vi.fn(), state: vi.fn(), stop: vi.fn(),
    failure: (error: unknown) => ({ retry: error !== "permanent", delay: error === "rate" ? 10000 : 0, message: "failed" }) };
  queue = new SendQueue(deps); return deps;
}
beforeEach(() => { vi.useFakeTimers(); version = 1; });
afterEach(() => { queue.clear(); vi.useRealTimers(); });

it("reconciles a lost ACK with the same immutable payload instead of inserting again", async () => {
  const deps = setup(); deps.send.mockRejectedValueOnce("network");
  deps.reconcile.mockResolvedValue({ state: "Readable", message: "already-saved" });
  const original = task("key"); queue.enqueue(original); original.payload.content = "edited draft";
  await vi.advanceTimersByTimeAsync(2000);
  expect(deps.send).toHaveBeenCalledTimes(1);
  expect(deps.reconcile.mock.calls[0][0].payload.content).toBe("😀 @an");
  expect(deps.confirm.mock.calls[0][1].message).toBe("already-saved");
});

it("serializes new sends behind retry while another conversation progresses", async () => {
  const deps = setup(); const order: string[] = [];
  deps.send.mockImplementation(async t => { order.push(t.payload.clientMessageId); if (order.length === 1) throw "network"; return "saved"; });
  queue.enqueue(task("one")); queue.enqueue(task("two")); queue.enqueue(task("other", "other"));
  await vi.advanceTimersByTimeAsync(0); expect(order).toEqual(["one", "other"]);
  queue.wake(); queue.wake(); queue.enqueue(task("three"));
  await vi.advanceTimersByTimeAsync(2000);
  expect(order).toEqual(["one", "other", "one", "two", "three"]);
});

it("coalesces duplicate enqueues and simultaneous wake signals", async () => {
  const deps = setup(); let finish!: (value: string) => void;
  deps.send.mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  queue.enqueue(task("same")); queue.enqueue(task("same")); queue.wake(); queue.wake();
  expect(deps.send).toHaveBeenCalledTimes(1); finish("saved"); await vi.advanceTimersByTimeAsync(0);
  expect(deps.confirm).toHaveBeenCalledTimes(1);
});

it("honors Retry-After even when online or reconnect wakes the scheduler", async () => {
  const deps = setup(); deps.send.mockRejectedValueOnce("rate"); queue.enqueue(task("rate"));
  await vi.advanceTimersByTimeAsync(9000); queue.wake();
  expect(deps.send).toHaveBeenCalledTimes(1);
  await vi.advanceTimersByTimeAsync(1500); expect(deps.send).toHaveBeenCalledTimes(2);
});

it("logout drops pending work and ignores late responses from old session", async () => {
  const deps = setup(); let finish!: (value: string) => void;
  deps.send.mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  queue.enqueue(task("one")); queue.enqueue(task("two")); version = 2; queue.clear();
  finish("old account secret"); await vi.advanceTimersByTimeAsync(60000);
  expect(deps.send).toHaveBeenCalledTimes(1); expect(deps.confirm).not.toHaveBeenCalled();
  expect(deps.stop).not.toHaveBeenCalled();
});

it.each(["Hidden", "Recalled", "Unavailable"])("settles %s without resending", async state => {
  const deps = setup(); deps.reconcile.mockResolvedValue({ state }); queue.enqueue(task("key"), true);
  await vi.advanceTimersByTimeAsync(0); expect(deps.send).not.toHaveBeenCalled();
  expect(deps.confirm.mock.calls[0][1].state).toBe(state);
});

it("stops on business errors and lets the next message proceed", async () => {
  const deps = setup(); deps.send.mockRejectedValueOnce("permanent");
  queue.enqueue(task("bad")); queue.enqueue(task("next")); await vi.advanceTimersByTimeAsync(10000);
  expect(deps.stop).toHaveBeenCalledTimes(1); expect(deps.send).toHaveBeenCalledTimes(2);
});

it("realtime acknowledgement prevents retry of a lost HTTP response", async () => {
  const deps = setup(); deps.send.mockRejectedValueOnce("network"); queue.enqueue(task("key"));
  await vi.advanceTimersByTimeAsync(0); queue.acknowledge("a", 1, "key");
  await vi.advanceTimersByTimeAsync(60000); expect(deps.send).toHaveBeenCalledTimes(1);
  expect(deps.reconcile).not.toHaveBeenCalled();
});

it("stops automatic retries after the bounded failure budget", async () => {
  const deps = setup(); deps.send.mockRejectedValue("network"); queue.enqueue(task("key"));
  await vi.advanceTimersByTimeAsync(300000);
  expect(deps.send).toHaveBeenCalledTimes(8); expect(deps.stop).toHaveBeenCalledTimes(1);
});
