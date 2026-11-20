import { test, expect, type Page } from "@playwright/test";

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==", "base64");
const pdf = Buffer.from("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");
const image = (name: string) => ({ name, mimeType: "image/png", buffer: png });
const doc = (name: string) => ({ name, mimeType: "application/pdf", buffer: pdf });

interface Options { failUpload?: Record<number, number>; }

async function mockApi(page: Page, options: Options = {}) {
  const sent: { assetId: string; replyToMessageId?: string | null; clientMessageId: string }[] = [];
  const uploads: string[] = [];
  const assets = new Map<string, { name: string; type: string }>();
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "Alice", username: "alice", fullName: "Alice", isVerified: true };
  const conversation = { id: "room", name: "Phòng nhiều tệp", type: "Direct", peerUserId: "peer", unreadCount: 0,
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
      const name = request.postDataBuffer()?.toString("latin1").match(/filename="([^"]+)"/)?.[1] ?? "";
      const attempt = uploads.push(name);
      const failure = options.failUpload?.[attempt];
      if (failure) return route.fulfill({ status: failure, json: { message: failure === 429 ? "Bạn thao tác quá nhiều lần." : "Máy chủ gặp lỗi." } });
      const assetId = `00000000-0000-0000-0000-${String(attempt).padStart(12, "0")}`;
      const type = name.endsWith(".png") ? "image/png" : "application/pdf";
      assets.set(assetId, { name, type });
      return route.fulfill({ json: { assetId, url: `/api/media/assets/${assetId}`, fileName: name, contentType: type } });
    }
    if (/\/messages\/[^/]+\/attachments\/[^/]+$/.test(path)) return route.fulfill({ body: png, contentType: "image/png" });
    if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.endsWith("/messages") && request.method() === "GET") return route.fulfill({ json: { messages: [
      { id: "peer-text", conversationId: "room", senderId: "peer", clientMessageId: "old", sequence: 1, content: "Gửi mình các tệp nhé",
        createdAt: new Date().toISOString(), status: "Sent" },
    ], hasMore: false } });
    if (path.endsWith("/messages") && request.method() === "POST") {
      const body = request.postDataJSON(); sent.push(body);
      const asset = assets.get(body.assetId) ?? { name: "?", type: "application/pdf" };
      return route.fulfill({ json: { id: `msg-${sent.length}`, conversationId: "room", senderId: "Alice", clientMessageId: body.clientMessageId,
        sequence: sent.length + 1, content: "", type: "File", replyToMessageId: body.replyToMessageId ?? null,
        attachments: [{ id: `att-${sent.length}`, fileName: asset.name, fileType: asset.type, fileSize: 100 }],
        createdAt: new Date().toISOString(), status: "Sent" } });
    }
    if (path.includes("/conversations/")) return route.fulfill({ json: conversation });
    return route.fulfill({ json: [] });
  });
  return { sent, uploads };
}

async function open(page: Page) {
  await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Phòng nhiều tệp")).toBeVisible();
}
async function pick(page: Page, button: string, files: { name: string; mimeType: string; buffer: Buffer }[]) {
  const chooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: button, exact: true }).click();
  const target = await chooser;
  expect(target.isMultiple()).toBe(true);
  await target.setFiles(files);
}

test("several images picked at once are sent as separate messages in the order chosen", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await open(page);
  await pick(page, "Gửi ảnh", [image("c.png"), image("a.png"), image("b.png")]);
  await expect.poll(() => sent.length).toBe(3);
  expect(uploads).toEqual(["c.png", "a.png", "b.png"]);
  expect(sent.map(m => m.assetId)).toEqual(["000000000001", "000000000002", "000000000003"].map(n => `00000000-0000-0000-0000-${n}`));
  expect(new Set(sent.map(m => m.clientMessageId)).size).toBe(3);
  for (const n of [1, 2, 3]) await expect(page.locator(`#message-msg-${n} img.private-attachment__image`)).toBeVisible();
});

test("several documents picked at once each get their own downloadable card", async ({ page }) => {
  const { sent } = await mockApi(page); await open(page);
  await pick(page, "Gửi file", [doc("Hop dong.pdf"), doc("Bao cao.pdf"), image("anh.png")]);
  await expect.poll(() => sent.length).toBe(3);
  await expect(page.locator("#message-msg-1 .file-card")).toContainText("Hop dong.pdf");
  await expect(page.locator("#message-msg-2 .file-card")).toContainText("Bao cao.pdf");
  await expect(page.locator("#message-msg-3 img.private-attachment__image")).toBeVisible();
});

test("a quoted reply is attached to the first message only", async ({ page }) => {
  const { sent } = await mockApi(page); await open(page);
  const bubble = page.locator("#message-peer-text .message-bubble");
  await bubble.hover();
  await bubble.getByRole("button", { name: "Trả lời", exact: true }).click();
  await pick(page, "Gửi file", [doc("a.pdf"), doc("b.pdf")]);
  await expect.poll(() => sent.length).toBe(2);
  expect(sent[0].replyToMessageId).toBe("peer-text");
  expect(sent[1].replyToMessageId ?? null).toBeNull();
});

test("unsupported files are skipped with a clear message while the valid ones still send", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await open(page);
  await pick(page, "Gửi file", [doc("ok.pdf"), { name: "setup.exe", mimeType: "application/octet-stream", buffer: Buffer.from("MZ") }, doc("ok2.pdf")]);
  await expect.poll(() => sent.length).toBe(2);
  expect(uploads).toEqual(["ok.pdf", "ok2.pdf"]);
  await expect(page.getByRole("alert").filter({ hasText: "Đã gửi 2/3" })).toContainText("setup.exe (định dạng không được hỗ trợ)");
});

test("more than ten files in one pick sends ten and explains the rest", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await open(page);
  await pick(page, "Gửi ảnh", Array.from({ length: 12 }, (_, i) => image(`p${i}.png`)));
  await expect.poll(() => sent.length).toBe(10);
  expect(uploads).toHaveLength(10);
  await expect(page.getByRole("alert").filter({ hasText: "Đã gửi 10/12" })).toContainText("vượt quá 10 mục mỗi lần");
});

test("one failed upload does not stop the others", async ({ page }) => {
  const { sent } = await mockApi(page, { failUpload: { 2: 500 } }); await open(page);
  await pick(page, "Gửi file", [doc("one.pdf"), doc("two.pdf"), doc("three.pdf")]);
  await expect.poll(() => sent.length).toBe(2);
  await expect(page.getByRole("alert").filter({ hasText: "Đã gửi 2/3" })).toContainText("two.pdf");
});

test("hitting the upload rate limit stops the batch and says how many were left", async ({ page }) => {
  const { sent, uploads } = await mockApi(page, { failUpload: { 2: 429 } }); await open(page);
  await pick(page, "Gửi file", [doc("one.pdf"), doc("two.pdf"), doc("three.pdf"), doc("four.pdf")]);
  await expect.poll(() => sent.length).toBe(1);
  await expect(page.getByRole("alert").filter({ hasText: "Đã gửi 1/4" })).toContainText("3 mục còn lại chưa được gửi");
  expect(uploads).toHaveLength(2); // stopped hammering the server after the 429
});

test("a single file still works exactly as before", async ({ page }) => {
  const { sent } = await mockApi(page); await open(page);
  await pick(page, "Gửi file", [doc("only.pdf")]);
  await expect.poll(() => sent.length).toBe(1);
  await expect(page.locator("#message-msg-1 .file-card")).toContainText("only.pdf");
  await expect(page.getByRole("alert")).toHaveCount(0);
});
