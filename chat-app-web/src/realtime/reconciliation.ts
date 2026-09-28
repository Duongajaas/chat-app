// Reconcile even when a Redis interruption did not close the browser's WebSocket.
export function startReconciliation(reconcile: () => Promise<void>, intervalMs = 30000) {
  let stopped = false;
  let running = false;
  async function tick() {
    if (stopped || running || document.visibilityState === "hidden") return;
    running = true;
    try { await reconcile(); } catch { /* Retry on the next tick or when the tab becomes visible. */ }
    finally { running = false; }
  }
  const onVisible = () => { void tick(); };
  const timer = setInterval(onVisible, intervalMs);
  document.addEventListener("visibilitychange", onVisible);
  return () => {
    stopped = true;
    clearInterval(timer);
    document.removeEventListener("visibilitychange", onVisible);
  };
}
