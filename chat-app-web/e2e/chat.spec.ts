import { test, expect, type Page } from "@playwright/test";
async function mockApi(page: Page) {
  let account = "Alice";
  let attempts = 0;
  const clientIds: string[] = [];
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  const user = () => ({
    id: account,
    username: account.toLowerCase(),
    fullName: account,
    isVerified: true,
  });
  const conversation = () => ({
    id: `room-${account}`,
    name: `Hội thoại ${account}`,
    type: "Direct",
    peerUserId: "peer",
    unreadCount: 0,
    createdAt: new Date().toISOString(),
    lastMessageAt: new Date().toISOString(),
    lastMessage: `Riêng của ${account}`,
  });
  await page.route("**/hubs/**", (route) =>
    route.fulfill({ status: 503, body: "offline" }),
  );
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, (r) =>
    r.abort(),
  );
  await page.route("https://accounts.google.com/**", (route) => route.abort());
  // Match backend requests only; /src/api/*.ts are Vite modules, not API responses.
  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (path.endsWith("/pins")) return route.fulfill({ json: [] });
      if (path.endsWith("/auth/logout")) return route.fulfill({ status: 204 });
      if (path.endsWith("/auth/login")) account = "Bob";
      if (path.endsWith("/auth/login") || path.endsWith("/auth/refresh-token"))
        return route.fulfill({
          json: {
            accessToken: token,
            user: user(),
            accessTokenExpiresAt: "2100-01-01T00:00:00Z",
          },
        });
      if (path.endsWith("/users/me")) return route.fulfill({ json: user() });
      if (path.endsWith("/conversations"))
        return route.fulfill({
          json: { items: [conversation()], nextCursor: null },
        });
      if (path.endsWith("/messages/reconcile"))
        return route.fulfill({
          json: request
            .postDataJSON()
            .items.map((i: { clientMessageId: string }) => ({
              clientMessageId: i.clientMessageId,
              state: "NotFound",
            })),
        });
      if (path.endsWith("/message-states")) return route.fulfill({ json: [] });
      if (path.endsWith("/messages") && request.method() === "GET")
        return route.fulfill({
          json: {
            messages: [
              {
                id: `private-${account}`,
                conversationId: `room-${account}`,
                senderId: account,
                clientMessageId: `old-${account}`,
                sequence: 1,
                content: `Riêng của ${account}`,
                createdAt: new Date().toISOString(),
                status: "Sent",
              },
            ],
            hasMore: false,
          },
        });
      if (path.endsWith("/messages") && request.method() === "POST") {
        const body = request.postDataJSON();
        clientIds.push(body.clientMessageId);
        if (++attempts === 1)
          return route.fulfill({
            status: 503,
            json: { message: "temporary failure" },
          });
        return route.fulfill({
          json: {
            ...body,
            expression: body.expression ? { ...body.expression, provider: body.expression.kind === "sticker" ? "local" : "giphy",
              name: body.expression.kind === "sticker" ? "Mèo vui" : "GIF từ GIPHY", width: 240, height: 240,
              url: body.expression.kind === "sticker" ? `/stickers/${body.expression.id}${body.expression.id === "heart" ? "-animated" : ""}.svg` : null,
              thumbnailUrl: body.expression.kind === "sticker" ? `/stickers/${body.expression.id}.svg` : null,
              animated: body.expression.kind === "gif" || body.expression.id === "heart" } : null,
            type: body.expression?.kind === "sticker" ? "Sticker" : body.expression ? "Gif" : "Text",
            id: "saved-message",
            conversationId: `room-${account}`,
            senderId: account,
            sequence: 2,
            createdAt: new Date().toISOString(),
            status: "Sent",
          },
        });
      }
      if (path.includes("/conversations/"))
        return route.fulfill({ json: conversation() });
      return route.fulfill({ json: [] });
    },
  );
  return clientIds;
}

