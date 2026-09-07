import axios, { type AxiosError, type InternalAxiosRequestConfig } from "axios";

export const BASE_URL = import.meta.env.VITE_API_BASE_URL;

export const apiClient = axios.create({
  baseURL: BASE_URL,
  withCredentials: false,
});

export const authClient = axios.create({
  baseURL: BASE_URL,
  withCredentials: true,
});

let accessTokenInMemory: string | null = null;

export function getAccessToken(): string | null {
  return accessTokenInMemory;
}

export function setAccessToken(token: string | null): void {
  accessTokenInMemory = token;
}

export function clearAccessToken(): void {
  setAccessToken(null);
}

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = getAccessToken();
  if (token) {
    config.headers.set("Authorization", `Bearer ${token}`);
  }
  return config;
});

let refreshPromise: Promise<string> | null = null;

async function doRefresh(): Promise<string> {
  const { data } = await authClient.post<{ accessToken: string }>("/auth/refresh-token", {});
  setAccessToken(data.accessToken);
  return data.accessToken;
}

function refreshSingleFlight(): Promise<string> {
  if (!refreshPromise) {
    refreshPromise = doRefresh().finally(() => {
      refreshPromise = null;
    });
  }

  return refreshPromise;
}

type NavigatorWithLocks = Navigator & {
  locks?: {
    request<T>(name: string, callback: () => Promise<T>): Promise<T>;
  };
};

export function refreshAcrossTabs(): Promise<string> {
  const locks = (navigator as NavigatorWithLocks).locks;

  if (locks) {
    return locks.request("chatapp-auth-refresh", () => refreshSingleFlight());
  }

  return refreshSingleFlight();
}

type RetryableConfig = InternalAxiosRequestConfig & {
  _retry?: boolean;
};

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as RetryableConfig | undefined;

    if (!originalRequest) {
      return Promise.reject(error);
    }

    const isAuthEndpoint =
      originalRequest.url?.includes("/auth/login") ||
      originalRequest.url?.includes("/auth/register") ||
      originalRequest.url?.includes("/auth/google-login") ||
      originalRequest.url?.includes("/auth/refresh-token");

    if (error.response?.status !== 401 || originalRequest._retry || isAuthEndpoint) {
      return Promise.reject(error);
    }

    originalRequest._retry = true;

    try {
      const newAccessToken = await refreshAcrossTabs();
      console.log("New access token obtained:", newAccessToken);
      originalRequest.headers.set("Authorization", `Bearer ${newAccessToken}`);
      return apiClient(originalRequest);
    } catch (refreshError) {
      clearAccessToken();
      window.dispatchEvent(new Event("auth:session-expired"));
      return Promise.reject(refreshError);
    }
  }
);
