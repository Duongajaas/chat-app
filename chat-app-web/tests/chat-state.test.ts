import { beforeEach, describe, expect, it } from "vitest";
import { useChatStore } from "../src/store/chatStore";
import { usePresenceStore } from "../src/store/presenceStore";
import type { ChatMessage } from "../src/types";
const optimistic: ChatMessage = { id: "client", clientMessageId: "client", conversationId: "room", senderId: "alice", sequence: Number.MAX_VALUE, content: "hello", createdAt: "2026-09-08T10:00:00Z", status: "Sending" };
const confirmed: ChatMessage = { ...optimistic, id: "server", sequence: 42, status: "Sent" };
beforeEach(() => { useChatStore.getState().reset(); usePresenceStore.getState().reset(); });
describe("message reconciliation", () => {
  it("confirms by HTTP ACK even if the event never arrives and deduplicates a later event", () => {
    const store = useChatStore.getState();
    store.appendMessage("room", optimistic);
    store.appendMessage("room", confirmed);
    store.appendMessage("room", confirmed);
    expect(useChatStore.getState().messagesByConversation.room).toEqual([confirmed]);
  });
  it("does not downgrade a confirmed message on a late request failure", () => {
    useChatStore.getState().appendMessage("room", confirmed);
    useChatStore.getState().failMessage("room", "client");
    expect(useChatStore.getState().messagesByConversation.room[0].status).toBe("Sent");
  });
  it("marks a pending message failed using current state and retry replaces it", () => {
    const store = useChatStore.getState();
    store.appendMessage("room", optimistic); store.failMessage("room", "client");
    expect(useChatStore.getState().messagesByConversation.room[0].status).toBe("Failed");
    store.appendMessage("room", optimistic); store.appendMessage("room", confirmed);
    expect(useChatStore.getState().messagesByConversation.room).toEqual([confirmed]);
  });
  it("does not resurrect recalled or privately hidden messages from delayed ACKs", () => {
    const store = useChatStore.getState();
    store.deleteMessage("room", confirmed.id); store.appendMessage("room", confirmed);
    expect(useChatStore.getState().messagesByConversation.room[0].status).toBe("Deleted");
    store.hideMessage("room", confirmed.id); store.appendMessage("room", confirmed);
    expect(useChatStore.getState().messagesByConversation.room).toEqual([]);
  });
  it("keeps ISO timestamp intact", () => {
    useChatStore.getState().appendMessage("room", confirmed);
    expect(Date.parse(useChatStore.getState().messagesByConversation.room[0].createdAt)).toBe(Date.parse(confirmed.createdAt));
  });
  it("resets all account data including cursors, tombstones and presence", () => {
    const store = useChatStore.getState();
    store.appendMessage("room", confirmed); store.setActiveConversationId("room");
    store.setSyncedThrough("room", 42); store.hideMessage("room", confirmed.id);
    usePresenceStore.getState().setUserPresence("alice", true);
    usePresenceStore.getState().setConversationRead("room", 42);
    store.reset(); usePresenceStore.getState().reset();
    expect(useChatStore.getState().messagesByConversation).toEqual({});
    expect(useChatStore.getState().activeConversationId).toBeNull();
    expect(useChatStore.getState().syncedThrough).toEqual({});
    expect(useChatStore.getState().hiddenMessageIds).toEqual({});
    expect(usePresenceStore.getState().onlineUserIds.size).toBe(0);
  });
  it("read receipts are monotonic and do not imply future messages were read", () => {
    usePresenceStore.getState().setConversationRead("room", 42);
    usePresenceStore.getState().setConversationRead("room", 40);
    expect(usePresenceStore.getState().readByConversation.room).toBe(42);
  });
});