test("transient failure retries automatically with the same ID and HTTP ACK confirms without realtime", async ({
  page,
}) => {
  const ids = await mockApi(page);
  await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await expect(composer).toBeVisible();
  await composer.fill("Tin cần gửi lại");
  await composer.press("Enter");
  await expect.poll(() => ids.length).toBe(2);
  await expect(
    page.getByRole("button", { name: "Gửi lại", exact: true }),
  ).toHaveCount(0);
  const message = page
    .locator(".message-bubble")
    .filter({ hasText: "Tin cần gửi lại" });
  await expect(message).toHaveCount(1);
  await message.click({ button: "right" });
  await expect(
    page.getByRole("button", { name: "Xóa với mọi người" }),
  ).toBeVisible();
  expect(ids).toHaveLength(2);
  expect(ids[0]).toBe(ids[1]);
});

test("message actions reveal on hover and keyboard; More keeps deletion actions", async ({
  page,
}) => {
  await mockApi(page);
  await page.goto("/");
  const bubble = page.locator("#message-private-Alice .message-bubble");
  const bar = bubble.locator(".message-action-bar");
  await expect(bar).toHaveCSS("opacity", "0");
  await bubble.hover();
  await expect(bar).toHaveCSS("opacity", "1");
  const box = await bubble.boundingBox();
  const actions = await bar.boundingBox();
  expect(actions!.x + actions!.width).toBeLessThanOrEqual(box!.x);
  await bubble.getByRole("button", { name: "Thêm thao tác" }).click();
  await expect(
    bubble.getByRole("button", { name: "Xóa phía tôi" }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/message-actions-desktop.png",
    fullPage: true,
  });
  await page.keyboard.press("Escape");
  await expect(bubble.locator(".message-menu")).toHaveCount(0);
  await page.mouse.move(0, 0);
  await bubble.focus();
  await page.keyboard.press("Enter");
  await expect(bubble).toHaveClass(/has-actions/);
});

test("desktop information panel preserves draft and fits four columns", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await mockApi(page);
  await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Bản nháp đang viết");
  await page
    .getByRole("button", { name: "Thông tin hội thoại", exact: true })
    .click();
  await expect(page.locator(".conversation-info")).toBeVisible();
  await expect(composer).toHaveValue("Bản nháp đang viết");
  const chat = await page.locator(".chat-window").boundingBox();
  const info = await page.locator(".conversation-info").boundingBox();
  expect(chat!.x + chat!.width).toBeLessThanOrEqual(info!.x);
  expect(info!.x + info!.width).toBeLessThanOrEqual(1440);
  await page.screenshot({ path: "test-results/refactor-desktop-light.png" });
  await page.evaluate(() => {
    document.documentElement.dataset.theme = "dark";
  });
  await page.screenshot({ path: "test-results/refactor-desktop-dark.png" });
  await page.keyboard.press("Escape");
  await expect(page.locator(".conversation-info")).toHaveCount(0);
  await expect(composer).toHaveValue("Bản nháp đang viết");
});

