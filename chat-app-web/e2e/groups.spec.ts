import { test, expect, type Page } from "@playwright/test";

async function groupApi(page: Page, role: "Owner" | "Member" = "Owner") {
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "owner", username: "owner", fullName: "Owner", isVerified: true };
  let version = 1;
  let avatarUrl: string | undefined;
  let pinned = false;
  const sentBodies: Record<string, unknown>[] = [];
  const avatarBodies: Record<string, unknown>[] = [];
  const oldMessage = { id: "old", conversationId: "group", senderId: "friend", sequence: 1,
    content: "Lịch sử được phép", createdAt: "2026-01-01", status: "Sent" };

  let left = false;
  const members = [
    { userId: "owner", username: "owner", fullName: "Owner", role, joinedAt: "2026-01-01", canKick: false, canChangeRole: false },
    { userId: "friend", username: "an", fullName: "Nguyễn An", role: "Member", joinedAt: "2026-01-02", canKick: role === "Owner", canChangeRole: role === "Owner" },
  ];
  const group = () => ({
    id: "group", name: "Nhóm kiểm thử", type: "Group", role: left ? null : role, hasLeft: left, isHidden: left,
    permissions: left ? [] : role === "Owner" ? ["RenameGroup", "AddMember", "ManageRoles", "ManageInvites", "ChangeAvatar", "PinMessage"] : ["AddMember", "ChangeAvatar", "PinMessage"],
    avatarUrl, memberCount: 2, version, unreadCount: 0, createdAt: "2026-01-01", lastMessageAt: "2026-01-02",
  });
  await page.route("**/hubs/**", r => r.fulfill({ status: 503 }));
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
  await page.route("https://accounts.google.com/**", r => r.abort());
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (path.endsWith("/pins")) return route.fulfill({ json: pinned ? [{ id: "pin", message: oldMessage }] : [] });
    if (path.endsWith("/pins/old")) { pinned = req.method() === "PUT"; return route.fulfill({ status: 204 }); }
    if (path.endsWith("/avatar/upload-intent")) return route.fulfill({ json: { assetId: "internal-asset", uploadUrl: "https://avatar.test/upload", fields: { signature: "signed" } } });
    if (path.endsWith("/avatar") && req.method() === "PUT") {
      avatarBodies.push(req.postDataJSON()); avatarUrl = "https://avatar.test/image.png"; version++;
      return route.fulfill({ json: group() });
    }
    if (path.endsWith("/messages/batch")) return route.fulfill({ json: [oldMessage] });
    if (path.endsWith("/messages") && req.method() === "POST") {
      const body = req.postDataJSON(); sentBodies.push(body);
      return route.fulfill({ json: { ...body, id: "sent", conversationId: "group", senderId: "owner", sequence: 3, createdAt: "2026-09-14", status: "Sent",
        mentions: body.mentions?.map((m: object) => ({ ...m, username: "an" })) ?? [] } });
    }
    if (path.endsWith("/auth/refresh-token")) return route.fulfill({ json: { accessToken: token, user, accessTokenExpiresAt: "2100-01-01" } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user });
    if (path.endsWith("/friends")) return route.fulfill({ json: [] });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [group()], nextCursor: null } });
    if (path.endsWith("/conversations/group")) return route.fulfill({ json: group() });
    if (path.endsWith("/message-states")) return route.fulfill({ json: [{ id: "old", hidden: false, deleted: false }] });
    if (path.endsWith("/messages")) return route.fulfill({ json: { messages: [
      oldMessage,
      { id: "reply", conversationId: "group", senderId: "friend", sequence: 2, content: "Trả lời tin cũ", createdAt: "2026-09-14",
        replyToMessageId: "unreadable", replyPreview: { id: "unreadable", isAvailable: false, contentSnippet: null } },

    ], hasMore: false } });
    if (path.endsWith("/members")) return route.fulfill({ json: members });
    if (path.endsWith("/role")) {
      expect(req.postDataJSON().role).toBe("Admin");
      members[1].role = "Admin"; version++;
      return route.fulfill({ status: 204 });
    }
    if (path.endsWith("/leave")) { left = true; version++; return route.fulfill({ status: 204 }); }
    if (path.endsWith("/invites")) {
      if (req.method() === "POST") {
        expect(req.headers()["idempotency-key"]).toBeTruthy();
        return route.fulfill({ json: { id: "invite", code: "secret-invite", maxUses: 100, usedCount: 0, expiresAt: "2026-10-01" } });
      }
      return route.fulfill({ json: [] });
    }
    return route.fulfill({ json: {} });
  });
  await page.route("https://avatar.test/**", r => r.fulfill({ json: { secure_url: "https://untrusted.test/ignore" } }));
  return { sentBodies, avatarBodies };
}

