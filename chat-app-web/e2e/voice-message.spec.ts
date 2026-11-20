import { test, expect, type Page } from "@playwright/test";

// Chromium feeds a synthetic tone as the microphone, so the real MediaRecorder runs end to end.
test.use({
  permissions: ["microphone"],
  launchOptions: { args: ["--use-fake-ui-for-media-stream", "--use-fake-device-for-media-stream"] },
});

const webmHeader = Buffer.from([0x1a, 0x45, 0xdf, 0xa3, 0x9f, 0x42, 0x86, 0x81, 0x01, 0x42, 0xf7, 0x81]);

async function mockApi(page: Page, options: { failDownload?: boolean } = {}) {
  const sent: Record<string, unknown>[] = [];
  const uploads: { name: string; header: number[] }[] = [];
  const downloads: string[] = [];
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "Alice", username: "alice", fullName: "Alice", isVerified: true };
  const conversation = { id: "room", name: "Phòng thoại", type: "Direct", peerUserId: "peer", unreadCount: 0,
    createdAt: new Date().toISOString(), lastMessageAt: new Date().toISOString(), lastMessage: "" };
  await page.route("**/hubs/**", route => route.fulfill({ status: 503, body: "offline" }));
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
  await page.route("https://accounts.google.com/**", route => route.abort());
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path.endsWith("/auth/refresh-token"))
      return route.fulfill({ json: { accessToken: token, user, accessTokenExpiresAt: "2100-01-01T00:00:00Z" } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user });
    if (path.endsWith("/pins")) return route.fulfill({ json: [] });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [conversation], nextCursor: null } });
    if (path.endsWith("/files/upload")) {
      const body = request.postDataBuffer() ?? Buffer.alloc(0);
      const text = body.toString("latin1");
      const start = text.indexOf("\r\n\r\n") + 4;
      uploads.push({ name: text.match(/filename="([^"]+)"/)?.[1] ?? "", header: [...body.subarray(start, start + 4)] });
      return route.fulfill({ json: { assetId: "33333333-3333-3333-3333-333333333333", url: "/api/media/assets/33333333-3333-3333-3333-333333333333",
        fileName: "tin-nhan-thoai.webm", contentType: "audio/webm" } });
    }
    if (/\/messages\/[^/]+\/attachments\/[^/]+$/.test(path)) {
      downloads.push(path);
      if (options.failDownload) return route.fulfill({ status: 404, json: { message: "Media không khả dụng." } });
      return route.fulfill({ body: webmHeader, contentType: "audio/webm" });
    }
    if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.endsWith("/messages") && request.method() === "GET") return route.fulfill({ json: { messages: [
      { id: "old-voice", conversationId: "room", senderId: "peer", clientMessageId: "old", sequence: 1, content: "", type: "Audio",
        voiceDuration: 7, waveformPoints: JSON.stringify(Array.from({ length: 40 }, (_, i) => 20 + (i % 6) * 12)),
        attachments: [{ id: "att-old", fileName: "tin-nhan-thoai.webm", fileType: "audio/webm", fileSize: webmHeader.length }],
        createdAt: new Date().toISOString(), status: "Sent" },
    ], hasMore: false } });
    if (path.endsWith("/messages") && request.method() === "POST") {
      const body = request.postDataJSON(); sent.push(body);
      return route.fulfill({ json: { id: "new-voice", conversationId: "room", senderId: "Alice", clientMessageId: body.clientMessageId,
        sequence: 2, content: "", type: "Audio", voiceDuration: body.voiceDuration, waveformPoints: body.waveformPoints,
        attachments: [{ id: "att-new", fileName: "tin-nhan-thoai.webm", fileType: "audio/webm", fileSize: webmHeader.length }],
        createdAt: new Date().toISOString(), status: "Sent" } });
    }
    if (path.includes("/conversations/")) return route.fulfill({ json: conversation });
    return route.fulfill({ json: [] });
  });
  return { sent, uploads, downloads };
}

const startButton = (page: Page) => page.getByRole("button", { name: "Ghi âm tin nhắn thoại", exact: true });

