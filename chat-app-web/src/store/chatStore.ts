import { create } from "zustand";
import type { ChatMessage, Conversation } from "../types";
import { mergeMessages } from "../utils/messageUtils";

interface ChatState {
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
        [conversationId]: mergeMessages([], messages),
      },
    })),

  prependMessages: (conversationId, older) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeMessages(state.messagesByConversation[conversationId] ?? [], older),
      },
    })),

  appendMessage: (conversationId, message) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeMessages(state.messagesByConversation[conversationId] ?? [], [message]),
      },
    })),

  syncNewerMessages: (conversationId, newer) =>
    set((state) => ({
      messagesByConversation: {
        ...state.messagesByConversation,
        [conversationId]: mergeMessages(state.messagesByConversation[conversationId] ?? [], newer),
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
