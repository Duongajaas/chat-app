import { apiClient } from "./client";
import type { Report, ModerationDecisionRequest } from "../types";

export const moderationApi = {
  listReports: (params?: { status?: string; targetType?: string; targetUserId?: string }) =>
    apiClient.get<Report[]>("/moderation/reports", { params }).then((r) => r.data),
  getReport: (reportId: string) =>
    apiClient.get<Report>(`/moderation/reports/${reportId}`).then((r) => r.data),
  updateStatus: (reportId: string, status: string) =>
    apiClient.put<Report>(`/moderation/reports/${reportId}/status`, { status }).then((r) => r.data),
  applyDecision: (reportId: string, data: ModerationDecisionRequest) =>
    apiClient.post<Report>(`/moderation/reports/${reportId}/decision`, data).then((r) => r.data),
  restrictUser: (targetUserId: string, minutes: number, reason: string) =>
    apiClient.post(`/moderation/users/${targetUserId}/restrict`, { minutes, reason }).then((r) => r.data),
  unrestrictUser: (targetUserId: string) =>
    apiClient.post(`/moderation/users/${targetUserId}/unrestrict`).then((r) => r.data),
};
