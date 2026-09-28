import { beforeEach, expect, it } from "vitest";
import { collectMentions } from "../src/utils/mentions";
import { useChatStore } from "../src/store/chatStore";
import type { ChatMessage } from "../src/types";

beforeEach(() => useChatStore.getState().reset());
it("collects confirmed usernames with UTF-16 positions without matching email addresses", () => {
  expect(collectMentions("😀 @An mail@An @unknown", [{ userId: "an-id", username: "an" }]))
    .toEqual([{ userId: "an-id", start: 3, length: 3 }]);
});
const reply: ChatMessage = { id: "reply", conversationId: "group", senderId: "a", sequence: 20,
  content: "reply body", createdAt: "2026-09-14", status: "Sent", replyToMessageId: "original",
  replyPreview: { id: "original", isAvailable: true, contentSnippet: "secret", senderName: "A" } };
it.each(["deleteMessage", "hideMessage"] as const)("%s prevents a delayed response restoring a quote", action => {
  const store = useChatStore.getState();
  store.appendMessage("group", reply);
  store[action]("group", "original");
  store.appendMessage("group", reply);
  const cached = useChatStore.getState().messagesByConversation.group[0];
  expect(cached.content).toBe("reply body");
  expect(cached.replyPreview?.isAvailable).toBe(false);
  expect(cached.replyPreview?.contentSnippet).toBeNull();
});
it("recall clears quote, mentions and attachment metadata on late ACK", () => {
  const store = useChatStore.getState();
  store.deleteMessage("group", reply.id);
  store.appendMessage("group", { ...reply, mentions: [{ userId: "a", username: "a", start: 0, length: 2 }],
    attachments: [{ id: "attachment", fileName: "secret.pdf" }] });
  const cached = useChatStore.getState().messagesByConversation.group[0];
  expect(cached.replyPreview).toBeNull(); expect(cached.mentions).toEqual([]); expect(cached.attachments).toEqual([]);
});
