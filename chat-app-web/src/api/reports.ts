import { apiClient } from "./client";
import type { CreateReportRequest, Report } from "../types";

export const reportsApi = {
  create: (data: CreateReportRequest) =>
    apiClient.post<Report>("/reports", data).then((r) => r.data),
};
