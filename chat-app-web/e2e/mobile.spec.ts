import { test, expect, type Page } from "@playwright/test";

async function mockApi(page: Page, authenticated = true) {
  let blocked = true;
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = { id: "me", username: "me", fullName: "Người kiểm thử", email: "me@example.test", isVerified: true };
  await page.route("**/hubs/**", r => r.fulfill({ status: 503 }));
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
  await page.route("https://accounts.google.com/**", r => r.abort());
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/auth/refresh-token")) return route.fulfill(authenticated
      ? { json: { accessToken: token, user, accessTokenExpiresAt: "2100-01-01" } } : { status: 401 });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [], nextCursor: null } });
    if (path.endsWith("/blocks/peer")) { blocked = false; return route.fulfill({ status: 204 }); }
    if (path.endsWith("/blocks")) return route.fulfill({ json: blocked ? [{ id: "peer", fullName: "An", username: "an", blockedAt: "2026-09-01" }] : [] });
    if (path.includes("/friend-links")) return route.fulfill({ json: { id: "link", url: "https://example.test/add-friend/token", createdAt: "2026-09-01" } });
    return route.fulfill({ json: [] });
  });
}

for (const path of ["/login", "/register", "/forgot-password", "/reset-password?token=test"]) {
  test(`auth page scrolls on a short mobile viewport: ${path}`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 280 });
    await mockApi(page, false); await page.goto(path);
    const shell = page.locator(".auth-shell"); await expect(shell).toBeVisible();
    const scrollTop = await shell.evaluate(el => {
      const owner = el.scrollHeight > el.clientHeight && ["auto", "scroll"].includes(getComputedStyle(el).overflowY)
        ? el : document.scrollingElement!;
      owner.scrollTop = owner.scrollHeight;
      return owner.scrollTop;
    });
    expect(scrollTop).toBeGreaterThan(0);
    expect(await shell.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
    const last = page.locator(".auth-card a").last();
    await last.scrollIntoViewIfNeeded();
    const box = await last.boundingBox();
    expect(box!.y).toBeGreaterThanOrEqual(0); expect(box!.y + box!.height).toBeLessThanOrEqual(280);
  });
}

test("mobile bottom navigation survives route changes and unblock works from contacts", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await mockApi(page); await page.goto("/friends");
  const nav = page.getByRole("navigation", { name: "Điều hướng chính" });
  await expect(nav).toBeVisible();
  const bounds = await nav.boundingBox(); expect(bounds!.y + bounds!.height).toBe(667);
  await expect(page.locator(".app-sidebar")).toBeHidden();
  await page.getByRole("link", { name: "Đã chặn", exact: true }).click();
  await page.getByRole("button", { name: "Bỏ chặn", exact: true }).click();
  await expect(page.getByRole("dialog")).toContainText("1 giờ");
  await page.getByRole("button", { name: "Xác nhận bỏ chặn", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Đã bỏ chặn");
  await expect(page.getByText("Bạn chưa chặn ai.")).toBeVisible();
  await nav.getByRole("link", { name: "Cá nhân", exact: true }).click();
  await expect(page).toHaveURL(/\/profile$/);
  await nav.getByRole("link", { name: "Cài đặt", exact: true }).click();
  await page.locator(".settings-card").getByRole("link", { name: /Link kết bạn/ }).click();
  await expect(page).toHaveURL(/\/friend-link$/);
  await expect(nav).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test("profile content scrolls above bottom navigation at keyboard-sized height", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 350 });
  await mockApi(page); await page.goto("/profile");
  const logout = page.getByRole("button", { name: "Đăng xuất", exact: true });
  await logout.scrollIntoViewIfNeeded();
  const button = await logout.boundingBox();
  const nav = await page.locator(".mobile-nav").boundingBox();
  expect(button!.y).toBeGreaterThanOrEqual(0);
  expect(button!.y + button!.height).toBeLessThanOrEqual(nav!.y);
  expect(await page.locator(".profile-page").evaluate(el => el.scrollTop)).toBeGreaterThan(0);
});

