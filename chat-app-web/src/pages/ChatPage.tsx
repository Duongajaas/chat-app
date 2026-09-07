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
import { joinConversation, leaveConversation, markConversationAsRead } from "../realtime/connection";
import { useAuth } from "../context/AuthContext";
import type { ChatMessage, Conversation } from "../types";

function normalizeConversation(item: any): Conversation {
  const id = String(item.id);
  const name = item.name ?? "Cuộc trò chuyện";
  const palette = ["#33d6a6", "#7fa8ff", "#ff9f6b", "#c792ea", "#f4c95d", "#ff7aa2"];
  const index = Array.from(id).reduce((sum, ch) => sum + ch.charCodeAt(0), 0) % palette.length;

  return {
    id,
    name,
    type: item.type === "Group" ? "Group" : "Direct",
    avatarColor: palette[index],
    lastMessage: item.lastMessage ?? "Bắt đầu cuộc trò chuyện",
    lastMessageAt: item.lastMessageAt ? new Date(item.lastMessageAt).toLocaleString("vi-VN", { hour: "2-digit", minute: "2-digit" }) : "Gần đây",
    unreadCount: Number(item.unreadCount ?? 0),
    isOnline: false,
    isBlocked: Boolean(item.isBlocked),
    requestStatus: item.requestStatus ?? "Accepted",
    peerUserId: item.peerUserId ?? item.otherUserId,
    createdAt: item.createdAt,
    updatedAt: item.updatedAt,
    lastMessageId: item.lastMessageId ?? null,
  };
}

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
  const updateMessage = useChatStore((state) => state.updateMessage);
  const removeMessage = useChatStore((state) => state.removeMessage);

  const [showMobileChat, setShowMobileChat] = useState(false);

  const activeConversationIdRef = useRef(activeConversationId);
  useEffect(() => {
    activeConversationIdRef.current = activeConversationId;
  }, [activeConversationId]);

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

        const normalized = list.map(normalizeConversation);
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
    const existing = messagesByConversation[activeId];
    if (existing && existing.length > 0) return;

    let isMounted = true;

    async function fetchMessages() {
      try {
        const response = await messagesApi.list(activeId);
        if (!isMounted) return;
        
        setMessagesForConversation(
          activeId,
          response.messages.map((item) => ({
            ...item,
            senderId: String(item.senderId),
            createdAt: new Date(item.createdAt).toLocaleString("vi-VN", { hour: "2-digit", minute: "2-digit" }),
          }))
        );
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
        if (isMounted) await markConversationAsRead(activeId);
      } catch (error) {
        console.error("Không thể tham gia realtime conversation:", error);
      }
    }

    enterConversation();
    return () => {
      isMounted = false;
      leaveConversation(activeId).catch(() => undefined);
    };
  }, [activeConversationId]);

  const activeConversation = conversations.find((conversation) => conversation.id === activeConversationId) ?? null;
  const activeMessages = activeConversationId ? messagesByConversation[activeConversationId] ?? [] : [];

  async function handleSend(content: string) {
    if (!activeConversationId || !user) return;

    const clientMessageId = crypto.randomUUID();
    const optimisticMessage: ChatMessage = {
      id: clientMessageId,
      conversationId: activeConversationId,
      senderId: user.id,
      sequence: Number.MAX_VALUE, // Optimistic → ở cuối tới khi server trả về thật
      content,
      createdAt: new Date().toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" }),
      clientMessageId,
      status: "Sending",
    };

    appendMessage(activeConversationId, optimisticMessage);

    try {
      await messagesApi.send(activeConversationId, { clientMessageId, content });
      // Broadcast từ server sẽ merge tự động qua appendMessage (xóa optimistic, thêm version thật)
      // Không cần updateMessageByClientId nữa
    } catch (error) {
      console.error("Gửi tin nhắn thất bại:", error);
      // Đánh dấu status = Failed, optimistic sẽ hiển thị as failed message
      const messages = messagesByConversation[activeConversationId] ?? [];
      const failedMsg = messages.find((m) => m.clientMessageId === clientMessageId);
      if (failedMsg) {
        updateMessage(activeConversationId, failedMsg.id, { status: "Failed" });
      }
    }
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
        conversations={conversations}
        activeId={activeConversationId}
        onSelect={handleSelectConversation}
        onAcceptRequest={handleAcceptRequest}
        onDeleteRequest={handleDeleteRequest}
      />
      <ChatWindow
        conversation={activeConversation}
        messages={activeMessages}
        onSendMessage={handleSend}
        onBlockUser={handleBlockUser}
        onBack={() => setShowMobileChat(false)}
        onDeleteMessage={async (messageId) => {
          await messagesApi.delete(messageId);
          if (activeConversationId) {
            updateMessage(activeConversationId, messageId, { content: "Tin nhắn đã được thu hồi", status: "Deleted" });
          }
        }}
        onDeleteForMe={(messageId) => {
          if (activeConversationId) removeMessage(activeConversationId, messageId);
        }}
      />
    </div>
  );
}
