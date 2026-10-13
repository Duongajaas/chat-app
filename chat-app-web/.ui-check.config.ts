import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e", workers: 1, timeout: 30000,
  use: { baseURL: "http://127.0.0.1:4173", headless: true, channel: "chrome" },
  webServer: {
    command: '"C:/Users/Admin/AppData/Local/Programs/Microsoft VS Code/Code.exe" node_modules/vite/bin/vite.js --host 127.0.0.1 --port 4173 --strictPort',
    url: "http://127.0.0.1:4173", reuseExistingServer: false,
    env: { ELECTRON_RUN_AS_NODE: "1", VITE_API_BASE_URL: "/api", VITE_SIGNALR_HUB_URL: "/hubs/chat", VITE_GOOGLE_CLIENT_ID: "" },
  },
});
