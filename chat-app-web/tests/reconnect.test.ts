import { beforeEach, expect, it, vi } from "vitest";
const mocks = vi.hoisted(() => ({ list: vi.fn(), states: vi.fn(), getById: vi.fn(), conversations: vi.fn() }));
vi.mock("../src/api/messages", () => ({ messagesApi: mocks }));
vi.mock("../src/api/conversations", () => ({ conversationsApi: { getById: mocks.getById, list: mocks.conversations } }));
import { syncConversation, refreshConversationList } from "../src/realtime/connection";
import { useChatStore } from "../src/store/chatStore";
const message = (sequence: number) => ({ id: `message-${sequence}`, conversationId: "room", senderId: "alice", sequence, content: "hello", createdAt: "2026-09-08T10:00:00Z", status: "Sent" as const });
beforeEach(() => { vi.clearAllMocks(); useChatStore.getState().reset(); });
it("syncs from the last API cursor rather than a later out-of-order realtime event", async () => {
  const store = useChatStore.getState();
  store.appendMessage("room", message(10)); store.appendMessage("room", message(15));
  store.setSyncedThrough("room", 10); store.setHasMore("room", true);
  mocks.states.mockResolvedValue([10, 15].map(n => ({ id: `message-${n}`, deleted: false, hidden: false })));
  mocks.list.mockResolvedValueOnce({ messages: [message(11), message(12)], nextCursor: 12, hasMore: true })
    .mockResolvedValueOnce({ messages: [message(13), message(14), message(15)], nextCursor: 15, hasMore: false });
  await syncConversation("room");
  expect(mocks.list.mock.calls[0][2]).toBe(10);
  expect(mocks.list.mock.calls[1][2]).toBe(12);
  expect(useChatStore.getState().messagesByConversation.room.map(m => m.sequence)).toEqual([10, 11, 12, 13, 14, 15]);
  expect(useChatStore.getState().hasMoreByConversation.room).toBe(true);
});
it("reconciles recalls that happened offline before the new-message cursor", async () => {
  const store = useChatStore.getState();
  store.appendMessage("room", message(10)); store.setSyncedThrough("room", 10);
  mocks.states.mockResolvedValue([{ id: "message-10", deleted: true, hidden: false }]);
  mocks.list.mockResolvedValue({ messages: [], hasMore: false });
  await syncConversation("room");
  expect(useChatStore.getState().messagesByConversation.room[0].status).toBe("Deleted");
});

it("removes cached messages outside membership periods after rejoin", async () => {
  const store = useChatStore.getState();
  store.appendMessage("room", message(10)); store.appendMessage("room", message(20));
  store.setSyncedThrough("room", 20);
  mocks.states.mockResolvedValue([{ id: "message-10", deleted: false, hidden: false }]);
  mocks.list.mockResolvedValue({ messages: [message(30)], hasMore: false });
  await syncConversation("room");
  expect(useChatStore.getState().messagesByConversation.room.map(m => m.sequence)).toEqual([10, 30]);
});

it("discovers an old group added on a later metadata page after a missed event", async () => {
  mocks.conversations.mockResolvedValueOnce({ items: [{ id: "newer", name: "Newer", type: "Group", version: 1 }], nextCursor: "page2" })
    .mockResolvedValueOnce({ items: [{ id: "old-group", name: "Old group", type: "Group", version: 4 }], nextCursor: null });
  await refreshConversationList();
  expect(mocks.conversations.mock.calls[1][0]).toBe("page2");
  expect(useChatStore.getState().conversations.map(c => c.id)).toEqual(["newer", "old-group"]);
});
