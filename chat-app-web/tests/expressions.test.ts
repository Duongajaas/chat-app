import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { insertEmojiAtSelection, isSafeExpressionUrl, readExpressionHistory, rememberEmoji, rememberSticker, saveExpressionHistory, toggleFavoriteSticker } from "../src/utils/expressions";
import { mergeMessages } from "../src/utils/messageUtils";
import type { ChatMessage } from "../src/types";

describe("expression composer", () => {
  it("inserts Unicode sequences at the caret and replaces only the selected text", () => {
    expect(insertEmojiAtSelection("Xin chào", "👩🏽‍💻", 4, 4)).toEqual({ value: "Xin 👩🏽‍💻chào", caret: 11 });
    expect(insertEmojiAtSelection("Xin chào", "❤️", 4, 8)).toEqual({ value: "Xin ❤️", caret: 6 });
  });
  it("preserves the message when a complete emoji would exceed the server limit", () => {
    expect(insertEmojiAtSelection("x".repeat(3999), "😀", 3999, 3999)).toBeNull();
    expect(insertEmojiAtSelection("x".repeat(4000), "😀", 3998, 4000)?.value.length).toBe(4000);
  });
});

describe("private expression history", () => {
  const values = new Map<string, string>();
  beforeEach(() => { values.clear(); vi.stubGlobal("localStorage", { getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => values.set(key, value) }); });
  afterEach(() => vi.unstubAllGlobals());
  it("keeps recent/frequent emoji and stickers separate for each signed-in account", () => {
    let history = rememberEmoji(readExpressionHistory("Alice"), "😀", "1f600");
    history = rememberEmoji(history, "😀", "1f600");
    history = rememberSticker(rememberSticker(history, "cat-happy"), "cat-happy");
    history = toggleFavoriteSticker(history, "cat-happy");
    saveExpressionHistory("Alice", history);
    expect(readExpressionHistory("Alice").emojis[0].count).toBe(2);
    expect(readExpressionHistory("Alice").recentStickers).toEqual(["cat-happy"]);
    expect(readExpressionHistory("Alice").favoriteStickers).toEqual(["cat-happy"]);
    expect(readExpressionHistory("Bob").emojis).toEqual([]);
    expect(toggleFavoriteSticker(history, "cat-happy").favoriteStickers).toEqual([]);
  });
  it("recovers from corrupt or unavailable browser storage", () => {
    values.set("chatapp-expressions:Alice", "not json");
    expect(readExpressionHistory("Alice").emojis).toEqual([]);
    vi.stubGlobal("localStorage", { getItem: () => { throw new Error("disabled"); }, setItem: () => { throw new Error("full"); } });
    expect(readExpressionHistory("Alice").recentStickers).toEqual([]);
    expect(() => saveExpressionHistory("Alice", readExpressionHistory("Alice"))).not.toThrow();
  });
});

describe("expression safety and delivery", () => {
  it("allows only catalog asset paths and HTTPS GIPHY media without altering query parameters", () => {
    expect(isSafeExpressionUrl("/stickers/cat-happy.svg", "local")).toBe(true);
    for (const url of ["https://evil.test/a.svg", "/stickers/../secret.svg", "//evil.test/a.png"])
      expect(isSafeExpressionUrl(url, "local")).toBe(false);
    expect(isSafeExpressionUrl("https://media2.giphy.com/media/id/200.gif?cid=abc", "giphy")).toBe(true);
    for (const url of ["javascript:alert(1)", "https://media.giphy.com.evil.test/media/id/a.gif", "http://media.giphy.com/media/id/a.gif", "https://user@media.giphy.com/media/id/a.gif"])
      expect(isSafeExpressionUrl(url, "giphy")).toBe(false);
  });
  it("replaces an optimistic sticker with the ACK once and keeps recall over a delayed ACK", () => {
    const pending: ChatMessage = { id: "client", clientMessageId: "client", conversationId: "room", senderId: "Alice", content: "",
      sequence: Number.MAX_VALUE, createdAt: "2026-11-16T00:00:00Z", status: "Sending",
      expression: { kind: "sticker", provider: "local", id: "cat-happy", name: "Mèo vui", width: 240, height: 240 } };
    const ack: ChatMessage = { ...pending, id: "server", sequence: 10, status: "Sent" };
    expect(mergeMessages([pending], [ack, ack])).toEqual([ack]);
    const recalled: ChatMessage = { ...ack, status: "Deleted", expression: null };
    expect(mergeMessages([recalled], [ack])).toEqual([recalled]);
  });
});
