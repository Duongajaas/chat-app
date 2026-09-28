import { CreateGroup } from "./CreateGroup";
import { useEffect, useRef, useState } from "react";
import { Avatar } from "./Avatar";
import { SearchIcon, MoreIcon } from "./icons";
import type { Conversation } from "../types";
import { usePresenceStore } from "../store/presenceStore";
import { friendRequestsApi, type PendingFriendRequest } from "../api/friendRequests";

interface ConversationListProps {
  conversations: Conversation[];
  hasMore?: boolean;
  loadingMore?: boolean;
  onLoadMore?: () => void;
  activeId: string | null;
  onSelect: (id: string) => void;
  onAcceptRequest: (id: string) => Promise<void>;
  onDeleteRequest: (id: string) => void;
}

function getAvatarColor(id: string): string {
  const palette = ["#33d6a6", "#7fa8ff", "#ff9f6b", "#c792ea", "#f4c95d", "#ff7aa2"];
  const index = Array.from(id).reduce((sum, ch) => sum + ch.charCodeAt(0), 0) % palette.length;
  return palette[index];
}

export function ConversationList({ conversations, activeId, onSelect, onAcceptRequest, onDeleteRequest, hasMore, loadingMore, onLoadMore }: ConversationListProps) {
  const more = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    const dismiss = (event: Event) => {
      if (event instanceof KeyboardEvent && event.key !== "Escape") return;
      if (event.type === "pointerdown" && more.current?.contains(event.target as Node)) return;
      if (more.current) more.current.open = false;
    };
    document.addEventListener("pointerdown", dismiss); document.addEventListener("keydown", dismiss);
    return () => { document.removeEventListener("pointerdown", dismiss); document.removeEventListener("keydown", dismiss); };
  }, []);
  const [creating, setCreating] = useState(false);
  const [query, setQuery] = useState("");
  const [activeTab, setActiveTab] = useState<"accepted" | "unread" | "pending" | "left">("accepted");
  const [pendingRequests, setPendingRequests] = useState<PendingFriendRequest[]>([]);
  const [isLoadingPending, setIsLoadingPending] = useState(false);
  const [pendingError, setPendingError] = useState("");
  const [processingRequestId, setProcessingRequestId] = useState<string | null>(null);
  const onlineUserIds = usePresenceStore((state) => state.onlineUserIds);
  const pendingCount = pendingRequests.length;
  const unreadCount = conversations.filter(c => c.unreadCount > 0 && !c.hasLeft && !c.closedAt && c.requestStatus !== "Pending").length;

  function displayTime(value: string) {
    if (!/^\d{4}-\d{2}-\d{2}/.test(value)) return value || "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    return date.toDateString() === new Date().toDateString()
      ? date.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })
      : date.toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit" });
  }

  useEffect(() => {
    if (activeTab !== "pending") return;

    let isMounted = true;
    setIsLoadingPending(true);
    setPendingError("");
    friendRequestsApi.getPending()
      .then((requests) => { if (isMounted) setPendingRequests(requests); })
      .catch(() => { if (isMounted) setPendingError("Không thể tải lời mời kết bạn."); })
      .finally(() => { if (isMounted) setIsLoadingPending(false); });

    return () => { isMounted = false; };
  }, [activeTab]);

  const pendingConversations: Conversation[] = pendingRequests.map((request) => ({
    id: request.id,
    name: request.fullName || request.username,
    type: "Direct",
    peerUserId: request.senderId,
    avatarColor: getAvatarColor(request.senderId),
    lastMessage: `@${request.username} muốn kết bạn với bạn`,
    lastMessageAt: new Date(request.createdAt).toLocaleDateString("vi-VN"),
    unreadCount: 0,
    isOnline: false,
    requestStatus: "Pending",
  }));

  const visibleConversations = activeTab === "pending" ? pendingConversations : conversations;
  const filtered = visibleConversations
    .filter(c => activeTab !== "unread" || c.unreadCount > 0)
    .filter((conversation) => activeTab === "pending" ? conversation.requestStatus === "Pending" : conversation.requestStatus !== "Pending")
    .filter(c => activeTab === "pending" || (activeTab === "left" ? c.hasLeft || !!c.closedAt : !c.hasLeft && !c.closedAt))
    .filter((c) => c.name.toLowerCase().includes(query.toLowerCase()));

  async function acceptRequest(id: string) {
    setProcessingRequestId(id);
    try {
      await onAcceptRequest(id);
      setPendingRequests((requests) => requests.filter((request) => request.id !== id));
    } catch {
      setPendingError("Không thể chấp nhận lời mời.");
    } finally {
      setProcessingRequestId(null);
    }
  }

  return (
    <section className="conversation-list">
      <div className="conversation-list__header">
        <div className="conversation-list__heading"><h2>Đoạn chat</h2>
        <button className="conversation-create" aria-label="Tạo nhóm" title="Tạo nhóm" aria-expanded={creating} onClick={() => setCreating(v => !v)}><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 8v8M8 12h8" /></svg></button></div>
        <div className="conversation-search">
          <SearchIcon />
          <input aria-label="Tìm cuộc trò chuyện" placeholder="Tìm kiếm cuộc trò chuyện" value={query} onChange={(e) => setQuery(e.target.value)} />
        </div>
        <div className="conversation-tabs" role="group" aria-label="Loại cuộc trò chuyện">
          <button type="button" aria-pressed={activeTab === "accepted"} className={activeTab === "accepted" ? "is-active" : ""} onClick={() => setActiveTab("accepted")}>Tất cả</button>
          <button type="button" aria-pressed={activeTab === "unread"} className={activeTab === "unread" ? "is-active" : ""} onClick={() => setActiveTab("unread")}>Chưa đọc {unreadCount > 0 && <span className="unread-badge">{unreadCount}</span>}</button>
          <details ref={more} className="conversation-filters">
            <summary aria-label="Bộ lọc khác" title="Bộ lọc khác" className={activeTab === "left" || activeTab === "pending" ? "is-active" : ""}><MoreIcon /></summary>
            <div className="conversation-filters__menu">
              <button type="button" onClick={() => { setActiveTab("left"); if (more.current) more.current.open = false; }}>Nhóm đã rời</button>
              <button type="button" onClick={() => { setActiveTab("pending"); if (more.current) more.current.open = false; }}>Lời mời {pendingCount > 0 && <span className="unread-badge">{pendingCount}</span>}</button>
            </div>
          </details>
        </div>
        {(activeTab === "left" || activeTab === "pending") && <p className="conversation-list__filter-label">{activeTab === "left" ? "Nhóm đã rời" : "Lời mời kết bạn"}</p>}
      </div>

      {creating && <CreateGroup onCreated={onSelect} onClose={() => setCreating(false)} />}

      <div className="conversation-list__items">
        {activeTab === "pending" && isLoadingPending && <p className="conversation-list__empty">Đang tải lời mời...</p>}
        {activeTab === "pending" && pendingError && <p className="conversation-list__empty">{pendingError}</p>}
        {filtered.map((c) => (
          <div
            key={c.id}
            className={`conversation-item ${activeId === c.id ? "is-active" : ""}`}
            role="button"
            tabIndex={0}
            onClick={() => onSelect(c.id)}
            onKeyDown={(event) => { if (event.target === event.currentTarget && (event.key === "Enter" || event.key === " ")) { event.preventDefault(); onSelect(c.id); } }}
          >
            <Avatar src={c.avatarUrl} name={c.name} color={c.avatarColor || getAvatarColor(c.id)} isOnline={c.type === "Direct" && (c.peerUserId ? onlineUserIds.has(c.peerUserId) : c.isOnline)} size={44} />
            <div className="conversation-item__body">
              <div className="conversation-item__top">
                <span className="conversation-item__name">{c.name}</span>
                <time className="conversation-item__time" title={c.lastMessageAt}>{displayTime(c.lastMessageAt)}</time>
              </div>
              <div className="conversation-item__bottom">
                <span className="conversation-item__preview">{c.lastMessage || "Bắt đầu cuộc trò chuyện"}</span>
                {c.unreadCount > 0 && <span className="unread-badge">{c.unreadCount}</span>}
              </div>
              {activeTab === "pending" && (
                <div className="conversation-item__request-actions">
                  <button type="button" disabled={processingRequestId === c.id} onClick={(event) => { event.stopPropagation(); acceptRequest(c.id); }}>Chấp nhận</button>
                  <button type="button" onClick={(event) => { event.stopPropagation(); onDeleteRequest(c.id); }}>Xóa</button>
                </div>
              )}
            </div>
          </div>
        ))}
        {filtered.length === 0 && !(activeTab === "pending" && (isLoadingPending || pendingError)) && <p className="conversation-list__empty">{query ? "Không tìm thấy cuộc trò chuyện phù hợp." : activeTab === "pending" ? "Không có lời mời nào đang chờ." : activeTab === "left" ? "Chưa có nhóm đã rời." : "Chưa có cuộc trò chuyện nào. Chọn bạn trong Danh bạ để bắt đầu."}</p>}
      </div>
      {activeTab === "accepted" && hasMore && <button type="button" disabled={loadingMore} onClick={onLoadMore}>{loadingMore ? "Đang tải…" : "Tải thêm hội thoại"}</button>}
    </section>
  );
}

