import { describe, expect, it } from "vitest";
import { getGiphyMediaUrl } from "../src/utils/giphy";
import { useChatStore } from "../src/store/chatStore";
import type { ChatMessage } from "../src/types";

const gifUrl = "https://media2.giphy.com/media/v1.Y2lkPTexample/example123/200.gif?cid=example";

describe("GIPHY messages", () => {
  it("recognizes GIPHY media URLs including rendition and tracking parameters", () => {
    expect(getGiphyMediaUrl(gifUrl)).toBe(gifUrl);
    expect(getGiphyMediaUrl("https://media.giphy.com/media/abc123/giphy.gif")).not.toBeNull();
  });

  it.each([
    "Hello", "https://giphy.com/gifs/example", "http://media.giphy.com/media/abc/giphy.gif",
    "https://media.giphy.com.evil.test/media/abc/giphy.gif",
    "https://evil.test/media/abc/giphy.gif", "javascript:alert(1)",
    "https://user:password@media.giphy.com/media/abc/giphy.gif",
    "https://media.giphy.com/media/abc/file.html",
  ])("keeps non-media or untrusted content as plain text: %s", content => {
    expect(getGiphyMediaUrl(content)).toBeNull();
  });

  it("deduplicates a GIF send acknowledgement and prevents late events undoing a recall", () => {
    useChatStore.getState().reset();
    const message: ChatMessage = {
      id: "client-id", clientMessageId: "client-id", conversationId: "conversation", senderId: "sender",
      sequence: Number.MAX_VALUE, content: gifUrl, createdAt: "2026-01-01T00:00:00Z", status: "Sending",
    };
    useChatStore.getState().appendMessage("conversation", message);
    const saved = { ...message, id: "server-id", sequence: 1, status: "Sent" as const };
    useChatStore.getState().appendMessage("conversation", saved);
    expect(useChatStore.getState().messagesByConversation.conversation).toEqual([saved]);
    useChatStore.getState().deleteMessage("conversation", saved.id);
    useChatStore.getState().appendMessage("conversation", saved);
    const recalled = useChatStore.getState().messagesByConversation.conversation[0];
    expect(recalled.status).toBe("Deleted");
    expect(getGiphyMediaUrl(recalled.content)).toBeNull();
    useChatStore.getState().reset();
  });
});
