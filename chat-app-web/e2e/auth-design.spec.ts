import { test, expect } from "@playwright/test";

for (const path of ["login", "register"]) {
  test(`${path}: responsive layout, password toggle and error recovery`, async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
    await page.route("https://accounts.google.com/**", r => r.abort());
    await page.route(url => url.pathname.startsWith("/api/"), r => r.fulfill({ status: 401, json: { message: "Vui lòng kiểm tra lại thông tin." } }));
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`/${path}`);
    await expect(page.locator(".auth-entry__aside")).toBeVisible();
    await page.screenshot({ path: `test-results/auth-${path}-desktop.png`, fullPage: true });
    await page.getByLabel("Username", { exact: true }).fill("testuser");
    await page.getByLabel("Mật khẩu", { exact: true }).fill("password123");
    await page.getByRole("button", { name: "Hiện mật khẩu", exact: true }).click();
    await expect(page.locator("#password")).toHaveAttribute("type", "text");
    await page.getByRole("button", { name: "Ẩn mật khẩu", exact: true }).click();
    await expect(page.locator("#password")).toHaveAttribute("type", "password");
    if (path === "register") {
      await page.getByLabel("Họ và tên").fill("Người kiểm thử");
      await page.getByLabel("Email", { exact: true }).fill("test@example.com");
    }
    await page.locator('button[type="submit"]').click();
    await expect(page.getByRole("alert")).toBeVisible();
    await expect(page.locator('button[type="submit"]')).toBeEnabled();
    await expect(page.locator("#username")).toHaveValue("testuser");
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.locator(".auth-entry__aside")).toBeHidden();
    await expect(page.locator(".auth-entry__mobile-brand")).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: `test-results/auth-${path}-mobile.png`, fullPage: true });
    expect(errors).toEqual([]);
  });
}
