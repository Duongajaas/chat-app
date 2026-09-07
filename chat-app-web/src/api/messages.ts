import { apiClient } from "./client";
import type { ChatMessage, MessageListResponse } from "../types";

export const messagesApi = {
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

  send: (conversationId: string, payload: { clientMessageId: string; content?: string; attachmentObjectKey?: string }) =>
    apiClient.post<ChatMessage>(`/conversations/${conversationId}/messages`, payload).then((r) => r.data),

  delete: (messageId: string) => apiClient.delete(`/messages/${messageId}`).then((r) => r.data),
};
