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
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
  await page.route("https://accounts.google.com/**", route => route.abort());
  // Match backend requests only; /src/api/*.ts are Vite modules, not API responses.
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path.endsWith("/pins")) return route.fulfill({ json: [] });
    if (path.endsWith("/auth/logout")) return route.fulfill({ status: 204 });
    if (path.endsWith("/auth/login")) account = "Bob";
    if (path.endsWith("/auth/login") || path.endsWith("/auth/refresh-token"))
      return route.fulfill({ json: { accessToken: token, user: user(), accessTokenExpiresAt: "2100-01-01T00:00:00Z" } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user() });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [conversation()], nextCursor: null } });
    if (path.endsWith("/messages/reconcile")) return route.fulfill({ json: request.postDataJSON().items.map((i: { clientMessageId: string }) => ({ clientMessageId: i.clientMessageId, state: "NotFound" })) });
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

test("transient failure retries automatically with the same ID and HTTP ACK confirms without realtime", async ({ page }) => {
  const ids = await mockApi(page); await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await expect(composer).toBeVisible();
  await composer.fill("Tin cần gửi lại"); await composer.press("Enter");
  await expect.poll(() => ids.length).toBe(2);
  await expect(page.getByRole("button", { name: "Gửi lại", exact: true })).toHaveCount(0);
  const message = page.locator(".message-bubble").filter({ hasText: "Tin cần gửi lại" });
  await expect(message).toHaveCount(1);
  await message.click({ button: "right" });
  await expect(page.getByRole("button", { name: "Xóa với mọi người" })).toBeVisible();
  expect(ids).toHaveLength(2); expect(ids[0]).toBe(ids[1]);
});

test("message actions reveal on hover and keyboard; More keeps deletion actions", async ({ page }) => {
  await mockApi(page); await page.goto("/");
  const bubble = page.locator("#message-private-Alice .message-bubble");
  const bar = bubble.locator(".message-action-bar");
  await expect(bar).toHaveCSS("opacity", "0");
  await bubble.hover(); await expect(bar).toHaveCSS("opacity", "1");
  const box = await bubble.boundingBox(); const actions = await bar.boundingBox();
  expect(actions!.x + actions!.width).toBeLessThanOrEqual(box!.x);
  await bubble.getByRole("button", { name: "Thêm thao tác" }).click();
  await expect(bubble.getByRole("button", { name: "Xóa phía tôi" })).toBeVisible();
  await page.screenshot({ path: "test-results/message-actions-desktop.png", fullPage: true });
  await page.keyboard.press("Escape");
  await expect(bubble.locator(".message-menu")).toHaveCount(0);
  await page.mouse.move(0, 0);
  await bubble.focus(); await page.keyboard.press("Enter");
  await expect(bubble).toHaveClass(/has-actions/);
});

test("desktop information panel preserves draft and fits four columns", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await mockApi(page); await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Bản nháp đang viết");
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await expect(page.locator(".conversation-info")).toBeVisible();
  await expect(composer).toHaveValue("Bản nháp đang viết");
  const chat = await page.locator(".chat-window").boundingBox();
  const info = await page.locator(".conversation-info").boundingBox();
  expect(chat!.x + chat!.width).toBeLessThanOrEqual(info!.x);
  expect(info!.x + info!.width).toBeLessThanOrEqual(1440);
  await page.screenshot({ path: "test-results/refactor-desktop-light.png" });
  await page.evaluate(() => { document.documentElement.dataset.theme = "dark"; });
  await page.screenshot({ path: "test-results/refactor-desktop-dark.png" });
  await page.keyboard.press("Escape");
  await expect(page.locator(".conversation-info")).toHaveCount(0);
  await expect(composer).toHaveValue("Bản nháp đang viết");
});

