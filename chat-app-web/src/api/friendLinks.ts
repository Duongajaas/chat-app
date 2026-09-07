import { apiClient } from "./client";

export interface FriendLink {
  id: string;
  url: string;
  expiresAt: string | null;
  maxUses: number | null;
  usedCount: number;
  createdAt: string;
}

export interface PublicFriendProfile {
  id: string;
  username: string;
  fullName: string;
  avatarUrl: string | null;
  relationshipStatus: "Friends" | "Pending" | "None";
}

export interface FriendRequestResponse {
  id: string;
  senderId: string;
  receiverId: string;
  status: string;
  createdAt: string;
}

export const friendLinksApi = {
  getMine: () => apiClient.get<FriendLink>("/friend-links/me").then((response) => response.data),
  regenerate: () => apiClient.post<FriendLink>("/friend-links/regenerate").then((response) => response.data),
  revoke: (linkId: string) => apiClient.delete(`/friend-links/${linkId}`).then((response) => response.data),
  resolve: (token: string) => apiClient.get<PublicFriendProfile>(`/friend-links/resolve/${encodeURIComponent(token)}`).then((response) => response.data),
  sendRequest: (receiverId: string) => apiClient.post<FriendRequestResponse>("/friend-requests", { receiverId }).then((response) => response.data),
};