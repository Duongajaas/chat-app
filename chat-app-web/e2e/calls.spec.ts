import { test, expect, type Page } from "@playwright/test";
test.use({ launchOptions: { args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] } });
const room = "11111111-1111-4111-8111-111111111111";
const me = "22222222-2222-4222-8222-222222222222";
function snapshot(overrides: Record<string, unknown> = {}) {
  return { id: "33333333-3333-4333-8333-333333333333", conversationId: room, type: "Audio", status: "Ringing", version: 1,
    isCaller: false, ownsEndpoint: false, hasEndpoint: false, generation: 0, epoch: 0, peerName: "Minh Anh", serverTime: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 30000).toISOString(), ...overrides };
}
async function setup(page: Page, incoming = false) {
  let call: ReturnType<typeof snapshot> | null = incoming ? snapshot() : null;
  const commands: string[] = [];
  const requests: Record<string, unknown>[] = [];
  const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, r => r.abort());
  await page.route("https://accounts.google.com/**", r => r.abort());
  await page.routeWebSocket(/\/hubs\/chat/, ws => {
    ws.onMessage(raw => {
      for (const part of String(raw).split("\u001e").filter(Boolean)) {
        const msg = JSON.parse(part);
        if (msg.protocol) { ws.send("{}\u001e"); continue; }
        if (msg.type !== 1) continue;
        const args = msg.arguments ?? [];
        let result: unknown = null;
        if (msg.target === "InitiateCall") {
          requests.push(args[0]); call = snapshot({ isCaller: true, ownsEndpoint: true, hasEndpoint: true, generation: 1, clientCallId: args[0].clientCallId }); result = call;
        } else if (msg.target === "UpdateCall") {
          const action = args[1].action; commands.push(action);
          if (action === "reject") call = snapshot({ status: "Rejected", version: 2 });
          if (action === "accept") call = snapshot({ status: "Accepted", ownsEndpoint: true, hasEndpoint: true, generation: 1, epoch: 1, version: 2 });
          result = call;
        } else if (msg.target === "SendSignal") result = true;
        ws.send(JSON.stringify({ type: 3, invocationId: msg.invocationId, result }) + "\u001e");
      }
    });
  });
  await page.route("**/hubs/chat/negotiate?*", r => r.fulfill({ json: { negotiateVersion: 1, connectionId: "connection", connectionToken: "connection", availableTransports: [{ transport: "WebSockets", transferFormats: ["Text", "Binary"] }] } }));
  await page.route(url => url.pathname.startsWith("/api/"), route => {
    const path = new URL(route.request().url()).pathname;
    const user = { id: me, username: "alice", fullName: "Alice", isVerified: true };
    const conversation = { id: room, name: "Minh Anh", type: "Direct", peerUserId: "peer", unreadCount: 0, lastMessage: "Xin chào", createdAt: new Date().toISOString() };
    if (path.endsWith("/auth/refresh-token")) return route.fulfill({ json: { accessToken: token, user } });
    if (path.endsWith("/users/me")) return route.fulfill({ json: user });
    if (path.endsWith("/ice-servers")) return route.fulfill({ json: { iceServers: [], relayAvailable: true, expiresAt: new Date(Date.now() + 3600000).toISOString() } });
    if (path.endsWith("/end")) { commands.push("end"); call = snapshot({ status: "Ended", version: 4 }); return route.fulfill({ json: call }); }
    if (path.includes("/calls/")) return route.fulfill({ json: call });
    if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [conversation], nextCursor: null } });
    if (path.endsWith("/messages")) return route.fulfill({ json: { messages: [], hasMore: false } });
    if (path.endsWith("/pins") || path.endsWith("/message-states")) return route.fulfill({ json: [] });
    if (path.includes("/conversations/")) return route.fulfill({ json: conversation });
    return route.fulfill({ json: [] });
  });
  return { commands, requests, change: (next: ReturnType<typeof snapshot>) => { call = next; } };
}
test("incoming call is keyboard accessible and rejection is account-wide", async ({ page }) => {
  const mock = await setup(page, true); await page.goto("/");
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("button", { name: "Nhận cuộc gọi", exact: true })).toBeVisible();
  await page.keyboard.press("Escape"); await expect(page.getByRole("dialog")).toBeVisible();
  await page.getByRole("button", { name: "Từ chối", exact: true }).click();
  await expect(page.getByText("Cuộc gọi bị từ chối", { exact: true })).toBeVisible(); expect(mock.commands).toContain("reject");
});
test("permission failure does not reject other devices", async ({ page }) => {
  const mock = await setup(page, true);
  await page.addInitScript(() => { navigator.mediaDevices.getUserMedia = async () => { throw new DOMException("Denied", "NotAllowedError"); }; });
  await page.goto("/"); await page.getByRole("button", { name: "Nhận cuộc gọi", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Không mở được micro/camera");
  expect(mock.commands).not.toContain("reject"); expect(mock.commands).not.toContain("accept");
  await expect(page.getByRole("button", { name: "Nhận cuộc gọi", exact: true })).toBeEnabled();
});
test("outgoing audio ends once and stops microphone tracks", async ({ page }) => {
  const mock = await setup(page); await page.goto("/");
  await page.getByRole("button", { name: "Gọi thoại", exact: true }).click();
  await expect(page.getByRole("button", { name: "Cúp máy", exact: true })).toBeVisible();
  const before = await page.evaluate(async () => { const path = "/src/store/callStore.ts"; const m = await import(path); (window as any).__callTracks = m.useCallStore.getState().local.getTracks(); return (window as any).__callTracks.length; });
  expect(before).toBeGreaterThan(0);
  await page.getByRole("button", { name: "Cúp máy", exact: true }).click();
  await expect(page.getByText("Cuộc gọi đã kết thúc", { exact: true })).toBeVisible();
  expect(mock.requests).toHaveLength(1); expect(mock.commands.filter(c => c === "end")).toHaveLength(1);
  expect(await page.evaluate(() => (window as any).__callTracks.every((t: MediaStreamTrack) => t.readyState === "ended"))).toBeTruthy();
});
test("late ringing event cannot revive a terminal call", async ({ page }) => {
  const mock = await setup(page, true); await page.goto("/");
  await page.getByRole("button", { name: "Từ chối", exact: true }).click();
  mock.change(snapshot({ status: "Rejected", version: 2 }));
  await page.evaluate(() => window.dispatchEvent(new Event("call:changed")));
  await expect(page.getByRole("button", { name: "Nhận cuộc gọi", exact: true })).toHaveCount(0);
});
test("mobile incoming controls fit the viewport", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await setup(page, true); await page.goto("/");
  await expect(page.getByRole("dialog")).toBeVisible();
  for (const name of ["Nhận cuộc gọi", "Từ chối"]) {
    const box = await page.getByRole("button", { name, exact: true }).boundingBox();
    expect(box!.width).toBeGreaterThanOrEqual(44); expect(box!.x).toBeGreaterThanOrEqual(0); expect(box!.x + box!.width).toBeLessThanOrEqual(390);
  }
  await page.screenshot({ path: "test-results/call-mobile.png" });
});

for (const mediaType of ["Audio", "Video"] as const) test(`two browser endpoints exchange real WebRTC ${mediaType} through signaling and clean up`, async ({ browser }) => {
  const contexts = await Promise.all([browser.newContext(), browser.newContext()]);
  const pages = await Promise.all(contexts.map(c => c.newPage()));
  const wires: Array<{ send: (data: string) => void } | undefined> = [];
  let call: ReturnType<typeof snapshot> | null = null;
  const endpoints: Array<{ endpointId: string } | null> = [null, null];
  const connected = new Set<number>(); let routed = 0;
  const view = (i: number) => call && { ...call, isCaller: i === 0, ownsEndpoint: !!endpoints[i], hasEndpoint: !!endpoints[i], generation: endpoints[i] ? 1 : 0,
    leaseExpiresAt: endpoints[i] ? new Date(Date.now() + 60000).toISOString() : null, serverTime: new Date().toISOString() };
  const notify = () => wires.forEach(w => w?.send(JSON.stringify({ type: 1, target: "CallStateChanged", arguments: [{ callId: call!.id, version: call!.version }] }) + "\u001e"));
  for (let i = 0; i < pages.length; i++) {
    const page = pages[i];
    const token = `header.${Buffer.from(JSON.stringify({ exp: 4102444800 })).toString("base64url")}.signature`;
    await page.route(/^https:\/\/(fonts\.(googleapis|gstatic)\.com|accounts.google.com)\//, r => r.abort());
    await page.routeWebSocket(/\/hubs\/chat/, ws => {
      wires[i] = ws;
      ws.onMessage(raw => {
        for (const part of String(raw).split("\u001e").filter(Boolean)) {
          const msg = JSON.parse(part);
          if (msg.protocol) { ws.send("{}\u001e"); continue; }
          if (msg.type !== 1) continue;
          const args = msg.arguments ?? []; let result: unknown = null;
          if (msg.target === "InitiateCall") {
            endpoints[i] = args[0].endpoint; call = snapshot({ clientCallId: args[0].clientCallId, type: mediaType }); result = view(i);
          } else if (msg.target === "UpdateCall") {
            const command = args[1];
            if (command.action === "accept") { endpoints[i] = command.endpoint; call = { ...call!, status: "Accepted", epoch: 1, version: 2 }; }
            if (command.action === "connected") {
              connected.add(i);
              if (connected.size === 2 && !(call as any).connectedAt) call = { ...call!, version: 3, connectedAt: new Date().toISOString() } as any;
            }
            result = view(i);
          } else if (msg.target === "SendSignal") {
            routed++;
            wires[1 - i]?.send(JSON.stringify({ type: 1, target: "ReceiveSignal", arguments: [{ callId: args[0], ...args[1] }] }) + "\u001e"); result = true;
          }
          ws.send(JSON.stringify({ type: 3, invocationId: msg.invocationId, result }) + "\u001e");
          if (call && ["InitiateCall", "UpdateCall"].includes(msg.target)) notify();
        }
      });
    });
    await page.route("**/hubs/chat/negotiate?*", r => r.fulfill({ json: { negotiateVersion: 1, connectionId: `c${i}`, connectionToken: `c${i}`, availableTransports: [{ transport: "WebSockets", transferFormats: ["Text", "Binary"] }] } }));
    await page.route(url => url.pathname.startsWith("/api/"), route => {
      const path = new URL(route.request().url()).pathname;
      const user = { id: i === 0 ? me : "44444444-4444-4444-8444-444444444444", username: `user${i}`, fullName: `User ${i}`, isVerified: true };
      const conversation = { id: room, name: "Minh Anh", type: "Direct", peerUserId: "peer", unreadCount: 0, createdAt: new Date().toISOString() };
      if (path.endsWith("/auth/refresh-token")) return route.fulfill({ json: { accessToken: token, user } });
      if (path.endsWith("/users/me")) return route.fulfill({ json: user });
      if (path.endsWith("/ice-servers")) return route.fulfill({ json: { iceServers: [], relayAvailable: true, expiresAt: new Date(Date.now() + 3600000).toISOString() } });
      if (path.endsWith("/end")) { call = { ...call!, status: "Ended", version: 5 }; notify(); return route.fulfill({ json: view(i) }); }
      if (path.includes("/calls/")) return route.fulfill({ json: view(i) });
      if (path.endsWith("/conversations")) return route.fulfill({ json: { items: [conversation], nextCursor: null } });
      if (path.endsWith("/messages")) return route.fulfill({ json: { messages: [], hasMore: false } });
      if (path.endsWith("/pins") || path.endsWith("/message-states")) return route.fulfill({ json: [] });
      if (path.includes("/conversations/")) return route.fulfill({ json: conversation });
      return route.fulfill({ json: [] });
    });
    await page.goto("/");
  }
  try {
    await pages[0].getByRole("button", { name: mediaType === "Video" ? "Gọi video" : "Gọi thoại", exact: true }).click();
    await pages[1].getByRole("button", { name: "Nhận cuộc gọi", exact: true }).click();
    for (const page of pages) {
      await expect.poll(() => page.evaluate(async () => {
        const path = "/src/store/callStore.ts"; const m = await import(path); const s = m.useCallStore.getState();
        return s.connected && s.remote?.getAudioTracks().length > 0;
      }), { timeout: 15000 }).toBeTruthy();
    }
    expect(routed).toBeGreaterThan(2); await expect.poll(() => connected.size).toBe(2);
    if (mediaType === "Video") {
      for (const page of pages) await expect.poll(() => page.locator(".call-screen__remote").evaluate((el: HTMLVideoElement) => el.videoWidth)).toBeGreaterThan(0);
      await pages[0].getByRole("button", { name: "Tắt camera", exact: true }).click();
      await expect(pages[0].getByRole("button", { name: "Bật camera", exact: true })).toHaveAttribute("aria-pressed", "true");
    }
    await pages[0].getByRole("button", { name: "Tắt micro", exact: true }).click();
    await expect(pages[0].getByRole("button", { name: "Bật micro", exact: true })).toHaveAttribute("aria-pressed", "true");
    await pages[0].getByRole("button", { name: "Thu nhỏ cuộc gọi", exact: true }).click();
    await expect(pages[0].getByRole("dialog")).toBeHidden();
    const draft = pages[0].getByPlaceholder("Nhắn tin tới Minh Anh");
    await draft.fill("Vẫn nhắn tin khi đang gọi");
    await pages[0].getByRole("button", { name: "Mở lại cuộc gọi", exact: true }).click();
    await expect(pages[0].getByRole("dialog")).toBeVisible();
    await expect(draft).toHaveValue("Vẫn nhắn tin khi đang gọi");
    await pages[0].getByRole("button", { name: "Cúp máy", exact: true }).click();
    for (const page of pages) await expect(page.getByText("Cuộc gọi đã kết thúc", { exact: true })).toBeVisible();
  } finally { await Promise.all(contexts.map(c => c.close())); }
});
