import { beforeEach, expect, it, vi } from "vitest";
import { apiClient, authClient, clearAccessToken, getAccessToken, refreshAcrossTabs } from "../src/api/client";
import type { AxiosResponse, InternalAxiosRequestConfig } from "axios";
function response(config: InternalAxiosRequestConfig, data: unknown): AxiosResponse { return { data, status: 200, statusText: "OK", headers: {}, config }; }
beforeEach(() => {
  clearAccessToken();
  vi.stubGlobal("navigator", { locks: { request: async (_name: string, fn: () => Promise<string>) => await fn() } });
});
it("coalesces concurrent refresh calls before acquiring the cross-tab lock", async () => {
  let release!: () => void;
  const barrier = new Promise<void>(resolve => { release = resolve; });
  const adapter = vi.fn(async (config: InternalAxiosRequestConfig) => { await barrier; return response(config, { accessToken: "new-token" }); });
  authClient.defaults.adapter = adapter;
  const calls = [refreshAcrossTabs(), refreshAcrossTabs(), refreshAcrossTabs()];
  release();
  expect(await Promise.all(calls)).toEqual(["new-token", "new-token", "new-token"]);
  expect(adapter).toHaveBeenCalledTimes(1);
});
it("rejects a response from the previous session", async () => {
  let release!: () => void;
  const barrier = new Promise<void>(resolve => { release = resolve; });
  apiClient.defaults.adapter = async config => { await barrier; return response(config, { private: "alice" }); };
  const pending = apiClient.get("/users/me");
  await Promise.resolve(); await Promise.resolve();
  const assertion = expect(pending).rejects.toBeDefined();
  clearAccessToken(); release();
  await assertion;
});
it("an old refresh cannot install a token into the new session", async () => {
  let release!: () => void;
  const barrier = new Promise<void>(resolve => { release = resolve; });
  authClient.defaults.adapter = async config => { await barrier; return response(config, { accessToken: "old-token" }); };
  const pending = refreshAcrossTabs();
  await Promise.resolve(); await Promise.resolve();
  const assertion = expect(pending).rejects.toBeDefined();
  clearAccessToken(); release();
  await assertion;
  expect(getAccessToken()).toBeNull();
});
