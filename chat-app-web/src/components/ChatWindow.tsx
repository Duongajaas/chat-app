import { PrivateAttachment } from "./PrivateAttachment";
import { CallLogBubble } from "./CallOverlay";
import { startCall } from "../realtime/callManager";
import { useCallStore } from "../store/callStore";
import { messagesApi } from "../api/messages";
import { uploadMedia } from "../api/files";
import { DOCUMENT_EXTENSIONS, IMAGE_TYPES, describeBatchFailures, planAttachmentBatch, type RejectedFile } from "../utils/attachments";
import axios from "axios";
import { VoiceError, VoiceRecorder, canRecordVoice, describeVoiceError } from "../realtime/voiceRecorder";
import { MAX_VOICE_SECONDS, formatDuration } from "../utils/voice";
import { groupsApi, type GroupMember } from "../api/groups";
import { collectMentions, prependReplyMention, type MentionInput } from "../utils/mentions";
import { MessageText } from "./MessageText";
import { ReplyingToBar } from "./ReplyingToBar";
import { PinnedMessages, ForwardMessage } from "./MessageTools";
import { getSessionVersion } from "../api/client";
import { extractErrorMessage } from "../api/auth";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { GroupSettings } from "./GroupSettings";
import { Avatar } from "./Avatar";
import { MicIcon, TrashIcon, PhoneIcon, VideoIcon, SendIcon, PaperclipIcon, SmileIcon, MoreIcon, ReplyIcon, ForwardIcon, PinIcon, ArrowLeftIcon, CloseIcon, PlusCircleIcon } from "./icons";
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
  onSendMessage: (content: string, features?: { replyToMessageId?: string; mentions?: MentionInput[];
    assetId?: string | null; mediaUrl?: string | null; voiceDuration?: number | null; waveformPoints?: string | null;
    attachmentName?: string }) => void;
  onDeleteMessage: (messageId: string) => Promise<void>;
  onDeleteForMe: (messageId: string) => Promise<void>;
  onRetryMessage: (message: ChatMessage) => void;
  onBlockUser: (userId: string) => Promise<void>;
  onBack?: () => void;
}

