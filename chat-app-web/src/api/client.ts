import axios, { type AxiosError, type InternalAxiosRequestConfig } from "axios";

export const BASE_URL = import.meta.env.VITE_API_BASE_URL || "/api";
export const apiClient = axios.create({ baseURL: BASE_URL, withCredentials: false, timeout: 20000 });
export const authClient = axios.create({ baseURL: BASE_URL, withCredentials: true, timeout: 20000 });
let accessTokenInMemory: string | null = null;
let generation = 0;
let controller = new AbortController();
let refreshPromise: Promise<string> | null = null;
export const getSessionVersion = () => generation;
export const getAccessToken = () => accessTokenInMemory;
export function setAccessToken(token: string | null) { accessTokenInMemory = token; }
export function clearAccessToken() {
  generation++;
  controller.abort();
  controller = new AbortController();
  accessTokenInMemory = null;
  refreshPromise = null;
}

type SessionConfig = InternalAxiosRequestConfig & { sessionVersion?: number; _retry?: boolean };
function prepare(config: SessionConfig) {
  config.sessionVersion ??= generation;
  if (config.sessionVersion !== generation) throw new axios.CanceledError("Session changed");
  config.signal ??= controller.signal;
  return config;
}
apiClient.interceptors.request.use(config => {
  prepare(config);
  if (accessTokenInMemory) config.headers.set("Authorization", `Bearer ${accessTokenInMemory}`);
  return config;
});
authClient.interceptors.request.use(prepare);
const ensureCurrent = <T extends { config: InternalAxiosRequestConfig }>(response: T): T => {
  if ((response.config as SessionConfig).sessionVersion !== generation) throw new axios.CanceledError("Session changed");
  return response;
};
authClient.interceptors.response.use(ensureCurrent);

export function refreshAcrossTabs(): Promise<string> {
  if (refreshPromise) return refreshPromise;
  const version = generation;
  async function refresh() {
    if (version !== generation) throw new axios.CanceledError("Session changed");
    const { data } = await authClient.post<{ accessToken: string }>("/auth/refresh-token", {});
    if (version !== generation) throw new axios.CanceledError("Session changed");
    setAccessToken(data.accessToken);
    return data.accessToken;
  }
  const promise = Promise.resolve(navigator.locks ? navigator.locks.request("chatapp-auth-refresh", refresh) : refresh()).then(value => value)
    .finally(() => { if (refreshPromise === promise) refreshPromise = null; });
  refreshPromise = promise;
  return promise;
}

export async function getValidAccessToken() {
  const token = getAccessToken();
  if (token) {
    try {
      const payload = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
      if (JSON.parse(atob(payload)).exp * 1000 > Date.now() + 30000) return token;
    } catch { /* Refresh a malformed/expired cached token. */ }
  }
  return refreshAcrossTabs();
}

apiClient.interceptors.response.use(ensureCurrent, async (error: AxiosError) => {
  const config = error.config as SessionConfig | undefined;
  if (!config || config.sessionVersion !== generation || error.response?.status !== 401 || config._retry)
    return Promise.reject(error);
  config._retry = true;
  const version = generation;
  try {
    const token = await refreshAcrossTabs();
    if (version !== generation) throw new axios.CanceledError("Session changed");
    config.headers.set("Authorization", `Bearer ${token}`);
    return apiClient(config);
  } catch (refreshError) {
    if (version === generation && axios.isAxiosError(refreshError) && [401, 403].includes(refreshError.response?.status ?? 0))
      window.dispatchEvent(new Event("auth:session-expired"));
    return Promise.reject(refreshError);
  }
});
