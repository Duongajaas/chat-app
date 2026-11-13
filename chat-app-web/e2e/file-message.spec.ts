import { test, expect, type Page } from "@playwright/test";

const pdf = Buffer.from("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

async function mockApi(page: Page, options: { failDownload?: boolean } = {}) {
  const sent: Record<string, unknown>[] = [];
  const uploads: string[] = [];
  const downloads: string[] = [];
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "Alice", username: "alice", fullName: "Alice", isVerified: true };
  const conversation = { id: "room", name: "Phòng tài liệu", type: "Direct", peerUserId: "peer", unreadCount: 0,
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
      uploads.push(request.postDataBuffer()?.toString("latin1").match(/filename="([^"]+)"/)?.[1] ?? "");
      return route.fulfill({ json: { assetId: "22222222-2222-2222-2222-222222222222", url: "/api/media/assets/22222222-2222-2222-2222-222222222222",
        fileName: "Bao cao quy 3.pdf", contentType: "application/pdf" } });
    }
    if (/\/messages\/[^/]+\/attachments\/[^/]+$/.test(path)) {
      downloads.push(path);
      if (options.failDownload) return route.fulfill({ status: 404, json: { message: "Media không khả dụng." } });
      return route.fulfill({ body: pdf, contentType: "application/pdf" });
    }
    if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.endsWith("/messages") && request.method() === "GET") return route.fulfill({ json: { messages: [
      { id: "old-file", conversationId: "room", senderId: "peer", clientMessageId: "old", sequence: 1, content: "", type: "File",
        attachments: [{ id: "att-old", fileName: "Hop dong.docx", fileType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileSize: 245760 }],
        createdAt: new Date().toISOString(), status: "Sent" },
    ], hasMore: false } });
    if (path.endsWith("/messages") && request.method() === "POST") {
      const body = request.postDataJSON(); sent.push(body);
      return route.fulfill({ json: { id: "new-file", conversationId: "room", senderId: "Alice", clientMessageId: body.clientMessageId,
        sequence: 2, content: "", type: "File", attachments: [{ id: "att-new", fileName: "Bao cao quy 3.pdf", fileType: "application/pdf", fileSize: pdf.length }],
        createdAt: new Date().toISOString(), status: "Sent" } });
    }
    if (path.includes("/conversations/")) return route.fulfill({ json: conversation });
    return route.fulfill({ json: [] });
  });
  return { sent, uploads, downloads };
}

test("received files show a card and download only when clicked", async ({ page }) => {
  const { downloads } = await mockApi(page); await page.goto("/");
  const card = page.locator("#message-old-file .file-card");
  await expect(card).toContainText("Hop dong.docx");
  await expect(card).toContainText("240 KB");
  await expect(card.locator(".file-card__badge")).toHaveText("DOCX");
  expect(downloads).toHaveLength(0); // documents are not fetched until the user asks
  const download = page.waitForEvent("download");
  await card.click();
  expect((await download).suggestedFilename()).toBe("Hop dong.docx");
  expect(downloads).toHaveLength(1);
});

test("attaching a document uploads it, sends the private assetId and shows a downloadable card", async ({ page }) => {
  const { sent, uploads } = await mockApi(page); await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Phòng tài liệu")).toBeVisible();
  const chooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Gửi file" }).click();
  await (await chooser).setFiles({ name: "Bao cao quy 3.pdf", mimeType: "application/pdf", buffer: pdf });
  await expect.poll(() => sent.length).toBe(1);
  expect(uploads).toEqual(["Bao cao quy 3.pdf"]);
  expect(sent[0]).toMatchObject({ assetId: "22222222-2222-2222-2222-222222222222", content: "" });
  const card = page.locator("#message-new-file .file-card");
  await expect(card).toContainText("Bao cao quy 3.pdf");
  await expect(card.locator(".file-card__badge")).toHaveText("PDF");
  const download = page.waitForEvent("download");
  await card.click();
  expect((await download).suggestedFilename()).toBe("Bao cao quy 3.pdf");
});

test("unsupported and oversized files are rejected before upload", async ({ page }) => {
  const { uploads } = await mockApi(page); await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Phòng tài liệu")).toBeVisible();
  const input = page.locator("input[type=file][accept*='.pdf']");
  await input.setInputFiles({ name: "setup.exe", mimeType: "application/octet-stream", buffer: Buffer.from("MZ") });
  await expect(page.getByRole("alert").filter({ hasText: "Chỉ hỗ trợ PDF, Word, Excel" })).toBeVisible();
  await input.setInputFiles({ name: "big.pdf", mimeType: "application/pdf", buffer: Buffer.alloc(8 * 1024 * 1024 + 1) });
  await expect(page.getByRole("alert").filter({ hasText: "8 MB" })).toBeVisible();
  expect(uploads).toHaveLength(0);
});

test("a failed download explains the problem and can be retried", async ({ page }) => {
  await mockApi(page, { failDownload: true }); await page.goto("/");
  await page.locator("#message-old-file .file-card").click();
  await expect(page.locator("#message-old-file").getByRole("alert")).toContainText("Không tải được file");
  await expect(page.locator("#message-old-file .file-card")).toBeEnabled();
});
