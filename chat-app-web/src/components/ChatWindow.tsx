import { useEffect, useRef, useState, type FormEvent } from "react";
import { Avatar } from "./Avatar";
import { PhoneIcon, VideoIcon, SendIcon, PaperclipIcon, SmileIcon, MoreIcon } from "./icons";
import type { Conversation, ChatMessage } from "../types";
import { sendTyping } from "../realtime/connection";
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
  onDeleteForMe: (messageId: string) => void;
  onBlockUser: (userId: string) => Promise<void>;
  onBack?: () => void;
}

export function ChatWindow({ conversation, messages, onSendMessage, onDeleteMessage, onDeleteForMe, onBlockUser, onBack }: ChatWindowProps) {
  const [draft, setDraft] = useState("");
  const [openMessageId, setOpenMessageId] = useState<string | null>(null);
  const [isActionsMenuOpen, setIsActionsMenuOpen] = useState(false);
  const [isBlocking, setIsBlocking] = useState(false);
  const [deletingMessageId, setDeletingMessageId] = useState<string | null>(null);
  const onlineUserIds = usePresenceStore((state) => state.onlineUserIds);
  const isTyping = usePresenceStore((state) => conversation ? state.typingByConversation[conversation.id] : false);
  const hasRead = usePresenceStore((state) => conversation ? state.readByConversation[conversation.id] : false);
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
  }, [conversation?.id]);

  useEffect(() => {
    const sentinel = bottomSentinelRef.current;
    const container = scrollContainerRef.current;
    if (!sentinel || !container) return;

    const observer = new IntersectionObserver(
      ([entry]) => {
        const nearBottom = entry.isIntersecting;
        isNearBottomRef.current = nearBottom;
        if (nearBottom && conversation) clearNewMessageBelow(conversation.id);
      },
      { root: container, threshold: 0.1 }
    );

    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [conversation?.id, clearNewMessageBelow, scrollContainerRef]);

  if (!conversation) {
    return (
      <section className="chat-window chat-window--empty">
        <p>Chọn một cuộc trò chuyện để bắt đầu nhắn tin</p>
      </section>
    );
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!draft.trim()) return;
    onSendMessage(draft.trim());
    setDraft("");
  }

  const conversationId = conversation.id;
  const isBlocked = conversation.isBlocked === true;
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
    if (message.status === "Deleted") return false;
    const messageIsMine = message.senderId === user?.id;
    const withinWindow = Date.now() - new Date(message.createdAt).getTime() < 15 * 60 * 1000;
    return (messageIsMine && withinWindow) || (!messageIsMine && isAdmin);
  }

  async function deleteForEveryone(message: ChatMessage) {
    setDeletingMessageId(message.id);
    try {
      await onDeleteMessage(message.id);
      setOpenMessageId(null);
    } catch (error) {
      console.error("Không thể xóa tin nhắn:", error);
    } finally {
      setDeletingMessageId(null);
    }
  }

  async function handleBlockUser() {
    if (!conversation.peerUserId || isBlocking) return;

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
                {m.createdAt}
                {m.status === "Failed" && " • Gửi thất bại"}
              </span>
              {hasRead && isMine && index === messages.length - 1 && <span className="message-bubble__time">Đã xem</span>}
              {openMessageId === m.id && (
                <div className="message-menu" role="menu">
                  {isMine && <button type="button" onClick={() => { onDeleteForMe(m.id); setOpenMessageId(null); }}>Xóa phía tôi</button>}
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
        <div className="chat-window__blocked-message">Bạn không thể nhắn tin với người này.</div>
      ) : (
        <form className="chat-window__composer" onSubmit={handleSubmit}>
          <button type="button" className="icon-btn" title="Đính kèm (sắp có)" disabled>
            <PaperclipIcon />
          </button>
          <input placeholder={`Nhắn tin tới ${conversation.name}`} value={draft} onChange={(e) => handleDraftChange(e.target.value)} />
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
