import { apiClient } from "./client";
import type { Conversation, ConversationPage } from "../types";

export const conversationsApi = {
  list: (cursor?: string | null) => apiClient.get<ConversationPage>("/conversations", { params: { cursor, limit: 30 } }).then((r) => r.data),
  getById: (conversationId: string) => apiClient.get<Conversation>(`/conversations/${conversationId}`).then((r) => r.data),
  createDirect: (otherUserId: string) => apiClient.post<Conversation>("/conversations/direct", { otherUserId }).then((r) => r.data),
  createGroup: (payload: { name: string; memberIds: string[] }, key: string) => apiClient.post<Conversation>("/conversations/group", { ...payload, type: "Group" }, { headers: { "Idempotency-Key": key } }).then((r) => r.data),
  remove: (conversationId: string) => apiClient.delete(`/conversations/${conversationId}`).then((r) => r.data),
  hide: (conversationId: string) => apiClient.post(`/conversations/${conversationId}/hide`).then((r) => r.data),
};
