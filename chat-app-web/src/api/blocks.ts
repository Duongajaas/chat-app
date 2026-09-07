import { apiClient } from "./client";
import type { BlockedUser } from "../types";

export const blocksApi = {
  list: () => apiClient.get<BlockedUser[]>("/blocks").then((response) => response.data),
  block: (userId: string) => apiClient.post(`/blocks/${userId}`).then((response) => response.data),
  unblock: (userId: string) => apiClient.delete(`/blocks/${userId}`).then((response) => response.data),
};
