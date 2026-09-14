import * as signalR from "@microsoft/signalr";
import axios from "axios";
import { getSessionVersion, getValidAccessToken } from "../api/client";
import { messagesApi } from "../api/messages";
import { conversationsApi } from "../api/conversations";
import { useChatStore } from "../store/chatStore";
import { usePresenceStore } from "../store/presenceStore";
import { normalizeConversation } from "../utils/conversationUtils";
import type { ChatMessage } from "../types";
import { startReconciliation } from "./reconciliation";

let connection: signalR.HubConnection | null = null;
let starting: Promise<void> | null = null;
let userId: string | null = null;
let retryTimer: ReturnType<typeof setTimeout> | undefined;
let attempt = 0;
let stopReconciliation: (() => void) | undefined;
const subscriptions = new Set<string>();
const summaryTimers = new Map<string, ReturnType<typeof setTimeout>>();

export function refreshConversation(conversationId: string) {
  if (summaryTimers.has(conversationId)) return;
  const version = getSessionVersion();
  summaryTimers.set(conversationId, setTimeout(async () => {
    summaryTimers.delete(conversationId);
    try {
      const item = normalizeConversation(await conversationsApi.getById(conversationId));
      if (version !== getSessionVersion()) return;
      const store = useChatStore.getState();
      const previous = store.conversations.find(c => c.id === item.id);
      if ((previous?.version ?? 0) > (item.version ?? 0)) return;
      const others = store.conversations.filter(c => c.id !== item.id);
      store.setConversations(item.isHidden && !item.hasLeft ? others : [...others, item]);
    } catch { /* A membership may have been revoked while fetching. */ }
  }, 150));
}

export async function syncConversation(conversationId: string) {
  const version = getSessionVersion();
  const cached = useChatStore.getState().messagesByConversation[conversationId] ?? [];
  const confirmed = cached.filter(m => Number.isSafeInteger(m.sequence) && m.sequence > 0 && m.sequence < Number.MAX_SAFE_INTEGER);
  // Reconcile recalls/hides that occurred offline, including messages older than the new-message cursor.
  for (let offset = 0; offset < confirmed.length; offset += 100) {
    const states = await messagesApi.states(conversationId, confirmed.slice(offset, offset + 100).map(m => m.id));
    if (version !== getSessionVersion()) return;
    const returned = new Set(states.map(s => s.id));
    confirmed.slice(offset, offset + 100).filter(m => !returned.has(m.id)).forEach(m => useChatStore.getState().hideMessage(conversationId, m.id));
    states.forEach(state => {
      if (state.hidden) useChatStore.getState().hideMessage(conversationId, state.id);
      else if (state.deleted) useChatStore.getState().deleteMessage(conversationId, state.id);
    });
  }
  let cursor = useChatStore.getState().syncedThrough[conversationId] ?? 0;
  do {
    const page = await messagesApi.list(conversationId, undefined, cursor || undefined, 100);
    if (version !== getSessionVersion()) return;
    useChatStore.getState().syncNewerMessages(conversationId, page.messages);
    const newest = page.messages.reduce((max, m) => Math.max(max, m.sequence), cursor);
    useChatStore.getState().setSyncedThrough(conversationId, newest);
    usePresenceStore.getState().setConversationRead(conversationId, page.peerReadSequence ?? 0);
    if (!cursor) {
      useChatStore.getState().setHasMore(conversationId, page.hasMore);
      break;
    }
    if (!page.hasMore || !page.nextCursor || page.nextCursor <= cursor) break;
    cursor = page.nextCursor;
  } while (version === getSessionVersion());
}

export async function refreshConversationList() {
  const session = getSessionVersion();
  // An invitation can add an old group beyond the first immutable CreatedAt page.
  // Reconcile all metadata pages; message history is still polled only for the open chat.
  let cursor: string | null = null;
  do {
    const page = await conversationsApi.list(cursor);
    if (session !== getSessionVersion()) return;
    const store = useChatStore.getState();
    const entries = new Map(store.conversations.map(c => [c.id, c]));
    page.items.map(normalizeConversation).forEach(c => {
      if ((entries.get(c.id)?.version ?? 0) <= (c.version ?? 0)) entries.set(c.id, c);
    });
    store.setConversations([...entries.values()]);
    if (!page.nextCursor || page.nextCursor === cursor) break;
    cursor = page.nextCursor;
  } while (session === getSessionVersion());
}

async function recover(c: signalR.HubConnection) {
  await refreshConversationList();
  for (const id of subscriptions) {
    if (connection !== c) return;
    try { await c.invoke("JoinConversation", id); } catch { subscriptions.delete(id); }
  }
  const ids = new Set([...subscriptions, ...Object.keys(useChatStore.getState().messagesByConversation)]);
  for (const id of ids) {
    if (connection !== c) return;
    try { await syncConversation(id); refreshConversation(id); } catch { /* Retry on the next reconnect/open. */ }
  }
}

function scheduleRetry(c: signalR.HubConnection) {
  if (connection !== c || retryTimer) return;
  useChatStore.getState().setConnectionStatus("disconnected");
  retryTimer = setTimeout(() => {
    retryTimer = undefined;
    if (connection === c) void ensureStarted(c).catch(() => scheduleRetry(c));
  }, Math.min(30000, 1000 * 2 ** Math.min(attempt++, 5)));
}

function ensureStarted(c: signalR.HubConnection): Promise<void> {
  if (c.state === signalR.HubConnectionState.Connected) return Promise.resolve();
  if (starting) return starting;
  if (c.state !== signalR.HubConnectionState.Disconnected) return Promise.reject(new Error("Đang kết nối lại"));
  useChatStore.getState().setConnectionStatus("connecting");
  const promise = c.start().then(async () => {
    if (connection !== c) { await c.stop(); return; }
    attempt = 0;
    useChatStore.getState().setConnectionStatus("connected");
    await recover(c);
  }).finally(() => { if (starting === promise) starting = null; });
  starting = promise;
  return promise;
}

