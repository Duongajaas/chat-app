import { apiClient } from "./client";

export interface PendingFriendRequest {
  id: string;
  senderId: string;
  username: string;
  fullName: string;
  avatarUrl: string | null;
  createdAt: string;
}

export const friendRequestsApi = {
  getPending: () => apiClient.get<PendingFriendRequest[]>("/friend-requests/pending").then((response) => response.data),
  accept: (requestId: string) => apiClient.put(`/friend-requests/${requestId}/accept`).then((response) => response.data),
};