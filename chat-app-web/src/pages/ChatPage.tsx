import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { Sidebar } from "../components/Sidebar";
import { ConversationList } from "../components/ConversationList";
import { ChatWindow } from "../components/ChatWindow";
import { conversationsApi } from "../api/conversations";
import { messagesApi } from "../api/messages";
import { friendRequestsApi } from "../api/friendRequests";
import { blocksApi } from "../api/blocks";
import { useChatStore } from "../store/chatStore";
import { joinConversation, leaveConversation, syncConversation, refreshConversation } from "../realtime/connection";
import { useAuth } from "../context/AuthContext";
import { getSessionVersion } from "../api/client";
import { normalizeConversation } from "../utils/conversationUtils";
import type { ChatMessage } from "../types";

export default function ChatPage() {
  const { user } = useAuth();
  const location = useLocation();
  const requestedConversationId = (location.state as { activeConversationId?: string } | null)?.activeConversationId ?? null;
  const conversations = useChatStore((state) => state.conversations);
  const activeConversationId = useChatStore((state) => state.activeConversationId);
  const messagesByConversation = useChatStore((state) => state.messagesByConversation);
  const setConversations = useChatStore((state) => state.setConversations);
  const updateConversation = useChatStore((state) => state.updateConversation);
  const setActiveConversationId = useChatStore((state) => state.setActiveConversationId);
  const setMessagesForConversation = useChatStore((state) => state.setMessagesForConversation);
  const appendMessage = useChatStore((state) => state.appendMessage);

  const connectionStatus = useChatStore(state => state.connectionStatus);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [showMobileChat, setShowMobileChat] = useState(false);

  const activeConversationIdRef = useRef(activeConversationId);
  useEffect(() => {
    activeConversationIdRef.current = activeConversationId;
  }, [activeConversationId, connectionStatus]);

  useEffect(() => {
    if (requestedConversationId) {
      setActiveConversationId(requestedConversationId);
    }
  }, [requestedConversationId, setActiveConversationId]);

  useEffect(() => {
    let isMounted = true;

    async function fetchConversations() {
      try {
        const list = await conversationsApi.list();
        if (!isMounted) return;

        const normalized = list.items.map(normalizeConversation);
        setNextCursor(list.nextCursor);
        const requestedConversation = requestedConversationId && !normalized.some((conversation) => conversation.id === requestedConversationId)
          ? normalizeConversation(await conversationsApi.getById(requestedConversationId))
          : null;
        if (!isMounted) return;

        setConversations(requestedConversation ? [...normalized, requestedConversation] : normalized);

        if (!requestedConversationId && !activeConversationIdRef.current && normalized[0]) {
          setActiveConversationId(normalized[0].id);
        }
      } catch (error) {
        console.error("Không thể tải danh sách hội thoại:", error);
      }
    }

    fetchConversations();
    return () => {
      isMounted = false;
    };
  }, [requestedConversationId, setActiveConversationId, setConversations]);

  useEffect(() => {
    if (!activeConversationId) return;

    const activeId = activeConversationId;
    const existing = useChatStore.getState().messagesByConversation[activeId];
    if (existing && existing.length > 0) {
      void syncConversation(activeId).catch(() => undefined);
      return;
    }

    let isMounted = true;

    async function fetchMessages() {
      try {
        const response = await messagesApi.list(activeId);
        if (!isMounted) return;
        
        useChatStore.getState().syncNewerMessages(activeId, response.messages);
        useChatStore.getState().setHasMore(activeId, response.hasMore);
        useChatStore.getState().setSyncedThrough(activeId, response.messages.reduce((max, m) => Math.max(max, m.sequence), 0));
      } catch (error) {
        console.error("Không thể tải tin nhắn:", error);
      }
    }

    fetchMessages();
    return () => {
      isMounted = false;
    };
  }, [activeConversationId, setMessagesForConversation]);

  useEffect(() => {
    if (!activeConversationId) return;

    let isMounted = true;
    const activeId = activeConversationId;

    async function enterConversation() {
      try {
        await joinConversation(activeId);
        if (!isMounted) await leaveConversation(activeId);
      } catch (error) {
        console.error("Không thể tham gia realtime conversation:", error);
      }
    }

    enterConversation();
    return () => {
      isMounted = false;
      leaveConversation(activeId).catch(() => undefined);
    };
  }, [activeConversationId, connectionStatus]);

  const activeConversation = conversations.find((conversation) => conversation.id === activeConversationId) ?? null;
  const activeMessages = activeConversationId ? messagesByConversation[activeConversationId] ?? [] : [];

  async function handleSend(content: string, retry?: ChatMessage) {
    const id = retry?.conversationId ?? activeConversationId;
    if (!id || !user) return;
    const version = getSessionVersion();
    const clientMessageId = retry?.clientMessageId ?? crypto.randomUUID();
    appendMessage(id, {
      id: clientMessageId, conversationId: id, senderId: user.id, sequence: Number.MAX_VALUE,
      content, createdAt: retry?.createdAt ?? new Date().toISOString(), clientMessageId, status: "Sending",
    });
    try {
      const response = await messagesApi.send(id, { clientMessageId, content });
      if (version !== getSessionVersion()) return;
      appendMessage(id, { ...response, status: response.status === "Deleted" ? "Deleted" : "Sent" });
      refreshConversation(id);
    } catch {
      if (version === getSessionVersion()) useChatStore.getState().failMessage(id, clientMessageId);
    }
  }

  async function loadMoreConversations() {
    if (!nextCursor || loadingMore) return;
    setLoadingMore(true);
    const version = getSessionVersion();
    try {
      const page = await conversationsApi.list(nextCursor);
      if (version !== getSessionVersion()) return;
      const items = new Map(useChatStore.getState().conversations.map(c => [c.id, c]));
      page.items.map(normalizeConversation).forEach(c => items.set(c.id, c));
      setConversations([...items.values()]);
      setNextCursor(page.nextCursor);
    } finally { if (version === getSessionVersion()) setLoadingMore(false); }
  }

  async function handleAcceptRequest(id: string) {
    await friendRequestsApi.accept(id);
    updateConversation(id, { requestStatus: "Accepted" });
  }

  async function handleDeleteRequest(id: string) {
    updateConversation(id, { requestStatus: "Rejected" });

    try {
      await conversationsApi.hide(id);
    } catch (error) {
      console.error("Không thể ẩn lời mời nhắn tin:", error);
      // Rollback nếu API lỗi — tránh UI nói "đã xóa" nhưng F5 lại nó quay về
      updateConversation(id, { requestStatus: "Pending" });
    }
  }

  async function handleBlockUser(userId: string) {
    if (!activeConversationId) return;

    await blocksApi.block(userId);
    updateConversation(activeConversationId, { isBlocked: true });
  }

  function handleSelectConversation(conversationId: string) {
    setActiveConversationId(conversationId);
    // On mobile, show chat window when conversation is selected
    setShowMobileChat(true);
  }

  return (
    <div className={`app-shell ${showMobileChat ? "app-shell--mobile-chat" : ""}`}>
      <Sidebar />
      <ConversationList
        conversations={[...conversations].sort((a, b) => Date.parse(b.lastMessageAt) - Date.parse(a.lastMessageAt))}
        hasMore={!!nextCursor}
        loadingMore={loadingMore}
        onLoadMore={() => { void loadMoreConversations().catch(() => undefined); }}
        activeId={activeConversationId}
        onSelect={handleSelectConversation}
        onAcceptRequest={handleAcceptRequest}
        onDeleteRequest={handleDeleteRequest}
      />
      <ChatWindow
        conversation={activeConversation}
        messages={activeMessages}
        onSendMessage={handleSend}
        onRetryMessage={(message) => { void handleSend(message.content, message); }}
        onBlockUser={handleBlockUser}
        onBack={() => setShowMobileChat(false)}
        onDeleteMessage={async (messageId) => {
          const version = getSessionVersion();
          const id = activeConversationId;
          await messagesApi.delete(messageId);
          if (id && version === getSessionVersion()) useChatStore.getState().deleteMessage(id, messageId);
        }}
        onDeleteForMe={async (messageId) => {
          const version = getSessionVersion();
          const id = activeConversationId;
          await messagesApi.deleteForMe(messageId);
          if (id && version === getSessionVersion()) useChatStore.getState().hideMessage(id, messageId);
        }}
      />
    </div>
  );
}
