import { apiClient } from "./client";

export interface Friend {
  id: string;
  username: string;
  fullName: string;
  avatarUrl: string | null;
  friendSince: string;
}

export const friendsApi = {
  list: () => apiClient.get<Friend[]>("/friends").then((response) => response.data),
};