test("recording a voice message uploads real audio and sends duration, waveform and the private assetId", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Phòng thoại")).toBeVisible();
  await startButton(page).click();
  await expect(page.getByRole("group", { name: "Đang ghi âm tin nhắn thoại" })).toBeVisible();
  await expect(page.getByText("Đang ghi âm…")).toBeVisible();
  await page.waitForTimeout(1600);
  await page.getByRole("button", { name: "Gửi tin nhắn thoại", exact: true }).click();
  await expect.poll(() => sent.length).toBe(1);
  expect(uploads).toHaveLength(1);
  expect(uploads[0].name).toMatch(/^tin-nhan-thoai-\d+\.webm$/);
  expect(uploads[0].header).toEqual([0x1a, 0x45, 0xdf, 0xa3]); // genuine WebM, accepted by the server's signature check
  const message = sent[0] as { assetId: string; voiceDuration: number; waveformPoints: string; content: string };
  expect(message.assetId).toBe("33333333-3333-3333-3333-333333333333");
  expect(message.voiceDuration).toBeGreaterThanOrEqual(1);
  expect(message.voiceDuration).toBeLessThanOrEqual(5);
  const bars = JSON.parse(message.waveformPoints) as number[];
  expect(bars).toHaveLength(40);
  expect(bars.every(b => b >= 6 && b <= 100)).toBe(true);
  expect(message.content).toBe("");
  await expect(page.locator("#message-new-voice .voice-player")).toBeVisible();
  await expect(page.getByRole("group", { name: "Đang ghi âm tin nhắn thoại" })).toHaveCount(0);
});

test("cancelling a recording sends nothing and releases the microphone", async ({ page }) => {
  const { sent, uploads } = await mockApi(page);
  await page.addInitScript(() => {
    const original = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
    (window as unknown as { __tracks: MediaStreamTrack[] }).__tracks = [];
    navigator.mediaDevices.getUserMedia = async constraints => {
      const stream = await original(constraints);
      (window as unknown as { __tracks: MediaStreamTrack[] }).__tracks.push(...stream.getTracks());
      return stream;
    };
  });
  await page.goto("/");
  await startButton(page).click();
  await expect(page.getByText("Đang ghi âm…")).toBeVisible();
  await page.getByRole("button", { name: "Hủy ghi âm" }).click();
  await expect(page.getByRole("group", { name: "Đang ghi âm tin nhắn thoại" })).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { __tracks: MediaStreamTrack[] }).__tracks.every(t => t.readyState === "ended"))).toBe(true);
  expect(uploads).toHaveLength(0); expect(sent).toHaveLength(0);
});

test("Escape cancels the recording", async ({ page }) => {
  const { uploads } = await mockApi(page); await page.goto("/");
  await startButton(page).click();
  await expect(page.getByText("Đang ghi âm…")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("group", { name: "Đang ghi âm tin nhắn thoại" })).toHaveCount(0);
  expect(uploads).toHaveLength(0);
});

test("a recording shorter than one second is rejected with a clear message", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await page.goto("/");
  await startButton(page).click();
  await expect(page.getByText("Đang ghi âm…")).toBeVisible();
  await page.getByRole("button", { name: "Gửi tin nhắn thoại", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "quá ngắn" })).toBeVisible();
  expect(uploads).toHaveLength(0); expect(sent).toHaveLength(0);
});

test("a denied microphone explains how to allow it", async ({ page }) => {
  await mockApi(page);
  await page.addInitScript(() => {
    navigator.mediaDevices.getUserMedia = () => Promise.reject(new DOMException("denied", "NotAllowedError"));
  });
  await page.goto("/");
  await startButton(page).click();
  await expect(page.getByRole("alert").filter({ hasText: "chưa cho phép dùng micro" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Đang ghi âm tin nhắn thoại" })).toHaveCount(0);
});

test("received voice messages show duration, load only on play, and a failed load can be retried", async ({ page }) => {
  const { downloads } = await mockApi(page, { failDownload: true }); await page.goto("/");
  const player = page.locator("#message-old-voice .voice-player");
  await expect(player).toBeVisible();
  await expect(player.locator(".voice-player__time")).toHaveText("0:07");
  await expect(player.locator(".voice-player__bars span")).toHaveCount(40);
  expect(downloads).toHaveLength(0); // audio is only fetched when the user presses play
  await player.getByRole("button", { name: "Phát tin nhắn thoại" }).click();
  await expect(page.locator("#message-old-voice").getByRole("alert")).toContainText("Không tải được");
  await expect(player.getByRole("button", { name: "Phát tin nhắn thoại" })).toBeEnabled();
  await player.getByRole("button", { name: "Phát tin nhắn thoại" }).click();
  await expect.poll(() => downloads.length).toBe(2);
});
