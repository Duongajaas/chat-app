import { test, expect, type Page } from "@playwright/test";

async function setup(page: Page) {
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const me = { id: "me", username: "me", fullName: "Tôi", isVerified: true };
  const group = { id: "group", name: "Nhóm UI", type: "Group", permissions: ["PinMessage"], memberCount: 3, unreadCount: 0, version: 1, createdAt: "2026-01-01", lastMessageAt: "2026-10-01" };
  const direct = { ...group, id: "direct", name: "Duy", type: "Direct", peerUserId: "an", lastMessageAt: "2026-09-01" };
  const messages = [
    { id: "m1", senderId: "an", senderName: "Nguyễn An", senderUsername: "an", content: "Kế hoạch tuần mới", sequence: 1 },
    { id: "m2", senderId: "me", senderName: "Tôi", senderUsername: "me", content: "Nội dung của tôi", sequence: 2 },
    { id: "m3", senderId: "former", senderName: "Thành viên cũ", senderUsername: "former", content: "Tài liệu đã được chia sẻ trước đây rất dài để kiểm tra rút gọn và không tràn màn hình", sequence: 3 },
  ].map(m => ({ ...m, conversationId: "group", createdAt: "2026-10-01T10:00:00Z", status: "Sent" }));
  let pins = messages.map((message, i) => ({ id: `pin-${i}`, createdAt: `2026-10-01T10:0${i}:00Z`, message }));
  let failUnpin = true; const removed: string[] = []; const sent: any[] = [];
  await page.route("**/hubs/**", r => r.fulfill({ status: 503 }));
  await page.route("https://accounts.google.com/**", r => r.abort());
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (path.endsWith("/auth/refresh-token")) return route.fulfill({ json: { accessToken: token, user: me } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: me });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [group, direct], nextCursor: null } });
    if (path.endsWith("/members")) return route.fulfill({ json: [
      { userId: "me", username: "me", fullName: "Tôi", role: "Member" },
      { userId: "an", username: "an", fullName: "Nguyễn An", role: "Member" },
    ] });
    if (path.endsWith("/pins")) return route.fulfill({ json: path.includes("/group/") ? pins : [] });
    if (path.includes("/pins/") && req.method() === "DELETE") {
      removed.push(path.split("/").pop()!);
      if (failUnpin) { failUnpin = false; return route.fulfill({ status: 503, json: { message: "Thử lại sau" } }); }
      pins = pins.filter(p => p.message.id !== removed.at(-1));
      return route.fulfill({ status: 204 });
    }
    if (path.endsWith("/messages/batch")) return route.fulfill({ json: messages.filter(m => req.postDataJSON().ids.includes(m.id)) });
    if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.endsWith("/messages") && req.method() === "POST") {
      const body = req.postDataJSON(); sent.push(body);
      return route.fulfill({ json: { ...body, id: "sent", senderId: "me", conversationId: path.includes("/group/") ? "group" : "direct", sequence: 4, status: "Sent", createdAt: "2026-10-01" } });
    }
    if (path.endsWith("/messages")) return route.fulfill({ json: { messages: path.includes("/group/") ? messages : [{ ...messages[0], id: "dm1", conversationId: "direct" }], hasMore: false } });
    if (path.endsWith("/conversations/group")) return route.fulfill({ json: group });
    if (path.endsWith("/conversations/direct")) return route.fulfill({ json: direct });
    return route.fulfill({ json: [] });
  });
  await page.goto("/");
  await expect(page.getByPlaceholder("Nhắn tin tới Nhóm UI")).toBeVisible();
  return { sent, removed };
}
async function reply(page: Page, id: string) {
  const bubble = page.locator(`#message-${id} .message-bubble`);
  await bubble.click({ button: "right" });
  await bubble.getByRole("button", { name: "Trả lời", exact: true }).click();
}

test("pin dropdown sorts newest first, jumps, keeps failed unpin and uses message ID", async ({ page }) => {
  const { removed } = await setup(page);
  await expect(page.locator(".pins-bar__summary")).toContainText("Thành viên cũ");
  await page.getByRole("button", { name: "+2 ghim", exact: true }).click();
  await expect(page.locator(".pins-bar__item-main").first()).toContainText("Thành viên cũ");
  await page.locator(".pins-bar__item-main").last().click();
  await expect(page.locator(".pins-bar__dropdown")).toHaveCount(0);
  await expect(page.locator("#message-m1")).toHaveClass(/message-reference-highlight/);
  await page.getByRole("button", { name: "+2 ghim", exact: true }).click();
  await page.getByRole("button", { name: "Tùy chọn ghim của Nguyễn An" }).click();
  await page.getByRole("button", { name: "Bỏ ghim", exact: true }).click();
  await expect(page.locator(".pins-bar__error")).toContainText("Thử lại sau");
  await expect(page.locator(".pins-bar__item")).toHaveCount(3);
  await page.getByRole("button", { name: "Bỏ ghim", exact: true }).click();
  await expect(page.locator(".pins-bar__item")).toHaveCount(2);
  expect(removed).toEqual(["m1", "m1"]);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Danh sách tin nhắn ghim", exact: true })).toBeFocused();
});

test("reply preserves draft, only mentions active other group members, and sends quote independently", async ({ page }) => {
  const { sent } = await setup(page);
  const input = page.getByPlaceholder("Nhắn tin tới Nhóm UI");
  await input.fill("bản nháp"); await reply(page, "m1");
  await expect(input).toHaveValue("@an bản nháp"); await expect(input).toBeFocused();
  await expect(page.locator(".replying-to-bar")).toContainText("Trả lời Nguyễn An");
  await reply(page, "m1"); await expect(input).toHaveValue("@an bản nháp");
  await page.getByRole("button", { name: "Hủy trả lời" }).click();
  await expect(input).toHaveValue("@an bản nháp");
  await input.fill("nháp"); await reply(page, "m2"); await expect(input).toHaveValue("nháp");
  await reply(page, "m3"); await expect(input).toHaveValue("nháp");
  await reply(page, "m1"); await input.fill("không nhắc tên"); await input.press("Enter");
  await expect.poll(() => sent.length).toBe(1);
  expect(sent[0].replyToMessageId).toBe("m1"); expect(sent[0].mentions).toEqual([]);
  await reply(page, "m1");
  await page.locator(".conversation-item").filter({ hasText: "Duy" }).click();
  await expect(page.locator(".replying-to-bar")).toHaveCount(0);
  const direct = page.getByPlaceholder("Nhắn tin tới Duy");
  await direct.fill("tin riêng"); await reply(page, "dm1");
  await expect(direct).toHaveValue("tin riêng");
});

test("pin and reply layout fits mobile and dark mode", async ({ page }) => {
  await setup(page); await reply(page, "m1");
  await page.locator(".conversation-item").filter({ hasText: "Nhóm UI" }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "+2 ghim", exact: true }).click();
  await expect(page.locator(".pins-bar__dropdown")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "test-results/pins-reply-mobile.png" });
  await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
  await page.screenshot({ path: "test-results/pins-reply-mobile-dark.png" });
  await page.getByRole("button", { name: "Thu gọn", exact: true }).click();
  await expect(page.locator(".pins-bar__dropdown")).toHaveCount(0);
});
