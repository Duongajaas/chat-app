import { apiClient } from "./client";
import type { Conversation } from "../types";

export type GroupRole = "Owner" | "Admin" | "Member";
export interface GroupMember {
  userId: string; fullName: string; avatarUrl: string | null; role: GroupRole;
  joinedAt: string; canKick: boolean; canChangeRole: boolean;
}
export interface GroupInvite {
  id: string; createdBy: string; maxUses: number; usedCount: number;
  expiresAt: string; revokedAt: string | null; code: string | null;
}
export const groupsApi = {
  members: (id: string) => apiClient.get<GroupMember[]>(`/conversations/${id}/members`).then(r => r.data),
  rename: (id: string, name: string) => apiClient.patch<Conversation>(`/conversations/${id}/group`, { name }).then(r => r.data),
  add: (id: string, userId: string) => apiClient.post(`/conversations/${id}/members`, { userId }),
  kick: (id: string, userId: string) => apiClient.delete(`/conversations/${id}/members/${userId}`),
  role: (id: string, userId: string, role: GroupRole) => apiClient.put(`/conversations/${id}/members/${userId}/role`, { role }),
  leave: (id: string) => apiClient.post(`/conversations/${id}/leave`),
  invites: (id: string) => apiClient.get<GroupInvite[]>(`/conversations/${id}/invites`).then(r => r.data),
  createInvite: (id: string, key: string, maxUses: number) => apiClient.post<GroupInvite>(
    `/conversations/${id}/invites`, { maxUses }, { headers: { "Idempotency-Key": key } }).then(r => r.data),
  revoke: (id: string, inviteId: string) => apiClient.delete(`/conversations/${id}/invites/${inviteId}`),
  join: (code: string) => apiClient.post<Conversation>("/conversations/join", { code }).then(r => r.data),
};
