import { apiClient, authClient } from "./client";
import { getDeviceInfo } from "../utils/device";
import type { AuthResponse, RegisterPayload, ApiErrorResponse, User } from "../types";
import { isAxiosError } from "axios";

export const authApi = {
  register: (payload: RegisterPayload) =>
    authClient.post<AuthResponse>("/auth/register", { ...payload, device: getDeviceInfo() }).then((r) => r.data),

  login: (payload: { username: string; password: string }) =>
    authClient.post<AuthResponse>("/auth/login", { ...payload, device: getDeviceInfo() }).then((r) => r.data),

  googleLogin: (idToken: string) =>
    authClient.post<AuthResponse>("/auth/google-login", { idToken: idToken.trim(), device: getDeviceInfo() }).then((r) => r.data),

  logout: () =>
    authClient.post("/auth/logout", {}).then((r) => r.data),

  forgotPassword: (email: string) =>
    authClient.post("/auth/forgot-password", { email }).then((r) => r.data),

  resetPassword: (token: string, newPassword: string) =>
    authClient.post("/auth/reset-password", { token, newPassword }).then((r) => r.data),

  me: () => apiClient.get<User>("/auth/me").then((r) => r.data),
};

export function extractErrorMessage(
  error: unknown,
  fallback = "Đã có lỗi xảy ra, vui lòng thử lại."
): string {
  if (isAxiosError<ApiErrorResponse>(error)) {
    return error.response?.data?.message || fallback;
  }
  return fallback;
}
