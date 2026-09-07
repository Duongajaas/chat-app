import { apiClient } from "./client";
import type { Device } from "../types";

export const devicesApi = {
  list: () => apiClient.get<Device[]>("/auth/devices").then((r) => r.data),
  revoke: (deviceId: string) => apiClient.delete(`/auth/devices/${deviceId}`).then((r) => r.data),
};