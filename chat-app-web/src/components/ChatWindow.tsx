import { useEffect, useRef, useState, type FormEvent } from "react";
import { GroupSettings } from "./GroupSettings";
import { Avatar } from "./Avatar";
import { PhoneIcon, VideoIcon, SendIcon, PaperclipIcon, SmileIcon, MoreIcon } from "./icons";
import type { Conversation, ChatMessage } from "../types";
import { sendTyping, markConversationAsRead } from "../realtime/connection";
import { usePresenceStore } from "../store/presenceStore";
import { useChatStore } from "../store/chatStore";
import { useAuth } from "../context/AuthContext";
import { useLoadOlderMessages } from "../hooks/useLoadOlderMessages";
import { useAutoScrollToBottom } from "../hooks/useAutoScrollToBottom";

interface ChatWindowProps {
  conversation: Conversation | null;
  messages: ChatMessage[];
  onSendMessage: (content: string) => void;
  onDeleteMessage: (messageId: string) => Promise<void>;
  onDeleteForMe: (messageId: string) => Promise<void>;
  onRetryMessage: (message: ChatMessage) => void;
  onBlockUser: (userId: string) => Promise<void>;
  onBack?: () => void;
}

export function ChatWindow({ conversation, messages, onSendMessage, onDeleteMessage, onDeleteForMe, onBlockUser, onBack, onRetryMessage }: ChatWindowProps) {
  const connectionStatus = useChatStore(state => state.connectionStatus);
  const [showGroup, setShowGroup] = useState(false);
  const [actionError, setActionError] = useState("");
  const [nearBottom, setNearBottom] = useState(true);
  const [visible, setVisible] = useState(document.visibilityState === "visible");
  const [draft, setDraft] = useState("");
  const [openMessageId, setOpenMessageId] = useState<string | null>(null);
  const [isActionsMenuOpen, setIsActionsMenuOpen] = useState(false);
  const [isBlocking, setIsBlocking] = useState(false);
  const [deletingMessageId, setDeletingMessageId] = useState<string | null>(null);
  const onlineUserIds = usePresenceStore((state) => state.onlineUserIds);
  const isTyping = usePresenceStore((state) => conversation ? state.typingByConversation[conversation.id] : false);
  const hasRead = usePresenceStore((state) => conversation ? state.readByConversation[conversation.id] : 0);
  const { user } = useAuth();
  const typingStartTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const typingStopTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const bottomSentinelRef = useRef<HTMLDivElement>(null);
  const isNearBottomRef = useRef(true);
  const { loadOlder, isLoading: isLoadingOlder, hasMore, scrollContainerRef } = useLoadOlderMessages(conversation?.id ?? null);
  const hasNewMessageBelow = useChatStore((state) => conversation ? state.hasNewMessageBelowByConversation[conversation.id] ?? false : false);
  const markNewMessageBelow = useChatStore((state) => state.markNewMessageBelow);
  const clearNewMessageBelow = useChatStore((state) => state.clearNewMessageBelow);
  const { scrollToBottom } = useAutoScrollToBottom({
    conversationId: conversation?.id ?? null,
    messages,
    currentUserId: user?.id,
    scrollContainerRef,
    isNearBottomRef,
    onNewMessageWhileScrolledUp: () => {
      if (conversation) markNewMessageBelow(conversation.id);
    },
  });

  useEffect(() => () => {
    if (typingStartTimer.current) clearTimeout(typingStartTimer.current);
    if (typingStopTimer.current) clearTimeout(typingStopTimer.current);
  }, []);

  useEffect(() => {
    setIsActionsMenuOpen(false);
    setShowGroup(false);
  }, [conversation?.id]);

  useEffect(() => {
    const sentinel = bottomSentinelRef.current;
    const container = scrollContainerRef.current;
    if (!sentinel || !container) return;

    const observer = new IntersectionObserver(
      ([entry]) => {
        const nearBottom = entry.isIntersecting;
        isNearBottomRef.current = nearBottom;
        setNearBottom(nearBottom);
        if (nearBottom && conversation) clearNewMessageBelow(conversation.id);
      },
      { root: container, threshold: 0.1 }
    );

    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [conversation?.id, clearNewMessageBelow, scrollContainerRef]);

  useEffect(() => {
    const update = () => setVisible(document.visibilityState === "visible" && document.hasFocus());
    document.addEventListener("visibilitychange", update);
    window.addEventListener("focus", update);
    window.addEventListener("blur", update);
    update();
    return () => {
      document.removeEventListener("visibilitychange", update);
      window.removeEventListener("focus", update);
      window.removeEventListener("blur", update);
    };
  }, []);

  const lastSequence = messages.reduce((max, m) => Number.isSafeInteger(m.sequence) && m.sequence < Number.MAX_SAFE_INTEGER ? Math.max(max, m.sequence) : max, 0);
  useEffect(() => {
    if (!conversation || conversation.hasLeft || conversation.closedAt || !nearBottom || !visible || !lastSequence || connectionStatus !== "connected") return;
    const timer = setTimeout(() => { void markConversationAsRead(conversation.id, lastSequence).catch(() => undefined); }, 300);
    return () => clearTimeout(timer);
  }, [conversation?.id, conversation?.hasLeft, conversation?.closedAt, nearBottom, visible, lastSequence, connectionStatus]);

  if (!conversation) {
    return (
      <section className="chat-window chat-window--empty">
        <p>Chọn một cuộc trò chuyện để bắt đầu nhắn tin</p>
      </section>
    );
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!draft.trim() || conversation?.hasLeft || conversation?.closedAt || conversation?.isBlocked) return;
    onSendMessage(draft.trim());
    setDraft("");
  }

  const conversationId = conversation.id;
  const isBlocked = conversation.isBlocked === true || conversation.hasLeft === true || !!conversation.closedAt;
  const isDirectOnline = !isBlocked && conversation.type === "Direct" && (conversation.peerUserId ? onlineUserIds.has(conversation.peerUserId) : conversation.isOnline);
  const isAdmin = conversation.isAdmin === true;

  function handleDraftChange(value: string) {
    setDraft(value);
    if (typingStartTimer.current) clearTimeout(typingStartTimer.current);
    if (typingStopTimer.current) clearTimeout(typingStopTimer.current);

    if (!value.trim()) {
      sendTyping(conversationId, false).catch(() => undefined);
      return;
    }

    typingStartTimer.current = setTimeout(() => {
      sendTyping(conversationId, true).catch(() => undefined);
    }, 400);
    typingStopTimer.current = setTimeout(() => {
      sendTyping(conversationId, false).catch(() => undefined);
    }, 3400);
  }

  function canDeleteForEveryone(message: ChatMessage) {
    if (message.status === "Deleted" || message.status === "Sending" || message.status === "Failed") return false;
    const messageIsMine = message.senderId === user?.id;
    const withinWindow = Date.now() - new Date(message.createdAt).getTime() < 15 * 60 * 1000;
    return !isBlocked && ((messageIsMine && withinWindow) || isAdmin);
  }

  async function deleteForEveryone(message: ChatMessage) {
    setDeletingMessageId(message.id);
    try {
      await onDeleteMessage(message.id);
      setOpenMessageId(null);
    } catch (error) {
      setActionError("Không thể xóa tin nhắn. Vui lòng thử lại.");
    } finally {
      setDeletingMessageId(null);
    }
  }

  async function handleBlockUser() {
    if (!conversation?.peerUserId || isBlocking) return;

    const confirmed = window.confirm("Chặn người này? Hai bạn sẽ không thể nhắn tin riêng cho nhau.");
    if (!confirmed) return;

    setIsBlocking(true);
    try {
      await onBlockUser(conversation.peerUserId);
      setDraft("");
      setIsActionsMenuOpen(false);
    } catch {
      window.alert("Không thể chặn người dùng này.");
    } finally {
      setIsBlocking(false);
    }
  }

  return (
    <section className="chat-window">
      <header className="chat-window__header">
        {onBack && <button className="chat-window__back-btn icon-btn" onClick={onBack} title="Quay lại"><span>←</span></button>}
        <div className="chat-window__peer">
          <Avatar name={conversation.name} color={conversation.avatarColor} isOnline={isDirectOnline} size={40} />
          <div>
            <div className="chat-window__name">{conversation.name}</div>
            <div className="chat-window__status">
              {conversation.type === "Direct" ? (isDirectOnline ? "Đang hoạt động" : "Không hoạt động") : "Nhóm chat"}
            </div>
          </div>
        </div>
        <div className="chat-window__actions">
          {conversation.type === "Group" && <button onClick={() => setShowGroup(v => !v)}>Thông tin nhóm</button>}
          <button className="icon-btn" title={isBlocked ? "Không thể gọi" : "Gọi thoại (sắp có)"} disabled={isBlocked}>
            <PhoneIcon />
          </button>
          <button className="icon-btn" title={isBlocked ? "Không thể gọi" : "Gọi video (sắp có)"} disabled={isBlocked}>
            <VideoIcon />
          </button>
          {conversation.type === "Direct" && conversation.peerUserId && (
            <div className="chat-window__action-menu">
              <button type="button" className="icon-btn" title="Tùy chọn" onClick={() => setIsActionsMenuOpen((open) => !open)}>
                <MoreIcon />
              </button>
              {isActionsMenuOpen && (
                <div className="chat-window__menu" role="menu">
                  <button type="button" onClick={handleBlockUser} disabled={isBlocking}>
                    {isBlocking ? "Đang chặn..." : "Chặn người này"}
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      </header>

      {showGroup && conversation.type === "Group" && <GroupSettings key={conversation.id} conversation={conversation} onClose={() => setShowGroup(false)} />}
      {(conversation.hasLeft || conversation.closedAt) && <p role="status">Chỉ đọc lịch sử trong những khoảng bạn là thành viên.</p>}
      {connectionStatus !== "connected" && <p role="status">{connectionStatus === "connecting" ? "Đang kết nối lại…" : "Mất kết nối. Ứng dụng sẽ tự thử lại."}</p>}
      {actionError && <p role="alert">{actionError}</p>}
      <div className="chat-window__messages" ref={scrollContainerRef}>
        {hasMore && (
          <div className="chat-window__load-older">
            <button 
              onClick={loadOlder} 
              disabled={isLoadingOlder}
              className="chat-window__load-older-btn"
            >
              {isLoadingOlder ? "Đang tải..." : "Tải tin nhắn cũ"}
            </button>
          </div>
        )}
        {messages.map((m, index) => {
          const isMine = m.senderId === user?.id;
          // console.log("Rendering message", m.id, "isMine:", isMine, "content:", m.content, "userId:", m.senderId, "currentUserId:", user?.id);
          return (
          <div key={m.id} className={`message-bubble-row ${isMine ? "is-me" : ""}`}>
            <div className="message-bubble" onContextMenu={(event) => { event.preventDefault(); setOpenMessageId(m.id); }}>
              {m.status === "Deleted" ? <p className="message-bubble__deleted">Tin nhắn đã được thu hồi</p> : <p>{m.content}</p>}
              <span className="message-bubble__time">
                {new Date(m.createdAt).toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })}
                {m.status === "Failed" && <button type="button" onClick={() => onRetryMessage(m)}>Gửi lại</button>}
              </span>
              {conversation.type === "Direct" && (hasRead ?? 0) >= m.sequence && isMine && index === messages.length - 1 && <span className="message-bubble__time">Đã xem</span>}
              {openMessageId === m.id && (
                <div className="message-menu" role="menu">
                  {isMine && <button type="button" onClick={() => { void onDeleteForMe(m.id).then(() => setOpenMessageId(null)).catch(() => setActionError("Không thể xóa tin nhắn. Vui lòng thử lại.")); }}>Xóa phía tôi</button>}
                  {canDeleteForEveryone(m) && <button type="button" disabled={deletingMessageId === m.id} onClick={() => deleteForEveryone(m)}>Xóa với mọi người</button>}
                </div>
              )}
            </div>
          </div>
        );
        })}
        {isTyping && <div className="message-bubble-row"><div className="message-bubble"><p>Đang nhập...</p></div></div>}
        <div ref={bottomSentinelRef} style={{ height: 1 }} />
      </div>

      {hasNewMessageBelow && (
        <button
          type="button"
          className="chat-window__new-message-badge"
          onClick={() => {
            scrollToBottom("smooth");
            clearNewMessageBelow(conversation.id);
          }}
        >
          ↓ Có tin nhắn mới
        </button>
      )}

      {isBlocked ? (
        <div className="chat-window__blocked-message">{conversation.type === "Group" ? "Bạn không còn quyền gửi tin trong nhóm này." : "Bạn không thể nhắn tin với người này."}</div>
      ) : (
        <form className="chat-window__composer" onSubmit={handleSubmit}>
          <button type="button" className="icon-btn" title="Đính kèm (sắp có)" disabled>
            <PaperclipIcon />
          </button>
          <input maxLength={4000} placeholder={`Nhắn tin tới ${conversation.name}`} value={draft} onChange={(e) => handleDraftChange(e.target.value)} />
          <button type="button" className="icon-btn" title="Emoji (sắp có)" disabled>
            <SmileIcon />
          </button>
          <button type="submit" className="icon-btn icon-btn--send" disabled={!draft.trim()}>
            <SendIcon />
          </button>
        </form>
      )}
    </section>
  );
}