test("owner manages roles and receives invite code only in create response", async ({ page }) => {
  await groupApi(page); await page.goto("/");
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  await expect(page.getByRole("button", { name: "Cấp Admin" })).toBeVisible();
  await page.getByRole("button", { name: "Cấp Admin" }).click();
  await expect(page.getByRole("button", { name: "Hạ Admin" })).toBeVisible();
  await page.getByRole("button", { name: "Tạo link mời" }).click();
  await expect(page.getByLabel("Sao chép và lưu link ngay")).toHaveValue(/\/join#code=secret-invite$/);
  await page.locator(".group-settings--details").evaluate(el => { el.scrollTop = 0; });
  await page.screenshot({ path: "test-results/group-design-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole("dialog", { name: "Thông tin nhóm", exact: true })).toBeVisible();
  await expect(page.locator(".mobile-nav")).toBeVisible();
  await page.locator(".group-settings--details").evaluate(el => { el.scrollTop = 0; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "test-results/group-design-mobile.png", fullPage: true });
});

test("member has no admin actions and leaving preserves read-only history", async ({ page }) => {
  await groupApi(page, "Member"); await page.goto("/");
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  await expect(page.locator(".group-settings__member").filter({ hasText: "Nguyễn An" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Cấp Admin" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Tạo link mời" })).toHaveCount(0);
  page.on("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Rời nhóm", exact: true }).click();
  await page.getByRole("dialog", { name: "Rời nhóm?", exact: true }).getByRole("button", { name: "Xác nhận", exact: true }).click();
  await expect(page.getByPlaceholder("Nhắn tin tới Nhóm kiểm thử")).toHaveCount(0);
  await page.getByLabel("Bộ lọc khác").click();
  await page.getByRole("button", { name: "Nhóm đã rời" }).click();
  await expect(page.locator(".conversation-item__name").filter({ hasText: "Nhóm kiểm thử" })).toBeVisible();
  await expect(page.locator(".message-bubble").filter({ hasText: "Lịch sử được phép" })).toBeVisible();
});


test("member can pin, send reply mentions and apply an internal avatar asset", async ({ page }) => {
  const captured = await groupApi(page, "Member"); await page.goto("/");
  await expect(page.getByRole("button", { name: "Tin nhắn gốc không khả dụng", exact: true })).toBeDisabled();
  const original = page.locator("#message-old .message-bubble");
  await original.hover();
  const bubbleBounds = await original.boundingBox();
  const actionBounds = await original.locator(".message-action-bar").boundingBox();
  expect(actionBounds!.x).toBeGreaterThanOrEqual(bubbleBounds!.x + bubbleBounds!.width);
  await original.click({ button: "right" });
  await original.getByRole("button", { name: "Ghim tin nhắn", exact: true }).click();
  await expect(original.getByRole("button", { name: "Bỏ ghim tin nhắn", exact: true })).toHaveAttribute("aria-pressed", "true");
  await original.getByRole("button", { name: "Bỏ ghim tin nhắn", exact: true }).click();
  await expect(original.getByRole("button", { name: "Ghim tin nhắn", exact: true })).toHaveAttribute("aria-pressed", "false");
  await original.getByRole("button", { name: "Ghim tin nhắn", exact: true }).click();
  await expect(page.getByRole("button", { name: "Bỏ ghim", exact: true })).toBeVisible();
  await original.click({ button: "right" });
  await original.getByRole("button", { name: "Trả lời", exact: true }).click();
  const composer = page.getByPlaceholder("Nhắn tin tới Nhóm kiểm thử");
  await composer.fill("😀 @an xin chào");
  await composer.press("Enter");
  await expect.poll(() => captured.sentBodies.length).toBe(1);
  expect(captured.sentBodies[0].replyToMessageId).toBe("old");
  expect(captured.sentBodies[0].mentions).toEqual([{ userId: "friend", start: 3, length: 3 }]);
  await expect(page.locator("#message-sent .message-mention")).toHaveText("@an");
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  await page.locator('input[type="file"]').setInputFiles({ name: "avatar.png", mimeType: "image/png",
    buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJ1kAAAAASUVORK5CYII=", "base64") });
  await expect.poll(() => captured.avatarBodies.length).toBe(1);
  expect(captured.avatarBodies[0]).toEqual({ assetId: "internal-asset", expectedVersion: 1 });
});

test("create group selects friends, generates name and fits mobile", async ({ page }) => {
  await groupApi(page);
  const friends = [
    { id: "an", username: "an", fullName: "An", avatarUrl: null },
    { id: "binh", username: "binh", fullName: "Bình", avatarUrl: null },
    ...Array.from({ length: 18 }, (_, i) => ({ id: `friend-${i}`, username: `friend${i}`, fullName: `Bạn ${i}`, avatarUrl: null })),
  ];
  await page.route("**/api/friends", r => r.fulfill({ json: friends }));
  const bodies: any[] = [];
  await page.route("**/api/conversations/group", r => {
    if (r.request().method() !== "POST") return r.fallback();
    bodies.push(r.request().postDataJSON());
    return r.fulfill({ json: { id: "group", name: "An, Bình", version: 1 } });
  });
  await page.goto("/");
  await page.getByRole("button", { name: "Tạo nhóm", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Tạo nhóm" });
  const create = modal.getByRole("button", { name: "Tạo nhóm", exact: true });
  await expect(create).toBeDisabled();
  await modal.getByRole("checkbox", { name: "An @an", exact: true }).check();
  await expect(create).toBeDisabled();
  await modal.getByLabel("Tìm bạn bè", { exact: true }).fill("binh");
  await modal.getByRole("checkbox", { name: "Bình @binh", exact: true }).check();
  await expect(create).toBeEnabled();
  await modal.getByRole("button", { name: "Bỏ chọn An", exact: true }).click();
  await expect(create).toBeDisabled();
  await modal.getByLabel("Tìm bạn bè", { exact: true }).fill("");
  await modal.getByRole("checkbox", { name: "An @an", exact: true }).check();
  await page.screenshot({ path: "test-results/create-group-desktop.png" });
  await page.setViewportSize({ width: 360, height: 640 });
  await expect(create).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "test-results/create-group-mobile.png" });
  await create.click();
  await expect(modal).toHaveCount(0);
  expect(bodies).toEqual([{ name: "Bình, An", memberIds: ["binh", "an"], type: "Group" }]);
});

test("create group retries avatar without creating a second group", async ({ page }) => {
  const captured = await groupApi(page);
  await page.route("**/api/friends", r => r.fulfill({ json: [
    { id: "an", username: "an", fullName: "An" }, { id: "binh", username: "binh", fullName: "Bình" },
  ] }));
  let creates = 0; let uploads = 0;
  await page.route("**/api/conversations/group", r => {
    if (r.request().method() !== "POST") return r.fallback();
    creates++; return r.fulfill({ json: { id: "group", version: 1 } });
  });
  await page.route("https://avatar.test/upload", r => r.fulfill({ status: ++uploads === 1 ? 500 : 200, json: {} }));
  await page.goto("/");
  await page.getByRole("button", { name: "Tạo nhóm", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Tạo nhóm" });
  await modal.getByRole("checkbox").nth(0).check();
  await modal.getByRole("checkbox").nth(1).check();
  await modal.locator('input[type=file]').setInputFiles({ name: "avatar.png", mimeType: "image/png", buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZ1kAAAAASUVORK5CYII=", "base64") });
  await modal.getByRole("button", { name: "Tạo nhóm", exact: true }).click();
  await expect(modal.getByRole("alert")).toBeVisible();
  await modal.getByRole("button", { name: "Thử lại ảnh", exact: true }).click();
  await expect(modal).toHaveCount(0);
  expect(creates).toBe(1);
  expect(uploads).toBe(2);
  expect(captured.avatarBodies).toEqual([{ assetId: "internal-asset", expectedVersion: 1 }]);
});


test("group settings stays usable with long names, search and dark mobile", async ({ page }) => {
  await groupApi(page);
  await page.route("**/api/conversations/group/members", r => r.fulfill({ json: [
    { userId: "owner", username: "owner", fullName: "Tên thành viên rất dài để kiểm tra xuống dòng trên màn hình nhỏ", role: "Owner", joinedAt: "2026-01-01" },
    { userId: "friend", username: "an", fullName: "Nguyễn An", role: "Member", joinedAt: "2026-01-02", canKick: true, canChangeRole: true },
  ] }));
  await page.goto("/");
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Thông tin nhóm", exact: true });
  await modal.getByLabel("Tìm thành viên").fill("not-found");
  await expect(modal.getByText("Không tìm thấy thành viên phù hợp.")).toBeVisible();
  await modal.getByLabel("Tìm thành viên").fill("an");
  await expect(modal.locator(".group-settings__members li")).toHaveCount(1);
  await modal.getByLabel("Tìm thành viên").fill("");
  await page.setViewportSize({ width: 360, height: 640 });
  await page.evaluate(() => document.documentElement.dataset.theme = "dark");
  await modal.getByRole("button", { name: "Rời nhóm", exact: true }).scrollIntoViewIfNeeded();
  await expect(modal.getByRole("button", { name: "Đóng thông tin nhóm" })).toBeInViewport();
  expect(await modal.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  await page.screenshot({ path: "test-results/group-dark-mobile-bottom.png" });
  await modal.evaluate(el => el.scrollTop = 0);
  await page.screenshot({ path: "test-results/group-dark-mobile-top.png" });
  await page.keyboard.press("Escape");
  await expect(modal).toHaveCount(0);
});

test("create group retries loading friends after an error", async ({ page }) => {
  await groupApi(page);
  let attempts = 0;
  await page.route("**/api/friends", r => r.fulfill(++attempts === 1
    ? { status: 503, json: { message: "Không tải được bạn bè" } }
    : { json: [{ id: "an", username: "an", fullName: "An" }] }));
  await page.goto("/");
  await page.getByRole("button", { name: "Tạo nhóm", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Tạo nhóm" });
  await modal.getByRole("button", { name: "Thử tải lại" }).click();
  await expect(modal.getByRole("checkbox", { name: "An @an" })).toBeVisible();
  await expect(modal.getByRole("alert")).toHaveCount(0);
});

test("pins coalesces visibility events and invalidations without overlapping requests", async ({ page }) => {
  await groupApi(page);
  let requests = 0; let active = 0; let peak = 0;
  let release: (() => void) | undefined;
  await page.route("**/api/conversations/group/pins", async route => {
    requests++; active++; peak = Math.max(peak, active);
    if (requests === 2) await new Promise<void>(resolve => { release = resolve; });
    await route.fulfill({ json: [] }); active--;
  });
  await page.goto("/");
  await expect.poll(() => requests).toBe(1);
  await page.getByRole("button", { name: "Thông tin hội thoại", exact: true }).click();
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  await page.evaluate(() => {
    for (let i = 0; i < 20; i++) document.dispatchEvent(new Event("visibilitychange"));
  });
  // Allow the refresh debounce to run; fresh data must not be fetched again.
  await page.waitForTimeout(400);
  expect(requests).toBe(1);
  await page.evaluate(() => {
    for (let i = 0; i < 20; i++) window.dispatchEvent(new Event("chat:pins-changed"));
  });
  await expect.poll(() => requests).toBe(2);
  await page.evaluate(() => {
    for (let i = 0; i < 20; i++) window.dispatchEvent(new Event("chat:pins-changed"));
  });
  await page.waitForTimeout(400);
  expect(requests).toBe(2);
  release!();
  await expect.poll(() => requests).toBe(3);
  expect(peak).toBe(1);
});



