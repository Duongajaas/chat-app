import { create } from "zustand";
import type { ChatMessage, Conversation } from "../types";
import { mergeMessages } from "../utils/messageUtils";

interface ChatState {
  syncedThrough: Record<string, number>;
  setSyncedThrough: (id: string, sequence: number) => void;
  reset: () => void;
  connectionStatus: "connecting" | "connected" | "disconnected";
  setConnectionStatus: (status: "connecting" | "connected" | "disconnected") => void;
  failMessage: (conversationId: string, clientMessageId: string) => void;
  hideMessage: (conversationId: string, messageId: string) => void;
  hiddenMessageIds: Record<string, boolean>;
  deletedMessageIds: Record<string, boolean>;
  deleteMessage: (conversationId: string, messageId: string) => void;
  conversations: Conversation[];
  activeConversationId: string | null;
  messagesByConversation: Record<string, ChatMessage[]>;
  hasMoreByConversation: Record<string, boolean>;
  isLoadingOlderByConversation: Record<string, boolean>;
  hasNewMessageBelowByConversation: Record<string, boolean>;
  setConversations: (conversations: Conversation[]) => void;
  updateConversation: (conversationId: string, next: Partial<Conversation>) => void;
  setActiveConversationId: (conversationId: string | null) => void;
  setMessagesForConversation: (conversationId: string, messages: ChatMessage[]) => void;
  prependMessages: (conversationId: string, older: ChatMessage[]) => void;
  appendMessage: (conversationId: string, message: ChatMessage) => void;
  syncNewerMessages: (conversationId: string, newer: ChatMessage[]) => void;
  updateMessage: (conversationId: string, messageId: string, next: Partial<ChatMessage>) => void;
  removeMessage: (conversationId: string, messageId: string) => void;
  setHasMore: (conversationId: string, hasMore: boolean) => void;
  setIsLoadingOlder: (conversationId: string, isLoading: boolean) => void;
  markNewMessageBelow: (conversationId: string) => void;
  clearNewMessageBelow: (conversationId: string) => void;
}

export const useChatStore = create<ChatState>((set) => ({
  syncedThrough: {},
  setSyncedThrough: (id, sequence) => set(state => ({ syncedThrough: { ...state.syncedThrough, [id]: Math.max(state.syncedThrough[id] ?? 0, sequence) } })),
  reset: () => set({ syncedThrough: {}, conversations: [], activeConversationId: null, messagesByConversation: {},
    hasMoreByConversation: {}, isLoadingOlderByConversation: {}, hasNewMessageBelowByConversation: {},
    hiddenMessageIds: {}, deletedMessageIds: {}, connectionStatus: "disconnected" }),
  connectionStatus: "disconnected",
  setConnectionStatus: (connectionStatus) => set({ connectionStatus }),
  hiddenMessageIds: {},
  deletedMessageIds: {},
  hideMessage: (conversationId, messageId) => set(state => ({
    hiddenMessageIds: { ...state.hiddenMessageIds, [messageId]: true },
    messagesByConversation: { ...state.messagesByConversation, [conversationId]: (state.messagesByConversation[conversationId] ?? []).filter(m => m.id !== messageId) },
  })),
  deleteMessage: (conversationId, messageId) => set(state => ({
    deletedMessageIds: { ...state.deletedMessageIds, [messageId]: true },
    messagesByConversation: { ...state.messagesByConversation, [conversationId]: (state.messagesByConversation[conversationId] ?? []).map(m => m.id === messageId ? { ...m, status: "Deleted", content: "Tin nhắn đã được thu hồi" } : m) },
  })),
  failMessage: (conversationId, clientMessageId) => set(state => ({
    messagesByConversation: { ...state.messagesByConversation, [conversationId]: (state.messagesByConversation[conversationId] ?? []).map(m =>
      m.clientMessageId === clientMessageId && m.status === "Sending" ? { ...m, status: "Failed" } : m) },
  })),
  conversations: [],
  activeConversationId: null,
  messagesByConversation: {},
  hasMoreByConversation: {},
  isLoadingOlderByConversation: {},
  hasNewMessageBelowByConversation: {},

  setConversations: (conversations) => set({ conversations }),

  updateConversation: (conversationId, next) =>
    set((state) => ({
      conversations: state.conversations.map((conversation) =>
        conversation.id === conversationId ? { ...conversation, ...next } : conversation
      ),
    })),

  setActiveConversationId: (conversationId) => set({ activeConversationId: conversationId }),

  setMessagesForConversation: (conversationId, messages) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeVisible(state, [], messages),
      },
    })),

  prependMessages: (conversationId, older) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeVisible(state, state.messagesByConversation[conversationId] ?? [], older),
      },
    })),

  appendMessage: (conversationId, message) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeVisible(state, state.messagesByConversation[conversationId] ?? [], [message]),
      },
    })),

  syncNewerMessages: (conversationId, newer) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeVisible(state, state.messagesByConversation[conversationId] ?? [], newer),
      },
    })),

  updateMessage: (conversationId, messageId, next) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: (state.messagesByConversation[conversationId] ?? []).map((message) =>
          message.id === messageId ? { ...message, ...next } : message
        ),
      },
    })),

  removeMessage: (conversationId, messageId) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: (state.messagesByConversation[conversationId] ?? []).filter((message) => message.id !== messageId),
      },
    })),

  setHasMore: (conversationId, hasMore) =>
    set((state) => ({
      hasMoreByConversation: { ...state.hasMoreByConversation, [conversationId]: hasMore },
    })),

  setIsLoadingOlder: (conversationId, isLoading) =>
    set((state) => ({
      isLoadingOlderByConversation: { ...state.isLoadingOlderByConversation, [conversationId]: isLoading },
    })),

  markNewMessageBelow: (conversationId) =>
    set((state) => ({
      hasNewMessageBelowByConversation: { ...state.hasNewMessageBelowByConversation, [conversationId]: true },
    })),

  clearNewMessageBelow: (conversationId) =>
    set((state) => ({
      hasNewMessageBelowByConversation: { ...state.hasNewMessageBelowByConversation, [conversationId]: false },
    })),
}));

function mergeVisible(state: ChatState, existing: ChatMessage[], incoming: ChatMessage[]) {
  return mergeMessages(existing, incoming).filter(m => !state.hiddenMessageIds[m.id]).map(m =>
    state.deletedMessageIds[m.id] ? { ...m, status: "Deleted" as const, content: "Tin nhắn đã được thu hồi" } : m);
}
