import { apiClient } from "./client";
import type { ChatMessage, MessageListResponse } from "../types";

export const messagesApi = {
  reconcile: (id: string, items: import("../realtime/sendQueue").QueuePayload[]) =>
    apiClient.post<Array<{ clientMessageId: string; state: string; messageId?: string | null; message?: ChatMessage }>>(
      `/conversations/${id}/messages/reconcile`, { items }).then(r => r.data),

  list: (conversationId: string, before?: number | null, after?: number | null, limit: number = 50) =>
    apiClient
      .get<MessageListResponse>(`/conversations/${conversationId}/messages`, {
        params: {
          ...(before !== null && before !== undefined ? { before } : {}),
          ...(after !== null && after !== undefined ? { after } : {}),
          limit,
        },
      })
      .then((r) => r.data),

  send: (conversationId: string, payload: import("../realtime/sendQueue").QueuePayload) =>
    apiClient.post<ChatMessage>(`/conversations/${conversationId}/messages`, payload).then((r) => r.data),

  batch: (conversationId: string, ids: string[]) => apiClient.post<ChatMessage[]>(`/conversations/${conversationId}/messages/batch`, { ids }).then(r => r.data),
  pins: (id: string, signal?: AbortSignal) => apiClient.get<Array<{ id: string; message: ChatMessage }>>(`/conversations/${id}/pins`, { signal }).then(r => r.data),
  pin: (id: string, messageId: string) => apiClient.put(`/conversations/${id}/pins/${messageId}`),
  unpin: (id: string, messageId: string) => apiClient.delete(`/conversations/${id}/pins/${messageId}`),
  mentions: (id: string) => apiClient.get<ChatMessage[]>(`/conversations/${id}/mentions`).then(r => r.data),
  forward: (messageId: string, targetConversationId: string, clientMessageId: string) =>
    apiClient.post<ChatMessage>(`/messages/${messageId}/forward`, { targetConversationId, clientMessageId }).then(r => r.data),
  states: (conversationId: string, ids: string[]) => apiClient.post<Array<{ id: string; deleted: boolean; hidden: boolean }>>(`/conversations/${conversationId}/message-states`, { ids }).then(r => r.data),
  deleteForMe: (messageId: string) => apiClient.delete(`/messages/${messageId}/for-me`),
  delete: (messageId: string) => apiClient.delete(`/messages/${messageId}`).then((r) => r.data),
};

