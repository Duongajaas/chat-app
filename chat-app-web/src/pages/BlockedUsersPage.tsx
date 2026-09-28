import { ArrowLeftIcon } from "../components/icons";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Avatar } from "../components/Avatar";
import { blocksApi } from "../api/blocks";
import { extractErrorMessage } from "../api/auth";
import { getSessionVersion } from "../api/client";
import { refreshConversation } from "../realtime/connection";
import { useChatStore } from "../store/chatStore";
import type { BlockedUser } from "../types";

function formatBlockedAt(iso: string): string {
  return new Date(iso).toLocaleDateString("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

export default function BlockedUsersPage() {
  const [blockedUsers, setBlockedUsers] = useState<BlockedUser[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState("");
  const [confirmTarget, setConfirmTarget] = useState<BlockedUser | null>(null);
  const [success, setSuccess] = useState("");
  const busy = useRef(false);
  const [unblockingId, setUnblockingId] = useState<string | null>(null);

  useEffect(() => {
    let current = true; const session = getSessionVersion();
    blocksApi.list()
      .then(rows => { if (current && session === getSessionVersion()) setBlockedUsers(rows); })
      .catch(err => { if (current && session === getSessionVersion()) setError(extractErrorMessage(err, "Không tải được danh sách đã chặn.")); })
      .finally(() => { if (current && session === getSessionVersion()) setIsLoading(false); });
    return () => { current = false; };
  }, []);

  async function handleUnblock(userId: string) {
    if (busy.current) return;
    busy.current = true;
    const session = getSessionVersion();
    setError(""); setSuccess(""); setUnblockingId(userId);
    try {
      await blocksApi.unblock(userId);
      if (session !== getSessionVersion()) return;
      setConfirmTarget(null);
      setBlockedUsers(prev => prev.filter(user => user.id !== userId));
      setSuccess("Đã bỏ chặn. Bạn có thể gửi lại lời mời kết bạn. Nếu người đó vẫn chặn bạn, hai bạn chưa thể nhắn tin.");
      useChatStore.getState().conversations.filter(c => c.peerUserId === userId)
        .forEach(c => refreshConversation(c.id));
    } catch (err) {
      if (session === getSessionVersion()) setError(extractErrorMessage(err, "Không thể bỏ chặn người dùng này."));
    } finally {
      busy.current = false;
      if (session === getSessionVersion()) setUnblockingId(null);
    }
  }

  return (
    <div className="profile-page">
      {confirmTarget && <ConfirmDialog title={`Bỏ chặn ${confirmTarget.fullName}?`}
        description="Bỏ chặn không tự khôi phục kết bạn. Bạn phải chờ 1 giờ mới có thể chặn lại người này."
        confirmLabel="Xác nhận bỏ chặn" busy={unblockingId !== null} error={error}
        onCancel={() => { setConfirmTarget(null); setError(""); }} onConfirm={() => void handleUnblock(confirmTarget.id)} />}
      <div className="profile-card">
        <Link to="/profile" className="profile-back-link"><ArrowLeftIcon /><span>Quay lại trang cá nhân</span></Link>

        <h2 style={{ fontFamily: "var(--font-display)", fontSize: 20, margin: "0 0 4px" }}>Đã chặn</h2>
        <p style={{ color: "var(--color-text-muted)", fontSize: 13.5, margin: "0 0 20px" }}>
          Bỏ chặn để cho phép liên lạc trở lại. Quan hệ bạn bè không tự khôi phục; bạn cần gửi lại lời mời kết bạn.
        </p>

        {success && <p className="alert alert-success" role="status">{success}</p>}
        {error && <div role="alert" className="alert alert-danger">{error}</div>}

        {isLoading ? (
          <p style={{ color: "var(--color-text-muted)", fontSize: 13.5 }}>Đang tải...</p>
        ) : blockedUsers.length === 0 ? (
          <p style={{ color: "var(--color-text-muted)", fontSize: 13.5 }}>Bạn chưa chặn ai.</p>
        ) : (
          <div className="device-list">
            {blockedUsers.map((blockedUser) => (
              <div key={blockedUser.id} className="device-item">
                <Avatar name={blockedUser.fullName} size={40} />
                <div className="device-item__body">
                  <div className="device-item__name">{blockedUser.fullName}</div>
                  <div className="device-item__meta">@{blockedUser.username} · Đã chặn {formatBlockedAt(blockedUser.blockedAt)}</div>
                </div>
                <button className="device-item__revoke" onClick={() => { setError(""); setConfirmTarget(blockedUser); }} disabled={unblockingId !== null}>
                  {unblockingId === blockedUser.id ? "Đang xử lý..." : "Bỏ chặn"}
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

