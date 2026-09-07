import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Sidebar } from "../components/Sidebar";
import { Avatar } from "../components/Avatar";
import { MoreIcon, SearchIcon } from "../components/icons";
import { friendsApi, type Friend } from "../api/friends";
import { conversationsApi } from "../api/conversations";
import { blocksApi } from "../api/blocks";
import { extractErrorMessage } from "../api/auth";
import { usePresenceStore } from "../store/presenceStore";

function getAvatarColor(id: string): string {
  const palette = ["#33d6a6", "#7fa8ff", "#ff9f6b", "#c792ea", "#f4c95d", "#ff7aa2"];
  const index = Array.from(id).reduce((sum, character) => sum + character.charCodeAt(0), 0) % palette.length;
  return palette[index];
}

export default function FriendsPage() {
  const navigate = useNavigate();
  const onlineUserIds = usePresenceStore((state) => state.onlineUserIds);
  const [friends, setFriends] = useState<Friend[]>([]);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [loadingFriendId, setLoadingFriendId] = useState<string | null>(null);
  const [openMenuFriendId, setOpenMenuFriendId] = useState<string | null>(null);
  const [blockingFriendId, setBlockingFriendId] = useState<string | null>(null);

  useEffect(() => {
    friendsApi.list()
      .then(setFriends)
      .catch((reason) => setError(extractErrorMessage(reason, "Không thể tải danh sách bạn bè.")));
  }, []);

  const visibleFriends = friends
    .filter((friend) => `${friend.fullName} ${friend.username}`.toLowerCase().includes(query.toLowerCase()))
    .sort((left, right) => {
      const onlineOrder = Number(onlineUserIds.has(right.id)) - Number(onlineUserIds.has(left.id));
      return onlineOrder || left.fullName.localeCompare(right.fullName, "vi");
    });

  async function handleMessage(friendId: string) {
    setLoadingFriendId(friendId);
    setError("");
    try {
      const conversation = await conversationsApi.createDirect(friendId);
      navigate("/", { state: { activeConversationId: conversation.id } });
    } catch (reason) {
      setError(extractErrorMessage(reason, "Không thể mở cuộc trò chuyện."));
    } finally {
      setLoadingFriendId(null);
    }
  }

  async function handleBlock(friend: Friend) {
    const confirmed = window.confirm(`Chặn ${friend.fullName}? Hai bạn sẽ không thể nhắn tin riêng cho nhau.`);
    if (!confirmed) return;

    setBlockingFriendId(friend.id);
    setError("");

    try {
      await blocksApi.block(friend.id);
      setFriends((prev) => prev.filter((item) => item.id !== friend.id));
      setOpenMenuFriendId(null);
    } catch (reason) {
      setError(extractErrorMessage(reason, "Không thể chặn người dùng này."));
    } finally {
      setBlockingFriendId(null);
    }
  }

  return (
    <div className="friends-shell">
      <Sidebar />
      <main className="friends-page">
        <header className="friends-page__header">
          <div>
            <h1>Danh bạ</h1>
            <p>{friends.length} người bạn</p>
          </div>
          <div className="friends-search">
            <SearchIcon />
            <input placeholder="Tìm bạn bè" value={query} onChange={(event) => setQuery(event.target.value)} />
          </div>
        </header>

        {error && <div className="alert alert-danger">{error}</div>}
        <div className="friends-list">
          {visibleFriends.map((friend) => {
            const isOnline = onlineUserIds.has(friend.id);
            return (
              <article className="friend-item" key={friend.id}>
                <Avatar name={friend.fullName} color={getAvatarColor(friend.id)} isOnline={isOnline} size={48} />
                <div className="friend-item__body">
                  <strong>{friend.fullName}</strong>
                  <span>@{friend.username} · {isOnline ? "Đang hoạt động" : "Ngoại tuyến"}</span>
                </div>
                <button type="button" className="friend-item__message" onClick={() => handleMessage(friend.id)} disabled={loadingFriendId === friend.id}>
                  {loadingFriendId === friend.id ? "Đang mở..." : "Nhắn tin"}
                </button>
                <div className="friend-item__actions">
                  <button type="button" className="friend-item__more" onClick={() => setOpenMenuFriendId((id) => id === friend.id ? null : friend.id)} title="Tùy chọn">
                    <MoreIcon size={18} />
                  </button>
                  {openMenuFriendId === friend.id && (
                    <div className="friend-item__menu" role="menu">
                      <button type="button" onClick={() => handleBlock(friend)} disabled={blockingFriendId === friend.id}>
                        {blockingFriendId === friend.id ? "Đang chặn..." : "Chặn"}
                      </button>
                    </div>
                  )}
                </div>
              </article>
            );
          })}
          {visibleFriends.length === 0 && !error && <p className="friends-page__empty">Chưa có người bạn phù hợp.</p>}
        </div>
      </main>
    </div>
  );
}
