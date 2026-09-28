import { create } from "zustand";

interface PresenceState {
  reset: () => void;
  onlineUserIds: Set<string>;
  typingByConversation: Record<string, boolean>;
  readByConversation: Record<string, number>;
  setOnlineUserIds: (userIds: string[]) => void;
  setUserPresence: (userId: string, isOnline: boolean) => void;
  setTypingForConversation: (conversationId: string, isTyping: boolean) => void;
  setConversationRead: (conversationId: string, sequence: number) => void;
}

const typingTimers = new Map<string, ReturnType<typeof setTimeout>>();

export const usePresenceStore = create<PresenceState>((set) => ({
  reset: () => {
    typingTimers.forEach(clearTimeout);
    typingTimers.clear();
    set({ onlineUserIds: new Set(), typingByConversation: {}, readByConversation: {} });
  },
  onlineUserIds: new Set(),
  typingByConversation: {},
  readByConversation: {},

  setOnlineUserIds: (userIds) => set({ onlineUserIds: new Set(userIds) }),

  setUserPresence: (userId, isOnline) =>
    set((state) => {
      const next = new Set(state.onlineUserIds);
      if (isOnline) next.add(userId);
      else next.delete(userId);
      return { onlineUserIds: next };
    }),

  setTypingForConversation: (conversationId, isTyping) =>
    set((state) => {
      const nextTyping = { ...state.typingByConversation, [conversationId]: isTyping };
      const currentTimer = typingTimers.get(conversationId);
      if (currentTimer) clearTimeout(currentTimer);

      if (isTyping) {
        typingTimers.set(
          conversationId,
          setTimeout(() => {
            set((currentState) => ({
              typingByConversation: { ...currentState.typingByConversation, [conversationId]: false },
            }));
            typingTimers.delete(conversationId);
          }, 3000)
        );
      } else {
        typingTimers.delete(conversationId);
      }

      return { typingByConversation: nextTyping };
    }),

  setConversationRead: (conversationId, sequence) =>
    set((state) => ({
      readByConversation: { ...state.readByConversation, [conversationId]: Math.max(state.readByConversation[conversationId] ?? 0, sequence) },
    })),
}));
