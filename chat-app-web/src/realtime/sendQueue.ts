export interface QueuePayload {
  clientMessageId: string;
  content?: string | null;
  replyToMessageId?: string | null;
  mentions?: { userId: string; start: number; length: number }[];
  sourceMessageId?: string;
}
export interface QueueTask {
  accountId: string; session: number; conversationId: string; payload: QueuePayload;
}
export type DeliveryState = "Queued" | "Sending" | "Reconciling" | "RetryWaiting";
export interface Receipt<T> { state: string; message?: T; messageId?: string | null; }
interface Dependencies<T> {
  current: (task: QueueTask) => boolean;
  send: (task: QueueTask) => Promise<T>;
  reconcile: (task: QueueTask) => Promise<Receipt<T>>;
  confirm: (task: QueueTask, receipt: Receipt<T>) => void;
  state: (task: QueueTask, state: DeliveryState, error?: string) => void;
  stop: (task: QueueTask, error: string) => void;
  failure: (error: unknown) => { retry: boolean; delay?: number; message: string };
}
export const retryPolicy = { maxFailures: 8, lifetimeMs: 15 * 60_000, capacity: 100, baseDelayMs: 1000, maxDelayMs: 30_000 };
type Entry = QueueTask & { created: number; due: number; failures: number; uncertain: boolean; done: boolean };

/** Memory only. One runner per conversation, including newly enqueued sends. */
export class SendQueue<T> {
  private entries = new Map<string, Entry>();
  private running = new Set<string>();
  private timer?: ReturnType<typeof setTimeout>;
  constructor(private deps: Dependencies<T>) {}
  private key(task: QueueTask) { return `${task.session}:${task.accountId}:${task.payload.clientMessageId}`; }
  enqueue(task: QueueTask, uncertain = false) {
    if (!this.deps.current(task)) return false;
    const existing = this.entries.get(this.key(task));
    if (existing) {
      if (existing.conversationId !== task.conversationId || JSON.stringify(existing.payload) !== JSON.stringify(task.payload))
        throw new Error("Mã gửi đã được dùng cho một yêu cầu khác.");
      return true;
    }
    if (this.entries.size >= retryPolicy.capacity) throw new Error("Đã có 100 tin chờ gửi. Vui lòng chờ rồi thử lại.");
    const entry: Entry = { ...structuredClone(task), created: Date.now(), due: Date.now(), failures: 0, uncertain, done: false };
    this.entries.set(this.key(entry), entry);
    this.deps.state(entry, "Queued"); this.wake(); return true;
  }
  clear() {
    this.entries.forEach(e => { e.done = true; }); this.entries.clear();
    clearTimeout(this.timer); this.timer = undefined;
    // Old requests may still be unwinding; their finally blocks release runners.
  }
  acknowledge(accountId: string, session: number, clientId: string) {
    const key = `${session}:${accountId}:${clientId}`;
    const entry = this.entries.get(key);
    if (entry) { entry.done = true; this.entries.delete(key); this.wake(); }
  }
  wake = () => {
    clearTimeout(this.timer); this.timer = undefined;
    const seen = new Set<string>();
    for (const entry of this.entries.values()) {
      if (!this.deps.current(entry)) { entry.done = true; this.entries.delete(this.key(entry)); continue; }
      const lane = `${entry.session}:${entry.accountId}:${entry.conversationId}`;
      if (seen.has(lane)) continue; seen.add(lane);
      if (this.running.has(lane)) continue;
      if (Date.now() - entry.created >= retryPolicy.lifetimeMs) { this.finish(entry, "Đã hết thời gian tự gửi. Bạn có thể thử lại thủ công."); continue; }
      if (entry.due <= Date.now()) { this.running.add(lane); void this.run(entry, lane); }
    }
    if (this.entries.size) this.timer = setTimeout(this.wake, 500);
  };
  private active(entry: Entry) { return !entry.done && this.deps.current(entry); }
  private finish(entry: Entry, error?: string) {
    if (!this.active(entry)) return;
    entry.done = true; this.entries.delete(this.key(entry));
    if (error) this.deps.stop(entry, error);
  }
  private async run(entry: Entry, lane: string) {
    try {
      if (entry.uncertain) {
        this.deps.state(entry, "Reconciling");
        const receipt = await this.deps.reconcile(entry);
        if (!this.active(entry)) return;
        if (receipt.state === "Conflict") { this.finish(entry, "Mã gửi đã dùng cho nội dung khác. Không thể tự gửi lại."); return; }
        if (receipt.state !== "NotFound") {
          if (!["Readable", "Recalled", "Hidden", "Unavailable"].includes(receipt.state)) throw new Error("Kết quả đối soát không hợp lệ.");
          this.finish(entry); this.deps.confirm(entry, receipt); return;
        }
      }
      if (!this.active(entry)) return;
      this.deps.state(entry, "Sending");
      // Once submitted, timeout or cancellation cannot prove that the DB did not commit.
      entry.uncertain = true;
      const message = await this.deps.send(entry);
      if (!this.active(entry)) return;
      this.finish(entry); this.deps.confirm(entry, { state: "Readable", message });
    } catch (error) {
      if (!this.active(entry)) return;
      const failure = this.deps.failure(error);
      entry.failures++;
      if (!failure.retry || entry.failures >= retryPolicy.maxFailures) this.finish(entry, failure.message);
      else {
        const backoff = Math.min(retryPolicy.maxDelayMs, retryPolicy.baseDelayMs * 2 ** (entry.failures - 1));
        entry.due = Date.now() + Math.max(failure.delay ?? 0, Math.min(retryPolicy.maxDelayMs, backoff * (0.8 + Math.random() * 0.4)));
        this.deps.state(entry, "RetryWaiting", "Chờ kết nối để gửi lại…");
      }
    } finally { this.running.delete(lane); this.wake(); }
  }
}
