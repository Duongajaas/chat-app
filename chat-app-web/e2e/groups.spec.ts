import { test, expect, type Page } from "@playwright/test";

async function groupApi(page: Page, role: "Owner" | "Member" = "Owner") {
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "owner", username: "owner", fullName: "Owner", isVerified: true };
  let version = 1;
  let left = false;
  const members = [
    { userId: "owner", fullName: "Owner", role, joinedAt: "2026-01-01", canKick: false, canChangeRole: false },
    { userId: "friend", fullName: "Nguyễn An", role: "Member", joinedAt: "2026-01-02", canKick: role === "Owner", canChangeRole: role === "Owner" },
  ];
  const group = () => ({
    id: "group", name: "Nhóm kiểm thử", type: "Group", role: left ? null : role, hasLeft: left, isHidden: left,
    permissions: left ? [] : role === "Owner" ? ["RenameGroup", "AddMember", "ManageRoles", "ManageInvites"] : ["AddMember"],
    memberCount: 2, version, unreadCount: 0, createdAt: "2026-01-01", lastMessageAt: "2026-01-02",
  });
  await page.route("**/hubs/**", r => r.fulfill({ status: 503 }));
  await page.route("https://accounts.google.com/**", r => r.abort());
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (path.endsWith("/auth/refresh-token")) return route.fulfill({ json: { accessToken: token, user, accessTokenExpiresAt: "2100-01-01" } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user });
    if (path.endsWith("/friends")) return route.fulfill({ json: [] });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [group()], nextCursor: null } });
    if (path.endsWith("/conversations/group")) return route.fulfill({ json: group() });
    if (path.endsWith("/message-states")) return route.fulfill({ json: [{ id: "old", hidden: false, deleted: false }] });
    if (path.endsWith("/messages")) return route.fulfill({ json: { messages: [
      { id: "old", conversationId: "group", senderId: "friend", sequence: 1, content: "Lịch sử được phép", createdAt: "2026-01-01" },
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
}

test("owner manages roles and receives invite code only in create response", async ({ page }) => {
  await groupApi(page); await page.goto("/");
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  await expect(page.getByRole("button", { name: "Cấp Admin" })).toBeVisible();
  await page.getByRole("button", { name: "Cấp Admin" }).click();
  await expect(page.getByRole("button", { name: "Hạ Admin" })).toBeVisible();
  await page.getByRole("button", { name: "Tạo link mời" }).click();
  await expect(page.getByLabel("Sao chép và lưu link ngay")).toHaveValue(/\/join#code=secret-invite$/);
});

test("member has no admin actions and leaving preserves read-only history", async ({ page }) => {
  await groupApi(page, "Member"); await page.goto("/");
  await page.getByRole("button", { name: "Thông tin nhóm", exact: true }).click();
  await expect(page.getByText("Nguyễn An · Member")).toBeVisible();
  await expect(page.getByRole("button", { name: "Cấp Admin" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Tạo link mời" })).toHaveCount(0);
  page.on("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Rời nhóm", exact: true }).click();
  await expect(page.getByPlaceholder("Nhắn tin tới Nhóm kiểm thử")).toHaveCount(0);
  await page.getByRole("button", { name: "Nhóm đã rời" }).click();
  await expect(page.locator(".conversation-item__name").filter({ hasText: "Nhóm kiểm thử" })).toBeVisible();
  await expect(page.locator(".message-bubble").filter({ hasText: "Lịch sử được phép" })).toBeVisible();
});
