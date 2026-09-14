import { test, expect, type Page } from "@playwright/test";
async function mockApi(page: Page) {
  let account = "Alice";
  let attempts = 0;
  const clientIds: string[] = [];
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = () => ({ id: account, username: account.toLowerCase(), fullName: account, isVerified: true });
  const conversation = () => ({ id: `room-${account}`, name: `Hội thoại ${account}`, type: "Direct", peerUserId: "peer", unreadCount: 0,
    createdAt: new Date().toISOString(), lastMessageAt: new Date().toISOString(), lastMessage: `Riêng của ${account}` });
  await page.route("**/hubs/**", route => route.fulfill({ status: 503, body: "offline" }));
  await page.route("https://accounts.google.com/**", route => route.abort());
  // Match backend requests only; /src/api/*.ts are Vite modules, not API responses.
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path.endsWith("/auth/logout")) return route.fulfill({ status: 204 });
    if (path.endsWith("/auth/login")) account = "Bob";
    if (path.endsWith("/auth/login") || path.endsWith("/auth/refresh-token"))
      return route.fulfill({ json: { accessToken: token, user: user(), accessTokenExpiresAt: "2100-01-01T00:00:00Z" } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user() });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [conversation()], nextCursor: null } });
    if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.endsWith("/messages") && request.method() === "GET") return route.fulfill({ json: { messages: [
      { id: `private-${account}`, conversationId: `room-${account}`, senderId: account, clientMessageId: `old-${account}`, sequence: 1,
        content: `Riêng của ${account}`, createdAt: new Date().toISOString(), status: "Sent" },
    ], hasMore: false } });
    if (path.endsWith("/messages") && request.method() === "POST") {
      const body = request.postDataJSON(); clientIds.push(body.clientMessageId);
      if (++attempts === 1) return route.fulfill({ status: 503, json: { message: "temporary failure" } });
      return route.fulfill({ json: { ...body, id: "saved-message", conversationId: `room-${account}`, senderId: account,
        sequence: 2, createdAt: new Date().toISOString(), status: "Sent" } });
    }
    if (path.includes("/conversations/")) return route.fulfill({ json: conversation() });
    return route.fulfill({ json: [] });
  });
  return clientIds;
}

test("failed message retries with the same ID and HTTP ACK confirms without realtime", async ({ page }) => {
  const ids = await mockApi(page); await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await expect(composer).toBeVisible();
  await composer.fill("Tin cần gửi lại"); await composer.press("Enter");
  await page.getByRole("button", { name: "Gửi lại", exact: true }).click();
  await expect(page.getByRole("button", { name: "Gửi lại", exact: true })).toHaveCount(0);
  const message = page.locator(".message-bubble").filter({ hasText: "Tin cần gửi lại" });
  await expect(message).toHaveCount(1);
  await message.click({ button: "right" });
  await expect(page.getByRole("button", { name: "Xóa với mọi người" })).toBeVisible();
  expect(ids).toHaveLength(2); expect(ids[0]).toBe(ids[1]);
});

test("logout and login as another user clears the prior account chat", async ({ page }) => {
  await mockApi(page); await page.goto("/");
  await expect(page.locator(".message-bubble").filter({ hasText: "Riêng của Alice" })).toBeVisible();
  await page.locator(".sidebar-avatar-btn").click();
  await page.getByRole("button", { name: "Đăng xuất" }).click();
  await page.getByLabel("Username", { exact: true }).fill("bob");
  await page.getByLabel("Mật khẩu", { exact: true }).fill("test-password");
  await page.getByRole("button", { name: "Đăng nhập", exact: true }).click();
  await expect(page.getByPlaceholder("Nhắn tin tới Hội thoại Bob")).toBeVisible();
  await expect(page.locator(".message-bubble").filter({ hasText: "Riêng của Bob" })).toBeVisible();
  await expect(page.getByText("Riêng của Alice", { exact: true })).toHaveCount(0);
});