export function ChatWindow({ conversation, messages, onSendMessage, onDeleteMessage, onDeleteForMe, onBlockUser, onBack, onRetryMessage }: ChatWindowProps) {
  const connectionStatus = useChatStore(state => state.connectionStatus);
  const [reply, setReply] = useState<ChatMessage | null>(null);
  const [forward, setForward] = useState<ChatMessage | null>(null);
  const [contextMessage, setContextMessage] = useState<ChatMessage | null>(null);
  const [members, setMembers] = useState<GroupMember[]>([]);
  const [mentioned, setMentioned] = useState<ChatMessage[] | null>(null);
  const conversations = useChatStore(state => state.conversations);
  const deletedIds = useChatStore(state => state.deletedMessageIds);
  const hiddenIds = useChatStore(state => state.hiddenMessageIds);
  useEffect(() => {
    const unavailable = (m: ChatMessage | null) => m != null && (deletedIds[m.id] || hiddenIds[m.id]);
    if (unavailable(reply)) setReply(null);
    if (unavailable(forward)) setForward(null);
    if (unavailable(contextMessage)) setContextMessage(null);
    setMentioned(rows => rows?.filter(m => !unavailable(m)) ?? null);
  }, [deletedIds, hiddenIds, reply, forward, contextMessage]);
  const [showGroup, setShowGroup] = useState(false);
  const [infoOpen, setInfoOpen] = useState<Record<string, boolean>>({});
  useEffect(() => {
    const id = conversation?.id;
    if (!id || !infoOpen[id]) return;
    const close = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !document.querySelector("dialog[open]")) {
        setInfoOpen(prev => ({ ...prev, [id]: false }));
        document.querySelector<HTMLButtonElement>('.chat-window__actions [aria-label="Thông tin hội thoại"]')?.focus();
      }
    };
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [conversation?.id, infoOpen]);
  const [actionError, setActionError] = useState("");
  const [nearBottom, setNearBottom] = useState(true);
  const [visible, setVisible] = useState(document.visibilityState === "visible");
  const [draft, setDraft] = useState("");
  const composerInput = useRef<HTMLInputElement>(null);
  const imageInput = useRef<HTMLInputElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState<"image" | "file" | "voice" | null>(null);
  const [batch, setBatch] = useState<{ done: number; total: number } | null>(null);
  const [recording, setRecording] = useState<{ seconds: number; ready: boolean } | null>(null);
  const recorderRef = useRef<VoiceRecorder | null>(null);
  const finishingVoice = useRef(false);
  // Leaving the chat (or the page) discards an unfinished recording and releases the microphone.
  useEffect(() => () => {
    if (recorderRef.current) { recorderRef.current.cancel(); recorderRef.current = null; setRecording(null); }
  }, [conversation?.id]);
  useEffect(() => {
    if (!recording) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") { recorderRef.current?.cancel(); recorderRef.current = null; setRecording(null); }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [recording !== null]);
  const activeConversation = useRef(conversation?.id);
  activeConversation.current = conversation?.id;
  const referenceRequest = useRef(0);
  const [highlightedMessage, setHighlightedMessage] = useState<string | null>(null);
  useEffect(() => {
    if (!highlightedMessage) return;
    const timer = setTimeout(() => setHighlightedMessage(null), 1500);
    return () => clearTimeout(timer);
  }, [highlightedMessage]);
  const [visibleHistory, setVisibleHistory] = useState<{ conversationId: string; count: number } | null>(null);
  const [openMessageId, setOpenMessageId] = useState<string | null>(null);
  const [activeActionId, setActiveActionId] = useState<string | null>(null);
  const [pinnedIds, setPinnedIds] = useState<string[]>([]);
  const [pinBusy, setPinBusy] = useState<string | null>(null);
  const pinRequest = useRef(false);
  const longPress = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pressed = useRef(false);
  function cancelLongPress() { if (longPress.current) clearTimeout(longPress.current); longPress.current = null; }
  useEffect(() => {
    setActiveActionId(null); setOpenMessageId(null); setPinnedIds([]);
    const dismiss = (event: Event) => {
      if (event.type === "keydown" && (event as KeyboardEvent).key !== "Escape") return;
      if (event.type === "pointerdown" && (event.target as Element).closest(".message-bubble")) return;
      cancelLongPress(); setActiveActionId(null); setOpenMessageId(null);
    };
    document.addEventListener("pointerdown", dismiss);
    document.addEventListener("keydown", dismiss);
    window.addEventListener("blur", dismiss);
    return () => { cancelLongPress(); document.removeEventListener("pointerdown", dismiss); document.removeEventListener("keydown", dismiss); window.removeEventListener("blur", dismiss); };
  }, [conversation?.id]);
  const [isActionsMenuOpen, setIsActionsMenuOpen] = useState(false);
  const [isBlocking, setIsBlocking] = useState(false);
  const [deletingMessageId, setDeletingMessageId] = useState<string | null>(null);
  const onlineUserIds = usePresenceStore((state) => state.onlineUserIds);
  const isTyping = usePresenceStore((state) => conversation ? state.typingByConversation[conversation.id] : false);
  const hasRead = usePresenceStore((state) => conversation ? state.readByConversation[conversation.id] : 0);
  const { user } = useAuth();
  const callBusy = useCallStore(s => s.preparing || s.call?.status === "Ringing" || s.call?.status === "Accepted");
  const typingStartTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const typingStopTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const bottomSentinelRef = useRef<HTMLDivElement>(null);
  const isNearBottomRef = useRef(true);
  const { loadOlder, isLoading: isLoadingOlder, hasMore, scrollContainerRef } = useLoadOlderMessages(conversation?.id ?? null);
  const hasNewMessageBelow = useChatStore((state) => conversation ? state.hasNewMessageBelowByConversation[conversation.id] ?? false : false);
  const visibleCount = visibleHistory && visibleHistory.conversationId === conversation?.id ? visibleHistory.count : 120;
  const visibleMessages = messages.slice(-visibleCount);
  const hasCachedOlder = messages.length > visibleCount;
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
    setHighlightedMessage(null); setShowGroup(false); setReply(null); setForward(null); setMentioned(null); setContextMessage(null);
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

  useEffect(() => {
    let current = true; setMembers([]);
    if (conversation?.type === "Group" && !conversation.hasLeft && !conversation.closedAt)
      void groupsApi.members(conversation.id).then(rows => { if (current) setMembers(rows); }).catch(() => undefined);
    return () => { current = false; };
  }, [conversation?.id, conversation?.version, conversation?.hasLeft]);

  function senderName(message: ChatMessage) {
    if (message.senderId === user?.id) return "Bạn";
    return members.find(m => m.userId === message.senderId)?.fullName || message.senderName ||
      (conversation?.type === "Direct" ? conversation.name : message.senderUsername || "Thành viên");
  }

  function startReply(message: ChatMessage) {
    setReply(message); setOpenMessageId(null); setActiveActionId(null);
    if (conversation?.type === "Group") {
      const member = members.find(m => m.userId === message.senderId);
      setDraft(current => prependReplyMention(current, member, user?.id));
    }
    composerInput.current?.focus();
  }

  async function openReference(message: ChatMessage) {
    if (!conversation) return;
    const id = conversation.id;
    const session = getSessionVersion();
    const request = ++referenceRequest.current;
    const valid = () => session === getSessionVersion() && id === activeConversation.current && request === referenceRequest.current;
    try {
      const [fresh] = await messagesApi.batch(id, [message.id]);
      if (!valid()) return;
      if (!fresh || ["Deleted", "Hidden", "Unavailable"].includes(fresh.status ?? "")) { setActionError("Tin nhắn gốc không khả dụng."); return; }
      setActionError("");
      const index = messages.findIndex(m => m.id === fresh.id);
      if (index >= 0) {
        setVisibleHistory({ conversationId: id, count: Math.max(120, messages.length - index) });
        requestAnimationFrame(() => requestAnimationFrame(() => {
          if (!valid()) return;
          document.getElementById("message-" + fresh.id)?.scrollIntoView({ behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth", block: "center" });
          setHighlightedMessage(fresh.id);
        }));
      } else setContextMessage(fresh); // Readable context from API for messages outside cached history.
    } catch { if (valid()) setActionError("Không thể mở tin nhắn."); }
  }
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

  async function handleAttachmentsPicked(picked: File[], kind: "image" | "file") {
    if (imageInput.current) imageInput.current.value = "";
    if (fileInput.current) fileInput.current.value = "";
    if (picked.length === 0 || uploading || isBlocked) return;
    const { accepted, rejected } = planAttachmentBatch(picked, kind);
    if (accepted.length === 0) {
      // Nothing to send: remind the user which formats work.
      const wrongFormat = rejected.some(r => r.reason.startsWith("định dạng") || r.reason.startsWith("không phải ảnh"));
      const hint = !wrongFormat ? "" : kind === "image" ? " Chỉ hỗ trợ ảnh JPEG, PNG hoặc WebP."
        : " Chỉ hỗ trợ PDF, Word, Excel, PowerPoint, ZIP và văn bản (TXT, CSV, MD).";
      setActionError(describeBatchFailures(0, picked.length, rejected) + hint); return;
    }
    const targetConversationId = conversationId;
    const session = getSessionVersion();
    const failures: RejectedFile[] = [...rejected];
    let sent = 0; let stopped = "";
    setActionError(""); setUploading(kind); setBatch({ done: 0, total: accepted.length });
    try {
      // One message per file, uploaded in order so they arrive in the order they were chosen.
      for (const [index, file] of accepted.entries()) {
        // Never post into a different chat or account if the user switched while uploading.
        if (session !== getSessionVersion() || useChatStore.getState().activeConversationId !== targetConversationId) return;
        setBatch({ done: index, total: accepted.length });
        try {
          const uploaded = await uploadMedia(file);
          if (session !== getSessionVersion() || useChatStore.getState().activeConversationId !== targetConversationId) return;
          // A quoted reply belongs to the first message only.
          onSendMessage("", { assetId: uploaded.assetId, replyToMessageId: sent === 0 ? reply?.id : undefined, attachmentName: uploaded.fileName || file.name });
          if (sent === 0) setReply(null);
          sent++;
        } catch (error) {
          if (session !== getSessionVersion()) return;
          if (axios.isAxiosError(error) && error.response?.status === 429) {
            const left = accepted.length - index;
            stopped = `Bạn thao tác quá nhanh nên ${left} mục còn lại chưa được gửi. Hãy đợi một chút rồi gửi lại.`;
            break;
          }
          const fallback = kind === "image" ? "không tải được ảnh" : "không tải được file";
          failures.push({ name: file.name, reason: extractErrorMessage(error, error instanceof Error && !("isAxiosError" in error) ? error.message : fallback) });
        }
      }
    } finally {
      setUploading(null); setBatch(null);
    }
    if (session === getSessionVersion()) setActionError(describeBatchFailures(sent, picked.length, failures, stopped));
  }

  async function startRecording() {
    if (uploading || recording || isBlocked) return;
    if (!canRecordVoice()) { setActionError("Trình duyệt này chưa hỗ trợ ghi âm."); return; }
    setActionError("");
    const recorder = new VoiceRecorder();
    recorderRef.current = recorder;
    setRecording({ seconds: 0, ready: false }); // not ready until the browser grants the microphone
    try {
      await recorder.start(seconds => {
        setRecording({ seconds, ready: true });
        if (seconds >= MAX_VOICE_SECONDS) void finishRecording();
      });
      setRecording(current => (current ? { ...current, ready: true } : current));
      // Cancelled (or the chat changed) while the browser was asking for microphone permission.
      if (recorderRef.current !== recorder) recorder.cancel();
    } catch (error) {
      if (recorderRef.current === recorder) { recorderRef.current = null; setRecording(null); }
      setActionError(describeVoiceError(error));
    }
  }

  function cancelRecording() {
    recorderRef.current?.cancel();
    recorderRef.current = null;
    setRecording(null);
  }

  async function finishRecording() {
    const recorder = recorderRef.current;
    if (!recorder || finishingVoice.current) return;
    finishingVoice.current = true;
    const targetConversationId = conversationId;
    const session = getSessionVersion();
    try {
      const result = await recorder.stop();
      recorderRef.current = null; setRecording(null); setActionError(""); setUploading("voice");
      const uploaded = await uploadMedia(result.file);
      // Do not post into a different chat or account if the user switched while uploading.
      if (session !== getSessionVersion() || useChatStore.getState().activeConversationId !== targetConversationId) return;
      onSendMessage("", { assetId: uploaded.assetId, voiceDuration: result.durationSeconds,
        waveformPoints: JSON.stringify(result.waveform), replyToMessageId: reply?.id, attachmentName: "Tin nhắn thoại" });
      setReply(null);
    } catch (error) {
      recorderRef.current = null; setRecording(null);
      if (session === getSessionVersion()) setActionError(error instanceof VoiceError ? error.message
        : extractErrorMessage(error, error instanceof Error && !("isAxiosError" in error) ? error.message : "Không gửi được tin nhắn thoại. Vui lòng thử lại."));
    } finally {
      finishingVoice.current = false; setUploading(null);
    }
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!draft.trim() || conversation?.hasLeft || conversation?.closedAt || conversation?.isBlocked) return;
    onSendMessage(draft, { replyToMessageId: reply?.id, mentions: conversation?.type === "Group" ? collectMentions(draft, members.filter(m => m.userId !== user?.id)) : [] });
    setReply(null);
    setDraft("");
  }

  const conversationId = conversation.id;
  const batchProgress = batch && batch.total > 1 ? ` ${Math.min(batch.done + 1, batch.total)}/${batch.total}` : "";
  const isBlocked = conversation.isBlocked === true || conversation.hasLeft === true || !!conversation.closedAt;
  const isDirectOnline = !isBlocked && conversation.type === "Direct" && (conversation.peerUserId ? onlineUserIds.has(conversation.peerUserId) : conversation.isOnline);
  const isAdmin = conversation.isAdmin === true;
  async function togglePin(message: ChatMessage) {
    if (pinRequest.current) return;
    pinRequest.current = true; setPinBusy(message.id); setActionError("");
    const session = getSessionVersion();
    try {
      if (pinnedIds.includes(message.id)) await messagesApi.unpin(conversationId, message.id);
      else await messagesApi.pin(conversationId, message.id);
      if (session === getSessionVersion()) window.dispatchEvent(new Event("chat:pins-changed"));
    } catch (e) { if (session === getSessionVersion()) setActionError(extractErrorMessage(e)); }
    finally { pinRequest.current = false; if (session === getSessionVersion()) setPinBusy(null); }
  }

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
    } catch (error) {
      setActionError(extractErrorMessage(error, "Không thể chặn người dùng này."));
    } finally {
      setIsBlocking(false);
    }
  }

  return (<>
    <section className="chat-window">
      <header className="chat-window__header">
        {onBack && <button className="chat-window__back-btn icon-btn" onClick={onBack} title="Quay lại" aria-label="Quay lại"><ArrowLeftIcon /></button>}
        <div className="chat-window__peer">
          <Avatar src={conversation.avatarUrl} name={conversation.name} color={conversation.avatarColor} isOnline={isDirectOnline} size={40} />
          <div>
            <div className="chat-window__name">{conversation.name}</div>
            <div className="chat-window__status">
              {conversation.type === "Direct" ? (isDirectOnline ? "Đang hoạt động" : "Không hoạt động") : "Nhóm chat"}
            </div>
          </div>
        </div>
        <div className="chat-window__actions">
          <button className={`icon-btn ${infoOpen[conversation.id] ? "is-active" : ""}`} aria-label="Thông tin hội thoại" aria-expanded={!!infoOpen[conversation.id]} onClick={() => setInfoOpen(prev => ({ ...prev, [conversation.id]: !prev[conversation.id] }))}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M15 4v16" /></svg>
          </button>
          <button className="icon-btn" title="Gọi thoại" aria-label="Gọi thoại" disabled={conversation.type !== "Direct" || isBlocked || callBusy || connectionStatus !== "connected"} onClick={() => void startCall(conversation.id, "Audio")}>
            <PhoneIcon />
          </button>
          <button className="icon-btn" title="Gọi video" aria-label="Gọi video" disabled={conversation.type !== "Direct" || isBlocked || callBusy || connectionStatus !== "connected"} onClick={() => void startCall(conversation.id, "Video")}>
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

      {forward && <ForwardMessage message={forward} conversations={conversations} onClose={() => setForward(null)} />}
      {contextMessage && <section className="group-settings" aria-label="Tin nhắn tham chiếu">
        <p><MessageText content={contextMessage.content} mentions={contextMessage.mentions} /></p>
        <button onClick={() => setContextMessage(null)}>Đóng tin tham chiếu</button>
      </section>}
      <PinnedMessages key={`pins:${conversation.id}`} conversation={conversation} senderName={senderName} onPinsChange={setPinnedIds} onOpen={m => void openReference(m)} />
      {showGroup && conversation.type === "Group" && <GroupSettings key={`group-settings:${conversation.id}`} conversation={conversation} onClose={() => setShowGroup(false)} />}
      {(conversation.hasLeft || conversation.closedAt) && <p role="status">Chỉ đọc lịch sử trong những khoảng bạn là thành viên.</p>}
      {connectionStatus !== "connected" && <p role="status">{connectionStatus === "connecting" ? "Đang kết nối lại…" : "Mất kết nối. Ứng dụng sẽ tự thử lại."}</p>}
      {actionError && <p role="alert">{actionError}</p>}
      <div className="chat-window__messages" ref={scrollContainerRef} onScroll={() => { cancelLongPress(); setActiveActionId(null); setOpenMessageId(null); }}>
        {(hasCachedOlder || hasMore) && (
          <div className="chat-window__load-older">
            <button 
              onClick={() => {
                if (hasCachedOlder) setVisibleHistory({ conversationId: conversation.id, count: visibleCount + 120 });
                else { setVisibleHistory({ conversationId: conversation.id, count: visibleCount + 50 }); void loadOlder(); }
              }}
              disabled={isLoadingOlder}
              className="chat-window__load-older-btn"
            >
              {isLoadingOlder ? "Đang tải..." : hasCachedOlder ? "Xem thêm tin nhắn đã tải" : "Tải tin nhắn cũ"}
            </button>
          </div>
        )}
        {visibleMessages.map((m, index) => {
          const isMine = m.senderId === user?.id;
          const actionable = m.type !== "CallLog" && m.status !== "Deleted" && m.status !== "Sending" && m.status !== "Failed" && m.status !== "Hidden" && m.status !== "Unavailable";
          // console.log("Rendering message", m.id, "isMine:", isMine, "content:", m.content, "userId:", m.senderId, "currentUserId:", user?.id);
          return (
          <div id={"message-" + m.id} key={m.id} className={`message-bubble-row ${isMine ? "is-me" : ""} ${highlightedMessage === m.id ? "message-reference-highlight" : ""}`}>
            <div className={`message-bubble ${activeActionId === m.id || openMessageId === m.id ? "has-actions" : ""}`}
              tabIndex={actionable ? 0 : undefined} aria-label={actionable ? "Tin nhắn; nhấn Enter để mở thao tác" : undefined}
              onPointerDown={event => {
                cancelLongPress(); pressed.current = false;
                if (!actionable || event.pointerType === "mouse" || !event.isPrimary || (event.target as Element).closest("button, a")) return;
                longPress.current = setTimeout(() => { pressed.current = true; setActiveActionId(m.id); navigator.vibrate?.(10); }, 450);
              }}
              onPointerMove={cancelLongPress} onPointerUp={cancelLongPress} onPointerCancel={cancelLongPress} onPointerLeave={cancelLongPress}
              onClickCapture={event => { if (pressed.current) { event.preventDefault(); event.stopPropagation(); pressed.current = false; } }}
              onKeyDown={event => { if (event.target === event.currentTarget && actionable && (event.key === "Enter" || event.key === " ")) { event.preventDefault(); setActiveActionId(m.id); } }}
              onContextMenu={(event) => { event.preventDefault(); if (actionable) { setActiveActionId(m.id); if (window.matchMedia("(pointer: fine)").matches) setOpenMessageId(m.id); } }}>
              {m.isForwarded && m.status !== "Deleted" && <small className="message-bubble__forwarded-label">Đã chuyển tiếp</small>}
              {m.replyPreview && m.status !== "Deleted" && <button className="reply-preview" disabled={!m.replyPreview.isAvailable} onClick={() => {
                if (m.replyPreview?.isAvailable) void openReference({ ...m, id: m.replyPreview.id, sequence: m.replyPreview.sequence ?? 0 });
              }}>{m.replyPreview.isAvailable ? `${m.replyPreview.senderName ?? "Người dùng"}: ${m.replyPreview.contentSnippet ?? ""}` : "Tin nhắn gốc không khả dụng"}</button>}
              {m.status !== "Deleted" && m.attachments?.map(a => <PrivateAttachment key={a.id} messageId={m.id} attachment={a} voice={{ duration: m.voiceDuration, waveform: m.waveformPoints }} />)}
              {(m.status === "Sending" || m.status === "Failed") && m.assetId && !m.attachments?.length &&
                <small className="message-bubble__pending-media">{m.voiceDuration ? `🎤 Tin nhắn thoại (${formatDuration(m.voiceDuration)})` : m.pendingAttachmentName ? `📎 ${m.pendingAttachmentName}` : "Ảnh"}</small>}
              {m.status === "Deleted" ? <p className="message-bubble__deleted">Tin nhắn đã được thu hồi</p> : <>
                {m.type === "CallLog" ? <CallLogBubble content={m.content} isCaller={isMine} conversationId={conversation.id} disabled={isBlocked || callBusy || connectionStatus !== "connected"} /> : m.content && <p><MessageText content={m.content} mentions={m.mentions} /></p>}
                {(m.type === "File" || m.type === "Audio") && !m.attachments?.length && <small>Media không khả dụng.</small>}
              </>}
              <span className="message-bubble__time">
                {new Date(m.createdAt).toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })}
                {m.status === "Sending" && <small role="status">{m.deliveryState === "RetryWaiting" || m.deliveryState === "Queued"
                  ? "Chờ kết nối / chờ gửi…" : m.deliveryState === "Reconciling" ? "Đang kiểm tra tin đã gửi…" : "Đang gửi…"}</small>}
                {m.status === "Failed" && m.deliveryError && <small role="status">{m.deliveryError}</small>}
                {m.status === "Failed" && <button type="button" onClick={() => onRetryMessage(m)}>Gửi lại</button>}
              </span>
              {conversation.type === "Direct" && (hasRead ?? 0) >= m.sequence && isMine && index === visibleMessages.length - 1 && <span className="message-bubble__time">Đã xem</span>}
              {actionable && <div className={`message-action-bar ${isMine ? "message-action-bar--mine" : ""}`} role="group" aria-label="Thao tác tin nhắn">
                <button type="button" className="message-action-btn" title="Bày tỏ cảm xúc (sắp có)" aria-label="Bày tỏ cảm xúc (sắp có)" disabled><SmileIcon size={16} /></button>
                <button type="button" className="message-action-btn" aria-label="Trả lời" title="Trả lời" disabled={isBlocked} onClick={() => startReply(m)}><ReplyIcon size={16} /></button>
                <button type="button" className="message-action-btn" aria-label="Chuyển tiếp" title="Chuyển tiếp" onClick={() => { setForward(m); setOpenMessageId(null); setActiveActionId(null); }}><ForwardIcon size={16} /></button>
                <button type="button" className={`message-action-btn ${pinnedIds.includes(m.id) ? "is-active" : ""}`} aria-pressed={pinnedIds.includes(m.id)} aria-label={pinnedIds.includes(m.id) ? "Bỏ ghim tin nhắn" : "Ghim tin nhắn"} title={pinnedIds.includes(m.id) ? "Bỏ ghim" : "Ghim"}
                  disabled={pinBusy !== null || isBlocked || !(conversation.type === "Direct" || conversation.permissions?.includes("PinMessage"))} onClick={() => void togglePin(m)}><PinIcon size={16} /></button>
                <button type="button" className="message-action-btn" disabled={!isMine && !canDeleteForEveryone(m)} aria-label="Thêm thao tác" title="Thêm thao tác" aria-expanded={openMessageId === m.id} onClick={() => setOpenMessageId(openMessageId === m.id ? null : m.id)}><MoreIcon size={16} /></button>
              </div>}
              {actionable && (isMine || canDeleteForEveryone(m)) && openMessageId === m.id && (
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

      {reply && !isBlocked && <ReplyingToBar message={reply} senderName={senderName(reply)} onCancel={() => { setReply(null); composerInput.current?.focus(); }} />}
      {conversation.type === "Group" && !isBlocked && /(?:^|\s)@([\w.-]*)$/.test(draft) && <div className="mention-suggestions">
        {members.filter(m => m.userId !== user?.id && m.username?.toLowerCase().startsWith(draft.slice(draft.lastIndexOf("@") + 1).toLowerCase()))
          .slice(0, 6).map(m => <button key={m.userId} onClick={() => setDraft(draft.slice(0, draft.lastIndexOf("@")) + "@" + m.username + " ")}>@{m.username}</button>)}
      </div>}
      {isBlocked ? (
        <div className="chat-window__blocked-message">{conversation.type === "Group" ? "Bạn không còn quyền gửi tin trong nhóm này." : "Bạn không thể nhắn tin với người này."}</div>
      ) : recording ? (
        <div className="chat-window__composer voice-recorder" role="group" aria-label="Đang ghi âm tin nhắn thoại">
          <button type="button" className="icon-btn" aria-label="Hủy ghi âm" title="Hủy ghi âm (Esc)" onClick={cancelRecording}><TrashIcon /></button>
          <span className="voice-recorder__dot" aria-hidden="true" />
          <span className="voice-recorder__time" role="timer">{formatDuration(recording.seconds)} / {formatDuration(MAX_VOICE_SECONDS)}</span>
          <span className="voice-recorder__hint">{recording.ready ? "Đang ghi âm…" : "Đang chờ micro…"}</span>
          <button type="button" className="icon-btn icon-btn--send" aria-label="Gửi tin nhắn thoại" title="Gửi" disabled={!recording.ready} onClick={() => void finishRecording()}><SendIcon /></button>
        </div>
      ) : (
        <form className="chat-window__composer" onSubmit={handleSubmit}>
          <div className="chat-window__composer-tools">
          <button type="button" className="icon-btn" title="Emoji (sắp có)" aria-label="Emoji (sắp có)" disabled><SmileIcon /></button>
          <input ref={imageInput} type="file" multiple accept={IMAGE_TYPES.join(",")} hidden aria-hidden="true" tabIndex={-1}
            onChange={e => { void handleAttachmentsPicked(Array.from(e.target.files ?? []), "image"); }} />
          <input ref={fileInput} type="file" multiple accept={[...DOCUMENT_EXTENSIONS, ...IMAGE_TYPES].join(",")} hidden aria-hidden="true" tabIndex={-1}
            onChange={e => { void handleAttachmentsPicked(Array.from(e.target.files ?? []), "file"); }} />
          <button type="button" className="icon-btn" title={uploading === "image" ? `Đang tải ảnh${batchProgress}…` : "Gửi ảnh (chọn được nhiều ảnh)"} aria-label={uploading === "image" ? `Đang tải ảnh${batchProgress}` : "Gửi ảnh"}
            aria-busy={uploading === "image"} disabled={uploading !== null} onClick={() => imageInput.current?.click()}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2" /><circle cx="8" cy="8" r="2" /><path d="m3 18 6-6 4 4 3-3 5 5" /></svg>
          </button>
          <button type="button" className="icon-btn" title={uploading === "file" ? `Đang tải file${batchProgress}…` : "Gửi file (chọn được nhiều file; PDF, Word, Excel, PowerPoint, ZIP, TXT – tối đa 8 MB mỗi file)"}
            aria-label={uploading === "file" ? `Đang tải file${batchProgress}` : "Gửi file"}
            aria-busy={uploading === "file"} disabled={uploading !== null} onClick={() => fileInput.current?.click()}>
            <PaperclipIcon />
          </button>
          <button type="button" className="icon-btn" title={uploading === "voice" ? "Đang gửi tin nhắn thoại…" : "Ghi âm tin nhắn thoại"}
            aria-label={uploading === "voice" ? "Đang gửi tin nhắn thoại" : "Ghi âm tin nhắn thoại"}
            aria-busy={uploading === "voice"} disabled={uploading !== null} onClick={() => void startRecording()}>
            <MicIcon />
          </button>
          <button type="button" className="icon-btn" title="Thêm tiện ích (sắp có)" aria-label="Thêm tiện ích (sắp có)" disabled><PlusCircleIcon /></button>
          </div>
          <input ref={composerInput} maxLength={4000} placeholder={`Nhắn tin tới ${conversation.name}`} value={draft} onChange={(e) => handleDraftChange(e.target.value)} />
          <button type="submit" aria-label="Gửi tin nhắn" className="icon-btn icon-btn--send" disabled={!draft.trim()}>
            <SendIcon />
          </button>
        </form>
      )}
    </section>
    {infoOpen[conversation.id] && <aside className="conversation-info" aria-label="Thông tin hội thoại">
      <header><h2>Thông tin hội thoại</h2><button className="icon-btn" aria-label="Đóng thông tin hội thoại" onClick={() => setInfoOpen(prev => ({ ...prev, [conversation.id]: false }))}><CloseIcon /></button></header>
      <div className="conversation-info__profile"><Avatar src={conversation.avatarUrl} name={conversation.name} color={conversation.avatarColor} size={72} /><h3>{conversation.name}</h3><p>{conversation.type === "Group" ? `${conversation.memberCount ?? members.length} thành viên` : isDirectOnline ? "Đang hoạt động" : "Không hoạt động"}</p></div>
      <div className="conversation-info__quick">
        <button disabled title="Sắp có"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M5 17h14l-2-4V9a5 5 0 0 0-10 0v4ZM10 21h4" /></svg>Tắt thông báo</button>
        <button disabled title="Sắp có"><PinIcon />Ghim hội thoại</button>

      </div>
      {conversation.type === "Group" && <section className="conversation-info__group"><h3>Nhóm</h3><div className="conversation-info__group-actions">
          {conversation.type === "Group" && <button onClick={() => {
            const session = getSessionVersion();
            void messagesApi.mentions(conversation.id).then(rows => { if (session === getSessionVersion()) setMentioned(rows); })
              .catch(e => setActionError(extractErrorMessage(e)));
          }}>Nhắc đến bạn</button>}
          {conversation.type === "Group" && <button onClick={() => setShowGroup(v => !v)}>Thông tin nhóm</button>}
      </div></section>}
      {mentioned && <section className="conversation-info__mentions" aria-label="Tin nhắn nhắc đến bạn">
        <h3>Nhắc đến bạn</h3>
        {mentioned.length === 0 && <p>Chưa có tin nhắc đến bạn.</p>}
        {mentioned.map(m => <button key={m.id} onClick={() => { setInfoOpen(prev => ({ ...prev, [conversation.id]: false })); void openReference(m); }}>{m.content.slice(0, 100)}</button>)}
        <button onClick={() => setMentioned(null)}>Đóng danh sách</button>
      </section>}
      <section><h3>Tin nhắn ghim</h3><p>{pinnedIds.length ? `${pinnedIds.length} tin nhắn bạn có thể xem. Mở từ thanh ghim trong hội thoại.` : "Chưa có tin nhắn ghim khả dụng."}</p></section>
      <section><h3>Tệp đã chia sẻ</h3><p className="conversation-info__hint">Trong các tin nhắn đã tải</p>
        {messages.flatMap(m => m.status !== "Deleted" && !deletedIds[m.id] && !hiddenIds[m.id] ? (m.attachments ?? []) : []).length === 0 && <p>Chưa có tệp trong các tin nhắn đã tải.</p>}
        {messages.flatMap(m => m.status !== "Deleted" && !deletedIds[m.id] && !hiddenIds[m.id] ? (m.attachments ?? []) : []).slice(0, 6).map(a => <div className="conversation-info__file" key={a.id}><PaperclipIcon /><span>{a.fileName || "Tệp đính kèm"}</span></div>)}
      </section>
    </aside>}
    </>
  );
}
