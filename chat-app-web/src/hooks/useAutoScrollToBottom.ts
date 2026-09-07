import { useEffect, useRef, type MutableRefObject, type RefObject } from "react";
import type { ChatMessage } from "../types";

interface Options {
  conversationId: string | null;
  messages: ChatMessage[];
  currentUserId: string | undefined;
  scrollContainerRef: RefObject<HTMLDivElement>;
  isNearBottomRef: MutableRefObject<boolean>;
  onNewMessageWhileScrolledUp: () => void;
}

export function useAutoScrollToBottom({
  conversationId,
  messages,
  currentUserId,
  scrollContainerRef,
  isNearBottomRef,
  onNewMessageWhileScrolledUp,
}: Options) {
  const previousConversationIdRef = useRef<string | null>(null);
  const previousLastMessageIdRef = useRef<string | null>(null);

  function scrollToBottom(behavior: ScrollBehavior) {
    const container = scrollContainerRef.current;
    if (!container) return;
    container.scrollTo({ top: container.scrollHeight, behavior });
  }

  useEffect(() => {
    if (!conversationId) {
      previousConversationIdRef.current = null;
      previousLastMessageIdRef.current = null;
      return;
    }

    const conversationChanged = conversationId !== previousConversationIdRef.current;
    if (conversationChanged) {
      previousConversationIdRef.current = conversationId;
      previousLastMessageIdRef.current = null;
    }

    if (messages.length === 0) return;

    const newLastMessage = messages[messages.length - 1];
    const previousLastMessageId = previousLastMessageIdRef.current;

    if (previousLastMessageId === null) {
      previousLastMessageIdRef.current = newLastMessage.id;
      requestAnimationFrame(() => scrollToBottom("auto"));
      return;
    }

    if (newLastMessage.id === previousLastMessageId) return;
    previousLastMessageIdRef.current = newLastMessage.id;

    if (newLastMessage.senderId === currentUserId || isNearBottomRef.current) {
      requestAnimationFrame(() => scrollToBottom("smooth"));
    } else {
      onNewMessageWhileScrolledUp();
    }
  }, [conversationId, messages, currentUserId, scrollContainerRef, isNearBottomRef, onNewMessageWhileScrolledUp]);

  return { scrollToBottom };
}