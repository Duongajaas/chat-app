import { useCallback, useRef } from "react";
import { getSessionVersion } from "../api/client";
import { messagesApi } from "../api/messages";
import { useChatStore } from "../store/chatStore";

export function useLoadOlderMessages(conversationId: string | null) {
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const messagesByConversation = useChatStore((state) => state.messagesByConversation);
  const isLoadingOlderByConversation = useChatStore((state) => state.isLoadingOlderByConversation);
  const hasMoreByConversation = useChatStore((state) => state.hasMoreByConversation);
  const prependMessages = useChatStore((state) => state.prependMessages);
  const setHasMore = useChatStore((state) => state.setHasMore);
  const setIsLoadingOlder = useChatStore((state) => state.setIsLoadingOlder);

  const isLoading = conversationId ? isLoadingOlderByConversation[conversationId] ?? false : false;
  const hasMore = conversationId ? hasMoreByConversation[conversationId] ?? true : true;
  const messages = conversationId ? messagesByConversation[conversationId] ?? [] : [];

  const loadOlder = useCallback(async () => {
    if (!conversationId || isLoading || !hasMore || messages.length === 0) return;

    const version = getSessionVersion();
    setIsLoadingOlder(conversationId, true);

    try {
      // Get oldest sequence from current messages
      const oldestSequence = Math.min(...messages.map((m) => m.sequence ?? Number.MAX_VALUE));
      if (oldestSequence === Number.MAX_VALUE) {
        // No valid sequences yet
        setHasMore(conversationId, false);
        return;
      }

      // Fetch older messages (before = oldest sequence)
      const response = await messagesApi.list(conversationId, oldestSequence, undefined, 50);
      if (version !== getSessionVersion()) return;
      const olderMessages = response.messages;

      if (olderMessages.length > 0) {
        prependMessages(conversationId, olderMessages);
      }

      // Update hasMore state
      setHasMore(conversationId, response.hasMore ?? false);
    } catch (error) {
      console.error("Failed to load older messages:", error);
      // Keep hasMore as is in case of error (allow retry)
    } finally {
      if (version === getSessionVersion()) setIsLoadingOlder(conversationId, false);
    }
  }, [conversationId, isLoading, hasMore, messages, prependMessages, setHasMore, setIsLoadingOlder]);

  return {
    scrollContainerRef,
    loadOlder,
    isLoading,
    hasMore,
  };
}
