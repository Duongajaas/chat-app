import { apiClient } from "./client";
import type { Conversation } from "../types";

export const conversationsApi = {
  list: () => apiClient.get<Conversation[]>("/conversations").then((r) => r.data),
  getById: (conversationId: string) => apiClient.get<Conversation>(`/conversations/${conversationId}`).then((r) => r.data),
  createDirect: (otherUserId: string) => apiClient.post<Conversation>("/conversations/direct", { otherUserId }).then((r) => r.data),
  createGroup: (payload: { name: string; memberIds: string[] }) => apiClient.post<Conversation>("/conversations/group", payload).then((r) => r.data),
  remove: (conversationId: string) => apiClient.delete(`/conversations/${conversationId}`).then((r) => r.data),
  hide: (conversationId: string) => apiClient.post(`/conversations/${conversationId}/hide`).then((r) => r.data),
};
