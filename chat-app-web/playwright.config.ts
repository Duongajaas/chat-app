import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e", workers: 1, timeout: 30000,
  use: { baseURL: "http://127.0.0.1:4173", headless: true, channel: process.env.PLAYWRIGHT_CHANNEL },
  webServer: {
    command: "node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 4173 --strictPort",
    url: "http://127.0.0.1:4173", reuseExistingServer: false,
    env: { VITE_API_BASE_URL: "/api", VITE_SIGNALR_HUB_URL: "/hubs/chat", VITE_GOOGLE_CLIENT_ID: "", VITE_GIPHY_API_KEY: "giphy-test-key" },
  },
});
