import axios from "axios";
import { getSessionVersion, onSessionCleared } from "../api/client";
import { messagesApi } from "../api/messages";
import { extractErrorMessage } from "../api/auth";
import { useChatStore } from "../store/chatStore";
import { refreshConversation } from "./connection";
import { SendQueue, type QueueTask } from "./sendQueue";
import type { ChatMessage } from "../types";

function pending(task: QueueTask) {
  return useChatStore.getState().messagesByConversation[task.conversationId]?.find(m =>
    m.clientMessageId === task.payload.clientMessageId && m.senderId === task.accountId && (m.status === "Sending" || m.status === "Failed"));
}
export const messageDelivery = new SendQueue<ChatMessage>({
  current: task => task.session === getSessionVersion(),
  send: task => task.payload.sourceMessageId
    ? messagesApi.forward(task.payload.sourceMessageId, task.conversationId, task.payload.clientMessageId)
    : messagesApi.send(task.conversationId, task.payload),
  reconcile: async task => {
    const rows = await messagesApi.reconcile(task.conversationId, [task.payload]);
    const row = rows.find(r => r.clientMessageId === task.payload.clientMessageId);
    if (!row) throw new Error("Thiếu kết quả đối soát tin nhắn.");
    return row;
  },
  state: (task, deliveryState, deliveryError) => {
    const message = pending(task);
    if (message) useChatStore.getState().updateMessage(task.conversationId, message.id, { status: "Sending", deliveryState, deliveryError });
  },
  stop: (task, error) => {
    const message = pending(task);
    if (message) useChatStore.getState().updateMessage(task.conversationId, message.id, { status: "Failed", deliveryState: undefined, deliveryError: error });
  },
  confirm: (task, receipt) => {
    const store = useChatStore.getState();
    store.removeMessage(task.conversationId, task.payload.clientMessageId);
    const status = receipt.message?.status;
    if (receipt.state === "Hidden" || receipt.state === "Unavailable" || status === "Hidden" || status === "Unavailable") {
      const id = receipt.messageId ?? receipt.message?.id;
      if (id) store.hideMessage(task.conversationId, id);
    } else if (receipt.message) store.appendMessage(task.conversationId, receipt.message);
    refreshConversation(task.conversationId);
  },
  failure: error => {
    const status = axios.isAxiosError(error) ? error.response?.status : undefined;
    const value = axios.isAxiosError(error) ? error.response?.headers?.["retry-after"] : undefined;
    const delay = value == null ? 0 : Number.isFinite(Number(value)) ? Number(value) * 1000 : Math.max(0, Date.parse(String(value)) - Date.now());
    const retry = axios.isAxiosError(error) && !axios.isCancel(error) && (status == null || [408, 429, 502, 503, 504].includes(status));
    return { retry, delay: Number.isFinite(delay) ? delay : 0, message: extractErrorMessage(error, "Không thể gửi tin. Hãy kiểm tra kết nối và thử lại.") };
  },
});
onSessionCleared(() => messageDelivery.clear());
for (const event of ["online", "visibilitychange", "chat:reconnected"]) {
  const target = event === "visibilitychange" ? document : window;
  target.addEventListener(event, messageDelivery.wake);
}
// HTTP response, realtime and history sync may all confirm the same optimistic entry.
useChatStore.subscribe((state, previous) => {
  for (const [id, rows] of Object.entries(state.messagesByConversation)) {
    if (rows === previous.messagesByConversation[id]) continue;
    rows.filter(m => m.clientMessageId && m.sequence > 0 && m.sequence < Number.MAX_SAFE_INTEGER && (m.status === "Sent" || m.status === "Deleted"))
      .forEach(m => messageDelivery.acknowledge(m.senderId, getSessionVersion(), m.clientMessageId!));
  }
});
