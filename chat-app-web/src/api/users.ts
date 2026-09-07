import { apiClient } from "./client";
import type { User, UpdateProfilePayload } from "../types";

export const usersApi = {
  getMe: () => apiClient.get<User>("/users/me").then((r) => r.data),
  updateMe: (payload: UpdateProfilePayload) =>
    apiClient.put<User>("/users/me", payload).then((r) => r.data),
};