test.describe("touch message actions", () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 390, height: 844 } });
  test("chat hides bottom navigation and conversation info opens full screen", async ({ page }) => {
    await mockApi(page); await page.goto("/");
    await expect(page.locator(".mobile-nav")).toBeVisible();
    await page.locator(".conversation-item").click();
    await expect(page.locator(".mobile-nav")).toBeHidden();
    await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
    await expect(page.locator(".conversation-info")).toBeVisible();
    await page.evaluate(() => { document.documentElement.dataset.theme = "dark"; });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await page.screenshot({ path: "test-results/refactor-mobile-info.png" });
    await page.getByRole("button", { name: "Đóng thông tin hội thoại" }).click();
    const composerBox = await page.locator(".chat-window__composer").boundingBox();
    expect(composerBox!.y + composerBox!.height).toBeLessThanOrEqual(844);
    expect(composerBox!.y).toBeGreaterThan(0);
    await page.screenshot({ path: "test-results/refactor-mobile-chat.png" });
    await page.getByRole("button", { name: "Quay lại", exact: true }).click();
    await expect(page.locator(".mobile-nav")).toBeVisible();
  });
  test("tap does not open; hold opens; movement cancels", async ({ page }) => {
    await mockApi(page); await page.goto("/");
    await page.locator(".conversation-item").click();
    const bubble = page.locator("#message-private-Alice .message-bubble");
    await bubble.tap();
    await expect(bubble.locator(".message-action-bar")).toBeHidden();
    await page.clock.install();
    await bubble.dispatchEvent("pointerdown", { pointerType: "touch", isPrimary: true });
    await page.clock.fastForward(460);
    await bubble.dispatchEvent("pointerup", { pointerType: "touch", isPrimary: true });
    await expect(bubble.locator(".message-action-bar")).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: "test-results/message-actions-mobile.png", fullPage: true });
    await page.getByPlaceholder("Nhắn tin tới Hội thoại Alice").tap();
    await expect(bubble).not.toHaveClass(/has-actions/);
    await bubble.dispatchEvent("pointerdown", { pointerType: "touch", isPrimary: true });
    await bubble.dispatchEvent("pointermove", { pointerType: "touch", isPrimary: true });
    await page.clock.fastForward(500);
    await expect(bubble.locator(".message-action-bar")).toBeHidden();
  });
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


test("lost HTTP acknowledgement is reconciled without a second send", async ({ page }) => {
  await mockApi(page);
  let attempts = 0;
  let saved: Record<string, unknown> | null = null;
  await page.route("**/api/conversations/room-Alice/messages", async route => {
    if (route.request().method() !== "POST") return route.fallback();
    attempts++;
    saved = { ...route.request().postDataJSON(), id: "reconciled", conversationId: "room-Alice", senderId: "Alice",
      sequence: 2, createdAt: new Date().toISOString(), status: "Sent" };
    return route.abort("failed");
  });
  await page.route("**/api/conversations/room-Alice/messages/reconcile", route => route.fulfill({
    json: [{ clientMessageId: saved!.clientMessageId, state: "Readable", messageId: "reconciled", message: saved }] }));
  await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("ACK bị mất"); await composer.press("Enter");
  await expect(page.locator("#message-reconciled")).toContainText("ACK bị mất");
  expect(attempts).toBe(1);
  await expect(page.locator(".message-bubble").filter({ hasText: "ACK bị mất" })).toHaveCount(1);
});

test("cooldown error shows the remaining wait time in chat", async ({ page }) => {
  await mockApi(page);
  await page.route("**/api/blocks/peer", route => route.fulfill({ status: 409, json: {
    code: "REBLOCK_COOLDOWN", remainingSeconds: 125, reblockAllowedAt: new Date(Date.now() + 125000).toISOString(),
    message: "Bạn cần chờ thêm 2 phút 5 giây mới được chặn lại." } }));
  page.on("dialog", dialog => dialog.accept());
  await page.goto("/");
  await page.getByRole("button", { name: "Tùy chọn", exact: true }).click();
  await page.getByRole("button", { name: "Chặn người này", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("2 phút 5 giây");
});


test("chat remains interactive after sending and after HTTP confirmation", async ({ page }) => {
  await mockApi(page); await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Kiểm tra giao diện sau khi gửi");
  await composer.press("Enter");
  await expect(composer).toHaveValue("");
  await composer.fill("Tôi vẫn nhập được");
  await expect(composer).toHaveValue("Tôi vẫn nhập được");
  await expect(page.locator(".message-bubble").filter({ hasText: "Kiểm tra giao diện sau khi gửi" })).toBeVisible();
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await expect(page.locator(".conversation-info")).toBeVisible();
});

test("send remains responsive with a long conversation", async ({ page }) => {
  await mockApi(page);
  await page.route(url => url.pathname === "/api/conversations/room-Alice/messages", route => route.request().method() === "GET"
    ? route.fulfill({ json: { messages: Array.from({ length: 1000 }, (_, i) => ({ id: `old-${i}`, conversationId: "room-Alice", senderId: "Alice", sequence: i + 1, content: `Tin ${i}`, createdAt: "2026-09-01T00:00:00Z", status: "Sent" })), hasMore: false } })
    : route.fallback());
  await page.goto("/");
  await expect(page.locator(".message-bubble")).toHaveCount(120);
  await expect(page.getByRole("button", { name: "Xem thêm tin nhắn đã tải" })).toBeVisible();
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Tin mới");
  const started = Date.now();
  await composer.press("Enter");
  await composer.fill("Vẫn tương tác được");
  expect(Date.now() - started).toBeLessThan(3000);
  await expect(composer).toHaveValue("Vẫn tương tác được");
  await page.getByRole("button", { name: "Xem thêm tin nhắn đã tải" }).click();
  await expect(page.locator(".message-bubble")).toHaveCount(240);
});



