import { messageDelivery } from "../realtime/messageDelivery";
import { useAuth } from "../context/AuthContext";
import { useEffect, useRef, useState } from "react";
import { useChatStore } from "../store/chatStore";
import { messagesApi } from "../api/messages";
import { extractErrorMessage } from "../api/auth";
import { getSessionVersion } from "../api/client";
import type { ChatMessage, Conversation, PinnedMessage } from "../types";
import { PinsBar } from "./PinsBar";

export function PinnedMessages({ conversation, onOpen, onPinsChange, senderName }: { senderName: (message: ChatMessage) => string; conversation: Conversation; onOpen: (message: ChatMessage) => void; onPinsChange?: (ids: string[]) => void }) {
  const [pins, setPins] = useState<PinnedMessage[]>([]);
  const [error, setError] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);
  const mutation = useRef(0);
  const busy = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const deleted = useChatStore(state => state.deletedMessageIds);
  const hidden = useChatStore(state => state.hiddenMessageIds);
  useEffect(() => { onPinsChange?.(pins.filter(pin => !deleted[pin.message.id] && !hidden[pin.message.id]).map(pin => pin.message.id)); }, [pins, deleted, hidden, onPinsChange]);
  useEffect(() => {
    let current = true;
    let inFlight = false;
    let dirty = true;
    let lastAttempt = 0;
    let scheduled: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();
    const session = getSessionVersion();
    const valid = () => current && session === getSessionVersion();
    const load = async () => {
      if (!valid() || document.visibilityState === "hidden" || inFlight) return;
      if (!dirty && Date.now() - lastAttempt < 30000) return;
      dirty = false; inFlight = true; lastAttempt = Date.now();
      const revision = mutation.current;
      try {
        const rows = await messagesApi.pins(conversation.id, controller.signal);
        if (valid() && revision === mutation.current) { setPins(rows); setError(""); }
        else if (valid()) dirty = true;
      } catch {
        if (valid() && !controller.signal.aborted) setError("Không tải được tin ghim.");
      } finally {
        inFlight = false;
        // An invalidation during a request needs one follow-up, not parallel requests.
        if (valid() && dirty) schedule();
      }
    };
    const schedule = () => {
      if (!valid() || scheduled !== undefined) return;
      scheduled = setTimeout(() => { scheduled = undefined; void load(); }, 150);
    };
    const onChanged = () => { dirty = true; schedule(); };
    const onVisible = () => { if (document.visibilityState === "visible") schedule(); };
    schedule();
    const timer = setInterval(schedule, 30000);
    window.addEventListener("chat:pins-changed", onChanged);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      current = false; controller.abort(); clearInterval(timer); clearTimeout(scheduled);
      window.removeEventListener("chat:pins-changed", onChanged);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [conversation.id, conversation.version]);
  const canPin = !conversation.hasLeft && !conversation.closedAt && !conversation.isBlocked &&
    (conversation.type === "Direct" || conversation.permissions?.includes("PinMessage"));
  const visiblePins = pins.filter(pin => !deleted[pin.message.id] && !hidden[pin.message.id] && !["Deleted", "Hidden", "Unavailable"].includes(pin.message.status ?? ""))
    .sort((a, b) => Date.parse(b.createdAt ?? b.message.createdAt) - Date.parse(a.createdAt ?? a.message.createdAt));
  async function unpin(pin: PinnedMessage) {
    if (busy.current || !canPin) return;
    busy.current = true; mutation.current++; setBusyId(pin.id); setError("");
    const session = getSessionVersion();
    try {
      // The existing DELETE API is keyed by message ID, not by pinned-record ID.
      await messagesApi.unpin(conversation.id, pin.message.id);
      if (!mounted.current || session !== getSessionVersion()) return;
      mutation.current++;
      setPins(rows => rows.filter(row => row.id !== pin.id));
      window.dispatchEvent(new Event("chat:pins-changed"));
    } catch (e) { if (mounted.current && session === getSessionVersion()) setError(extractErrorMessage(e)); }
    finally { busy.current = false; if (mounted.current && session === getSessionVersion()) setBusyId(null); }
  }
  return <>
    <PinsBar pins={visiblePins} canUnpin={!!canPin} busyId={busyId} onUnpin={pin => void unpin(pin)} onOpen={onOpen} senderName={senderName}/>
    {error && <small className="pins-bar__error" role="status">{error}</small>}
  </>;
}
export function ForwardMessage({ message, conversations, onClose }: { message: ChatMessage; conversations: Conversation[]; onClose: () => void }) {
  const { user } = useAuth();
  const [target, setTarget] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [key, setKey] = useState(() => crypto.randomUUID());
  return <section className="group-settings" aria-label="Chuyển tiếp tin nhắn">
    <h3>Chuyển tiếp tin nhắn</h3>
    <label>Hội thoại đích<select value={target} disabled={busy} onChange={e => { setTarget(e.target.value); setKey(crypto.randomUUID()); }}>
      <option value="">Chọn hội thoại</option>
      {conversations.filter(c => !c.hasLeft && !c.closedAt && !c.isBlocked).map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
    </select></label>
    {error && <p role="alert">{error}</p>}
    <button disabled={!target || busy} onClick={async () => {
      setBusy(true); const session = getSessionVersion();
      try {
        if (!user) return;
        useChatStore.getState().appendMessage(target, { id: key, clientMessageId: key, conversationId: target,
          senderId: user.id, content: message.content, sequence: Number.MAX_VALUE, status: "Sending",
          createdAt: new Date().toISOString(), pendingSourceMessageId: message.id, isForwarded: true });
        messageDelivery.enqueue({ accountId: user.id, session, conversationId: target,
          payload: { clientMessageId: key, sourceMessageId: message.id } });
        onClose();
      }
      catch (e) { if (session === getSessionVersion()) { useChatStore.getState().removeMessage(target, key); setError(extractErrorMessage(e)); } }
      finally { if (session === getSessionVersion()) setBusy(false); }
    }}>Chuyển tiếp</button>
    <button disabled={busy} onClick={onClose}>Hủy</button>
  </section>;
}