test.describe("touch message actions", () => {
  test.use({
    hasTouch: true,
    isMobile: true,
    viewport: { width: 390, height: 844 },
  });
  test("chat hides bottom navigation and conversation info opens full screen", async ({
    page,
  }) => {
    await mockApi(page);
    await page.goto("/");
    await expect(page.locator(".mobile-nav")).toBeVisible();
    await page.locator(".conversation-item").click();
    await expect(page.locator(".mobile-nav")).toBeHidden();
    await page
      .getByRole("button", { name: "Thông tin hội thoại", exact: true })
      .click();
    await expect(page.locator(".conversation-info")).toBeVisible();
    await page.evaluate(() => {
      document.documentElement.dataset.theme = "dark";
    });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await page.screenshot({ path: "test-results/refactor-mobile-info.png" });
    await page
      .getByRole("button", { name: "Đóng thông tin hội thoại" })
      .click();
    const composerBox = await page
      .locator(".chat-window__composer")
      .boundingBox();
    expect(composerBox!.y + composerBox!.height).toBeLessThanOrEqual(844);
    expect(composerBox!.y).toBeGreaterThan(0);
    await page.screenshot({ path: "test-results/refactor-mobile-chat.png" });
    await page.getByRole("button", { name: "Quay lại", exact: true }).click();
    await expect(page.locator(".mobile-nav")).toBeVisible();
  });
  test("tap does not open; hold opens; movement cancels", async ({ page }) => {
    await mockApi(page);
    await page.goto("/");
    await page.locator(".conversation-item").click();
    const bubble = page.locator("#message-private-Alice .message-bubble");
    await bubble.tap();
    await expect(bubble.locator(".message-action-bar")).toBeHidden();
    await page.clock.install();
    await bubble.dispatchEvent("pointerdown", {
      pointerType: "touch",
      isPrimary: true,
    });
    await page.clock.fastForward(460);
    await bubble.dispatchEvent("pointerup", {
      pointerType: "touch",
      isPrimary: true,
    });
    await expect(bubble.locator(".message-action-bar")).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({
      path: "test-results/message-actions-mobile.png",
      fullPage: true,
    });
    await page.getByPlaceholder("Nhắn tin tới Hội thoại Alice").tap();
    await expect(bubble).not.toHaveClass(/has-actions/);
    await bubble.dispatchEvent("pointerdown", {
      pointerType: "touch",
      isPrimary: true,
    });
    await bubble.dispatchEvent("pointermove", {
      pointerType: "touch",
      isPrimary: true,
    });
    await page.clock.fastForward(500);
    await expect(bubble.locator(".message-action-bar")).toBeHidden();
  });
});

test("logout and login as another user clears the prior account chat", async ({
  page,
}) => {
  await mockApi(page);
  await page.goto("/");
  await expect(
    page.locator(".message-bubble").filter({ hasText: "Riêng của Alice" }),
  ).toBeVisible();
  await page.locator(".sidebar-avatar-btn").click();
  await page.getByRole("button", { name: "Đăng xuất" }).click();
  await page.getByLabel("Username", { exact: true }).fill("bob");
  await page.getByLabel("Mật khẩu", { exact: true }).fill("test-password");
  await page.getByRole("button", { name: "Đăng nhập", exact: true }).click();
  await expect(
    page.getByPlaceholder("Nhắn tin tới Hội thoại Bob"),
  ).toBeVisible();
  await expect(
    page.locator(".message-bubble").filter({ hasText: "Riêng của Bob" }),
  ).toBeVisible();
  await expect(page.getByText("Riêng của Alice", { exact: true })).toHaveCount(
    0,
  );
});

test("lost HTTP acknowledgement is reconciled without a second send", async ({
  page,
}) => {
  await mockApi(page);
  let attempts = 0;
  let saved: Record<string, unknown> | null = null;
  await page.route(
    "**/api/conversations/room-Alice/messages",
    async (route) => {
      if (route.request().method() !== "POST") return route.fallback();
      attempts++;
      saved = {
        ...route.request().postDataJSON(),
        id: "reconciled",
        conversationId: "room-Alice",
        senderId: "Alice",
        sequence: 2,
        createdAt: new Date().toISOString(),
        status: "Sent",
      };
      return route.abort("failed");
    },
  );
  await page.route(
    "**/api/conversations/room-Alice/messages/reconcile",
    (route) =>
      route.fulfill({
        json: [
          {
            clientMessageId: saved!.clientMessageId,
            state: "Readable",
            messageId: "reconciled",
            message: saved,
          },
        ],
      }),
  );
  await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("ACK bị mất");
  await composer.press("Enter");
  await expect(page.locator("#message-reconciled")).toContainText("ACK bị mất");
  expect(attempts).toBe(1);
  await expect(
    page.locator(".message-bubble").filter({ hasText: "ACK bị mất" }),
  ).toHaveCount(1);
});