export function startChatHub(nextUserId: string) {
  if (connection && userId === nextUserId) return;
  void stopChatHub();
  userId = nextUserId;
  const version = getSessionVersion();
  const c = new signalR.HubConnectionBuilder().withUrl(import.meta.env.VITE_SIGNALR_HUB_URL || "/hubs/chat", {
    accessTokenFactory: async () => {
      try { return await getValidAccessToken(); }
      catch (error) {
        if (version === getSessionVersion() && axios.isAxiosError(error) && [401, 403].includes(error.response?.status ?? 0))
          window.dispatchEvent(new Event("auth:session-expired"));
        throw error;
      }
    }, withCredentials: true,
  }).withAutomaticReconnect().build();
  connection = c;
  stopReconciliation = startReconciliation(async () => {
    if (connection !== c || version !== getSessionVersion() || c.state !== signalR.HubConnectionState.Connected) return;
    await refreshConversationList();
    // Poll only the active conversation instead of every cached conversation.
    const active = useChatStore.getState().activeConversationId;
    if (active) {
      await syncConversation(active);
      if (connection !== c || version !== getSessionVersion()) return;
      refreshConversation(active);
    }
    await c.invoke("RefreshPresence");
  });
  const current = () => connection === c && version === getSessionVersion();
  c.on("PresenceSnapshot", (ids: string[]) => { if (current()) usePresenceStore.getState().setOnlineUserIds(ids); });
  c.on("UserPresenceChanged", (p: { userId: string; isOnline: boolean }) => {
    if (current()) usePresenceStore.getState().setUserPresence(p.userId, p.isOnline);
  });
  c.on("PresenceInvalidated", (p: { userId: string }) => {
    if (!current()) return;
    usePresenceStore.getState().setUserPresence(p.userId, false);
    useChatStore.getState().conversations.filter(x => x.peerUserId === p.userId).forEach(x => {
      usePresenceStore.getState().setTypingForConversation(x.id, false);
      refreshConversation(x.id);
    });
  });
  c.on("TypingIndicator", (p: { conversationId: string; userId: string; isTyping: boolean }) => {
    if (current() && p.userId !== nextUserId) usePresenceStore.getState().setTypingForConversation(p.conversationId, p.isTyping);
  });
  c.on("ReceiveMessage", (p: ChatMessage) => {
    if (!current() || typeof p.id !== "string" || typeof p.conversationId !== "string" || typeof p.senderId !== "string" ||
      !Number.isSafeInteger(p.sequence) || p.sequence <= 0 || !Number.isFinite(Date.parse(p.createdAt)) || typeof p.content !== "string") return;
    useChatStore.getState().appendMessage(p.conversationId, { ...p, status: p.status === "Deleted" ? "Deleted" : "Sent" });
    refreshConversation(p.conversationId);
  });
  c.on("MessagesRead", (p: { conversationId: string; userId: string; sequence: number }) => {
    if (!current()) return;
    if (p.userId !== nextUserId) usePresenceStore.getState().setConversationRead(p.conversationId, p.sequence);
    refreshConversation(p.conversationId);
  });
  for (const name of ["MessageDeleted", "MessageHidden"]) c.on(name, (p: { conversationId: string; messageId: string }) => {
    if (!current()) return;
    const store = useChatStore.getState();
    if (name === "MessageHidden") store.hideMessage(p.conversationId, p.messageId);
    else store.deleteMessage(p.conversationId, p.messageId);
    refreshConversation(p.conversationId);
  });
  c.on("ConversationChanged", (p: { conversationId: string }) => { if (current()) refreshConversation(p.conversationId); });
  for (const name of ["ConversationHidden", "ConversationLeft", "ConversationClosed"]) c.on(name, (p: { conversationId: string }) => {
    if (!current()) return;
    // Snapshot wins over late/duplicated events, especially after rejoin.
    refreshConversation(p.conversationId);
    void syncConversation(p.conversationId).catch(() => undefined);
  });
  c.onreconnecting(() => { if (current()) useChatStore.getState().setConnectionStatus("connecting"); });
  c.onreconnected(async () => {
    if (!current()) return;
    useChatStore.getState().setConnectionStatus("connected");
    await recover(c);
  });
  c.onclose(() => { if (current()) scheduleRetry(c); });
  void ensureStarted(c).catch(() => scheduleRetry(c));
}

export async function stopChatHub() {
  stopReconciliation?.(); stopReconciliation = undefined;
  const old = connection;
  connection = null;
  userId = null;
  starting = null;
  attempt = 0;
  clearTimeout(retryTimer); retryTimer = undefined;
  summaryTimers.forEach(clearTimeout); summaryTimers.clear();
  subscriptions.clear();
  if (old) await old.stop().catch(() => undefined);
}

async function connected() {
  const c = connection;
  if (!c) throw new Error("Chưa đăng nhập");
  await ensureStarted(c);
  if (c !== connection) throw new Error("Phiên đã thay đổi");
  return c;
}
export async function joinConversation(id: string) {
  subscriptions.add(id);
  const c = await connected();
  if (subscriptions.has(id)) await c.invoke("JoinConversation", id);
}
export async function leaveConversation(id: string) { subscriptions.delete(id); }
export async function sendTyping(id: string, typing: boolean) { await (await connected()).invoke("Typing", id, typing); }
export async function markConversationAsRead(id: string, sequence: number) { await (await connected()).invoke("MarkAsRead", id, sequence); }
