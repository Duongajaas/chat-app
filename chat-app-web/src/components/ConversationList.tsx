import { useEffect, useState } from "react";
import { Avatar } from "./Avatar";
import { SearchIcon } from "./icons";
import type { Conversation } from "../types";
import { usePresenceStore } from "../store/presenceStore";
import { friendRequestsApi, type PendingFriendRequest } from "../api/friendRequests";

interface ConversationListProps {
  conversations: Conversation[];
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

export function ConversationList({ conversations, activeId, onSelect, onAcceptRequest, onDeleteRequest }: ConversationListProps) {
  const [query, setQuery] = useState("");
  const [activeTab, setActiveTab] = useState<"accepted" | "pending">("accepted");
  const [pendingRequests, setPendingRequests] = useState<PendingFriendRequest[]>([]);
  const [isLoadingPending, setIsLoadingPending] = useState(false);
  const [pendingError, setPendingError] = useState("");
  const [processingRequestId, setProcessingRequestId] = useState<string | null>(null);
  const onlineUserIds = usePresenceStore((state) => state.onlineUserIds);
  const pendingCount = pendingRequests.length;

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
    .filter((conversation) => activeTab === "pending" ? conversation.requestStatus === "Pending" : conversation.requestStatus !== "Pending")
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
        <h2>Đoạn chat</h2>
        <div className="conversation-tabs" role="tablist" aria-label="Loại cuộc trò chuyện">
          <button type="button" className={activeTab === "accepted" ? "is-active" : ""} onClick={() => setActiveTab("accepted")}>Đoạn chat</button>
          <button type="button" className={activeTab === "pending" ? "is-active" : ""} onClick={() => setActiveTab("pending")}>Lời mời {pendingCount > 0 && <span className="unread-badge">{pendingCount}</span>}</button>
        </div>
      </div>

      <div className="conversation-search">
        <SearchIcon />
        <input placeholder="Tìm kiếm" value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>

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
            onKeyDown={(event) => { if (event.key === "Enter") onSelect(c.id); }}
          >
            <Avatar name={c.name} color={c.avatarColor || getAvatarColor(c.id)} isOnline={c.type === "Direct" && (c.peerUserId ? onlineUserIds.has(c.peerUserId) : c.isOnline)} size={48} />
            <div className="conversation-item__body">
              <div className="conversation-item__top">
                <span className="conversation-item__name">{c.name}</span>
                <span className="conversation-item__time">{c.lastMessageAt || "Gần đây"}</span>
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
        {filtered.length === 0 && <p className="conversation-list__empty">Không tìm thấy cuộc trò chuyện nào.</p>}
      </div>
    </section>
  );
}