test("cooldown error shows the remaining wait time in chat", async ({
  page,
}) => {
  await mockApi(page);
  await page.route("**/api/blocks/peer", (route) =>
    route.fulfill({
      status: 409,
      json: {
        code: "REBLOCK_COOLDOWN",
        remainingSeconds: 125,
        reblockAllowedAt: new Date(Date.now() + 125000).toISOString(),
        message: "Bạn cần chờ thêm 2 phút 5 giây mới được chặn lại.",
      },
    }),
  );
  page.on("dialog", (dialog) => dialog.accept());
  await page.goto("/");
  await page.getByRole("button", { name: "Tùy chọn", exact: true }).click();
  await page
    .getByRole("button", { name: "Chặn người này", exact: true })
    .click();
  await expect(page.getByRole("alert")).toContainText("2 phút 5 giây");
});

test("chat remains interactive after sending and after HTTP confirmation", async ({
  page,
}) => {
  await mockApi(page);
  await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Kiểm tra giao diện sau khi gửi");
  await composer.press("Enter");
  await expect(composer).toHaveValue("");
  await composer.fill("Tôi vẫn nhập được");
  await expect(composer).toHaveValue("Tôi vẫn nhập được");
  await expect(
    page
      .locator(".message-bubble")
      .filter({ hasText: "Kiểm tra giao diện sau khi gửi" }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Thông tin hội thoại", exact: true })
    .click();
  await expect(page.locator(".conversation-info")).toBeVisible();
});

test("send remains responsive with a long conversation", async ({ page }) => {
  await mockApi(page);
  await page.route(
    (url) => url.pathname === "/api/conversations/room-Alice/messages",
    (route) =>
      route.request().method() === "GET"
        ? route.fulfill({
            json: {
              messages: Array.from({ length: 1000 }, (_, i) => ({
                id: `old-${i}`,
                conversationId: "room-Alice",
                senderId: "Alice",
                sequence: i + 1,
                content: `Tin ${i}`,
                createdAt: "2026-09-01T00:00:00Z",
                status: "Sent",
              })),
              hasMore: false,
            },
          })
        : route.fallback(),
  );
  await page.goto("/");
  await expect(page.locator(".message-bubble")).toHaveCount(120);
  await expect(
    page.getByRole("button", { name: "Xem thêm tin nhắn đã tải" }),
  ).toBeVisible();
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

test("GIPHY picker searches, sends a GIF, retries and restores the image from history", async ({
  page,
}) => {
  const ids = await mockApi(page);
  const gifUrl = "https://media.giphy.com/media/example123/200.gif";
  const rendition = { url: gifUrl, width: "200", height: "200", size: "100" };
  const gif = {
    id: "example123",
    type: "gif",
    title: "Happy GIF",
    slug: "happy-example123",
    rating: "g",
    is_sticker: 0,
    url: "https://giphy.com/gifs/example123",
    username: "",
    source: "",
    import_datetime: "2026-01-01 00:00:00",
    images: Object.fromEntries(
      [
        "original",
        "original_still",
        "fixed_height",
        "fixed_height_still",
        "fixed_width",
        "fixed_width_still",
        "fixed_height_downsampled",
        "fixed_width_downsampled",
        "downsized",
      ].map((key) => [key, rendition]),
    ),
    analytics: {
      onload: { url: "" },
      onclick: { url: "" },
      onsent: { url: "" },
    },
  };
  await page.route("https://api.giphy.com/**", (route) =>
    route.fulfill({
      json: {
        data: new URL(route.request().url()).pathname.endsWith("/example123") ? gif : [gif],
        pagination: { total_count: 1, count: 1, offset: 0 },
        meta: { status: 200, msg: "OK", response_id: "test" },
      },
    }),
  );
  await page.route("https://media.giphy.com/**", (route) =>
    route.fulfill({
      contentType: "image/gif",
      body: Buffer.from(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7",
        "base64",
      ),
    }),
  );
  await page.goto("/");
  await page.getByRole("button", { name: "Gửi GIF", exact: true }).click();
  const picker = page.getByRole("region", { name: "Chọn GIF", exact: true });
  await expect(picker).toBeVisible();
  await expect(
    picker.getByText("Powered by GIPHY", { exact: true }),
  ).toBeVisible();
  const searchRequest = page.waitForRequest(
    (request) =>
      request.url().includes("api.giphy.com/v1/gifs/search") &&
      request.url().includes("q=happy"),
  );
  await picker.getByRole("searchbox").fill("happy");
  await searchRequest;
  await picker.locator("img").first().click();
  await expect(picker).toHaveCount(0);
  await expect.poll(() => ids.length).toBe(2);
  await expect(
    page.getByRole("button", { name: "Gửi lại", exact: true }),
  ).toHaveCount(0);
  const image = page.locator(".message-gif img");
  await expect(image).toHaveCount(1);
  await expect(image).toHaveAttribute("src", gifUrl);
  expect(ids).toHaveLength(2);
  expect(ids[0]).toBe(ids[1]);

  await page.route(
    (url) =>
      url.pathname.startsWith("/api/") && url.pathname.endsWith("/messages"),
    (route) =>
      route.fulfill({
        json: {
          messages: [
            {
              id: "saved-message",
              conversationId: "room-Alice",
              senderId: "Alice",
              clientMessageId: ids[0],
              sequence: 2,
              content: "", type: "Gif",
              expression: { kind: "gif", provider: "giphy", id: "example123", name: "Happy GIF", width: 200, height: 200, animated: true },
              createdAt: new Date().toISOString(),
              status: "Sent",
            },
          ],
          hasMore: false,
        },
      }),
  );
  await page.reload();
  await expect(page.locator(".message-gif img")).toHaveAttribute("src", gifUrl);
  await page.locator(".message-bubble").click({ button: "right" });
  await expect(
    page.getByRole("button", { name: "Xóa với mọi người" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Xóa với mọi người" }).press("Enter");
  await expect(page.locator(".message-gif img")).toHaveCount(0);
  await expect(page.locator(".message-bubble__deleted")).toBeVisible();
});

test("GIPHY errors can be retried and Escape returns focus to the GIF button", async ({
  page,
}) => {
  await mockApi(page);
  await page.route("https://api.giphy.com/**", (route) =>
    route.fulfill({
      status: 401,
      json: {
        data: [],
        meta: { status: 401, msg: "Invalid key", response_id: "test" },
      },
    }),
  );
  await page.goto("/");
  const button = page.getByRole("button", { name: "Gửi GIF", exact: true });
  await button.click();
  const picker = page.getByRole("region", { name: "Chọn GIF", exact: true });
  await expect(picker.getByRole("alert")).toContainText("Không thể tải GIF");
  await page.route("https://api.giphy.com/**", (route) =>
    route.fulfill({
      json: {
        data: [],
        pagination: { total_count: 0, count: 0, offset: 0 },
        meta: { status: 200, msg: "OK", response_id: "test" },
      },
    }),
  );
  await picker.getByRole("button", { name: "Thử lại", exact: true }).click();
  await expect(
    picker.getByText("Không tìm thấy GIF. Hãy thử từ khóa khác.", {
      exact: true,
    }),
  ).toBeVisible({ timeout: 10000 });
  await picker.getByRole("searchbox").press("Escape");
  await expect(picker).toHaveCount(0);
  await expect(button).toBeFocused();
});

test("emoji inserts at the caret repeatedly and keyboard navigation preserves the draft", async ({ page }) => {
  await mockApi(page); await page.goto("/");
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Xin chào");
  await composer.evaluate((input: HTMLInputElement) => input.setSelectionRange(4, 4));
  const trigger = page.getByRole("button", { name: "Mở bảng biểu cảm", exact: true });
  await trigger.click();
  const picker = page.getByRole("dialog", { name: "Emoji, Sticker và GIF" });
  await expect(picker).toBeVisible();
  await picker.getByText("😀", { exact: true }).first().click();
  await expect(composer).toHaveValue("Xin 😀chào");
  await picker.getByText("😀", { exact: true }).first().click();
  await expect(composer).toHaveValue("Xin 😀😀chào");
  const emojiTab = picker.getByRole("tab", { name: "😀 Emoji", exact: true });
  await emojiTab.focus(); await page.keyboard.press("ArrowRight");
  await expect(picker.getByRole("tab", { name: "🐱 Sticker", exact: true })).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Home");
  await expect(emojiTab).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Escape");
  await expect(picker).toHaveCount(0); await expect(trigger).toBeFocused();
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem("chatapp-expressions:Alice")!).emojis[0].count)).toBe(2);
});

test("mobile stickers support preview favorites and one-click send without covering the composer", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const ids = await mockApi(page);
  const sticker = { kind: "sticker", provider: "local", id: "cat-happy", packId: "cats", name: "Mèo vui",
    url: "/stickers/cat-happy.svg", thumbnailUrl: "/stickers/cat-happy.svg", width: 240, height: 240 };
  await page.route("**/api/expressions/sticker-packs", route => route.fulfill({ json: [{ id: "cats", name: "Mèo đáng yêu", stickers: [sticker] }] }));
  await page.goto("/"); await page.locator(".conversation-item").click();
  const composer = page.getByPlaceholder("Nhắn tin tới Hội thoại Alice");
  await composer.fill("Bản nháp vẫn còn");
  await page.getByRole("button", { name: "Mở bảng biểu cảm", exact: true }).click();
  const picker = page.getByRole("dialog", { name: "Emoji, Sticker và GIF" });
  await picker.getByRole("tab", { name: "🐱 Sticker", exact: true }).click();
  await picker.getByRole("button", { name: "Yêu thích Mèo vui", exact: true }).click();
  await picker.getByRole("button", { name: "Yêu thích", exact: true }).click();
  await expect(picker.getByRole("button", { name: "Yêu thích Mèo vui", exact: true })).toHaveAttribute("aria-pressed", "true");
  await picker.getByRole("button", { name: "Xem trước Mèo vui", exact: true }).click();
  await expect(picker.locator(".sticker-preview img")).toHaveAttribute("src", sticker.url);
  const panelBox = await picker.boundingBox();
  const composerBox = await page.locator(".chat-window__composer").boundingBox();
  expect(panelBox!.y + panelBox!.height).toBeLessThanOrEqual(composerBox!.y);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "test-results/expression-picker-mobile.png" });
  await page.evaluate(() => { document.documentElement.dataset.theme = "dark"; });
  await page.screenshot({ path: "test-results/expression-picker-mobile-dark.png" });
  const sent = page.waitForRequest(request => request.url().endsWith("/messages") && request.method() === "POST");
  await picker.getByRole("button", { name: "Gửi sticker Mèo vui", exact: true }).click();
  expect((await sent).postDataJSON().expression).toEqual({ kind: "sticker", id: "cat-happy", packId: "cats" });
  await expect.poll(() => ids.length).toBe(2);
  await expect(page.locator(".message-sticker img")).toHaveCount(1);
  await expect(composer).toHaveValue("Bản nháp vẫn còn");
});

test("reduced motion shows a still sticker until the user chooses to play it", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await mockApi(page);
  const sticker = { kind: "sticker", provider: "local", id: "heart", packId: "feelings", name: "Trái tim rung rinh",
    url: "/stickers/heart-animated.svg", thumbnailUrl: "/stickers/heart.svg", width: 240, height: 240, animated: true };
  await page.route("**/api/expressions/sticker-packs", route => route.fulfill({ json: [{ id: "feelings", name: "Gửi yêu thương", stickers: [sticker] }] }));
  await page.goto("/");
  await page.getByRole("button", { name: "Mở bảng biểu cảm", exact: true }).click();
  await page.getByRole("tab", { name: "🐱 Sticker", exact: true }).click();
  await page.getByRole("button", { name: "Gửi sticker Trái tim rung rinh", exact: true }).click();
  const image = page.locator(".message-sticker img");
  await expect(image).toHaveAttribute("src", "/stickers/heart.svg");
  await page.getByRole("button", { name: "Phát ảnh động", exact: true }).click();
  await expect(image).toHaveAttribute("src", "/stickers/heart-animated.svg");
  await page.getByRole("button", { name: "Dừng ảnh động", exact: true }).click();
  await expect(image).toHaveAttribute("src", "/stickers/heart.svg");
});
