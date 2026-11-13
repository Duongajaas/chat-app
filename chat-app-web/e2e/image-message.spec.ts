import { test, expect, type Page } from "@playwright/test";

// 1x1 transparent PNG.
const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==", "base64");

async function mockApi(page: Page) {
  const sent: Record<string, unknown>[] = [];
  const uploads: string[] = [];
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "Alice", username: "alice", fullName: "Alice", isVerified: true };
  const conversation = { id: "room", name: "Phòng ảnh", type: "Direct", peerUserId: "peer", unreadCount: 0,
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
      uploads.push(request.headers()["content-type"] ?? "");
      return route.fulfill({ json: { assetId: "11111111-1111-1111-1111-111111111111", url: "/api/media/assets/11111111-1111-1111-1111-111111111111",
        fileName: "cat.png", contentType: "image/png" } });
    }
    if (/\/messages\/[^/]+\/attachments\/[^/]+$/.test(path)) return route.fulfill({ body: png, contentType: "image/png" });
    if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.endsWith("/messages") && request.method() === "GET") return route.fulfill({ json: { messages: [
      { id: "old-image", conversationId: "room", senderId: "peer", clientMessageId: "old", sequence: 1, content: "", type: "File",
        attachments: [{ id: "att-old", fileName: "old.png", fileType: "image/png", fileSize: png.length }],
        createdAt: new Date().toISOString(), status: "Sent" },
    ], hasMore: false } });
    if (path.endsWith("/messages") && request.method() === "POST") {
      const body = request.postDataJSON(); sent.push(body);
      return route.fulfill({ json: { id: "new-image", conversationId: "room", senderId: "Alice", clientMessageId: body.clientMessageId,
        sequence: 2, content: "", type: "File", attachments: [{ id: "att-new", fileName: "cat.png", fileType: "image/png", fileSize: png.length }],
        createdAt: new Date().toISOString(), status: "Sent" } });
    }
    if (path.includes("/conversations/")) return route.fulfill({ json: conversation });
    return route.fulfill({ json: [] });
  });
  return { sent, uploads };
}

test("received images render as an inline preview", async ({ page }) => {
  await mockApi(page); await page.goto("/");
  const preview = page.locator("#message-old-image img.private-attachment__image");
  await expect(preview).toBeVisible();
  await expect(preview).toHaveAttribute("src", /^blob:/);
});

test("picking an image uploads it, sends the private assetId and shows the preview", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Phòng ảnh")).toBeVisible();
  const chooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Gửi ảnh" }).click();
  await (await chooser).setFiles({ name: "cat.png", mimeType: "image/png", buffer: png });
  await expect.poll(() => sent.length).toBe(1);
  expect(uploads[0]).toContain("multipart/form-data");
  expect(sent[0]).toMatchObject({ assetId: "11111111-1111-1111-1111-111111111111", content: "" });
  expect(sent[0].mediaUrl ?? null).toBeNull();
  await expect(page.locator("#message-new-image img.private-attachment__image")).toBeVisible();
});

test("unsupported files are rejected before upload", async ({ page }) => {
  const { uploads } = await mockApi(page); await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Phòng ảnh")).toBeVisible();
  await page.locator("input[type=file][accept^='image/']").first().setInputFiles({ name: "note.gif", mimeType: "image/gif", buffer: Buffer.from("GIF89a") });
  await expect(page.getByRole("alert").filter({ hasText: "Chỉ hỗ trợ ảnh JPEG, PNG hoặc WebP." })).toBeVisible();
  expect(uploads).toHaveLength(0);
});
