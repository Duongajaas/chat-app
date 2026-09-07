import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Avatar } from "../components/Avatar";
import { blocksApi } from "../api/blocks";
import { extractErrorMessage } from "../api/auth";
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
  const [unblockingId, setUnblockingId] = useState<string | null>(null);

  useEffect(() => {
    blocksApi
      .list()
      .then(setBlockedUsers)
      .catch((err) => setError(extractErrorMessage(err, "Không tải được danh sách đã chặn.")))
      .finally(() => setIsLoading(false));
  }, []);

  async function handleUnblock(userId: string) {
    setError("");
    setUnblockingId(userId);

    try {
      await blocksApi.unblock(userId);
      setBlockedUsers((prev) => prev.filter((user) => user.id !== userId));
    } catch (err) {
      setError(extractErrorMessage(err, "Không thể bỏ chặn người dùng này."));
    } finally {
      setUnblockingId(null);
    }
  }

  return (
    <div className="profile-page">
      <div className="profile-card">
        <Link to="/profile" className="profile-back-link">← Quay lại trang cá nhân</Link>

        <h2 style={{ fontFamily: "var(--font-display)", fontSize: 20, margin: "0 0 4px" }}>Đã chặn</h2>
        <p style={{ color: "var(--color-text-muted)", fontSize: 13.5, margin: "0 0 20px" }}>
          Quản lý những người không thể nhắn tin riêng hoặc gọi cho bạn.
        </p>

        {error && <div className="alert alert-danger">{error}</div>}

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
                <button className="device-item__revoke" onClick={() => handleUnblock(blockedUser.id)} disabled={unblockingId === blockedUser.id}>
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