test("settings links and theme selection persist after reload", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockApi(page); await page.goto("/settings");
  await page.getByRole("radio", { name: /Tối/ }).check();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.reload();
  await expect(page.getByRole("radio", { name: /Tối/ })).toBeChecked();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.getByRole("radio", { name: /Sáng/ }).check();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  await page.locator(".settings-card").getByRole("link", { name: /Quản lý thiết bị/ }).click();
  await expect(page).toHaveURL(/\/devices$/);
  await page.getByRole("link", { name: "Quay lại cài đặt" }).click();
  await expect(page).toHaveURL(/\/settings$/);
  await page.screenshot({ path: "test-results/settings-mobile.png" });
});

test("friend block confirmation preserves the list on failure and supports retry", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockApi(page);
  await page.route("**/api/friends", r => r.fulfill({ json: [{ id: "peer", username: "an", fullName: "Nguyễn An" }] }));
  let attempts = 0;
  await page.route("**/api/blocks/peer", r => r.fulfill(++attempts === 1
    ? { status: 409, json: { message: "Bạn cần chờ 1 giờ trước khi chặn lại." } } : { status: 204 }));
  await page.goto("/friends");
  await page.getByRole("button", { name: "Tùy chọn cho Nguyễn An" }).click();
  await page.getByRole("button", { name: "Chặn", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Xác nhận chặn" }).click();
  await expect(dialog.getByRole("alert")).toContainText("1 giờ");
  await expect(page.locator(".friend-item")).toHaveCount(1);
  await page.screenshot({ path: "test-results/friends-confirm-mobile.png", fullPage: true });
  await dialog.getByRole("button", { name: "Xác nhận chặn" }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator(".friend-item")).toHaveCount(0);
});


test("unblock dialog supports cancel, error retry and keyboard focus", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 400 }); await mockApi(page);
  let requests = 0;
  await page.route("**/api/blocks/peer", route => {
    requests++;
    return requests === 1 ? route.fulfill({ status: 503, json: { message: "Tạm thời chưa bỏ chặn được" } }) : route.fulfill({ status: 204 });
  });
  await page.goto("/blocked-users");
  const open = page.getByRole("button", { name: "Bỏ chặn", exact: true });
  await open.click();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0); expect(requests).toBe(0);
  await expect(open).toBeFocused();
  await open.click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Xác nhận bỏ chặn", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("Tạm thời");
  await expect(dialog).toBeVisible();
  await dialog.getByRole("button", { name: "Xác nhận bỏ chặn", exact: true }).click();
  await expect(dialog).toHaveCount(0); expect(requests).toBe(2);
});

test("settings return link follows the entry point and survives child pages", async ({ page }) => {
  await mockApi(page);
  await page.goto("/");
  await page.locator(".app-sidebar").getByRole("link", { name: "Cài đặt", exact: true }).click();
  await expect(page).toHaveURL(/\/settings$/);
  await page.getByRole("link", { name: "Quay lại trò chuyện" }).click();
  await expect(page).toHaveURL(/\/$/);
  await page.goto("/profile");
  await page.locator(".profile-card").getByRole("link", { name: "Cài đặt" }).click();
  await expect(page.getByRole("link", { name: "Quay lại trang cá nhân" })).toBeVisible();
  await page.locator(".settings-card").getByRole("link", { name: /Quản lý thiết bị/ }).click();
  await page.getByRole("link", { name: "Quay lại cài đặt" }).click();
  await page.locator(".settings-card").getByRole("link", { name: /Link kết bạn/ }).click();
  await page.getByRole("link", { name: "Quay lại cài đặt" }).click();
  await page.reload();
  await page.getByRole("link", { name: "Quay lại trang cá nhân" }).click();
  await expect(page).toHaveURL(/\/profile$/);
});

