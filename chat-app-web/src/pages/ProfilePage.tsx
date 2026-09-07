import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { usersApi } from "../api/users";
import { extractErrorMessage } from "../api/auth";
import { Avatar } from "../components/Avatar";

export default function ProfilePage() {
  const { user, setUser } = useAuth();
  const [fullName, setFullName] = useState(user?.fullName ?? "");
  const [bio, setBio] = useState(user?.bio ?? "");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setSuccess(false);
    setIsSubmitting(true);
    try {
      const updated = await usersApi.updateMe({ fullName: fullName.trim(), bio: bio.trim() || undefined });
      setUser(updated);
      setSuccess(true);
    } catch (err) {
      setError(extractErrorMessage(err, "Cập nhật thất bại, thử lại sau."));
    } finally {
      setIsSubmitting(false);
    }
  }

  if (!user) return null;

  return (
    <div className="profile-page">
      <div className="profile-card">
        <Link to="/" className="profile-back-link">← Quay lại trò chuyện</Link>

        <div className="profile-card__header">
          <Avatar name={user.fullName || user.username} size={72} />
          <div>
            <h2>{user.fullName}</h2>
            <p className="profile-card__username">@{user.username}</p>
          </div>
        </div>

        {error && <div className="alert alert-danger">{error}</div>}
        {success && <div className="alert alert-success">Đã lưu thay đổi.</div>}

        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="fullName">Họ và tên</label>
            <input id="fullName" value={fullName} onChange={(e) => setFullName(e.target.value)} required />
          </div>
          <div className="field">
            <label>Username</label>
            <input value={user.username} disabled />
          </div>
          <div className="field">
            <label>Email</label>
            <input value={user.email ?? ""} disabled />
          </div>
          <div className="field">
            <label htmlFor="bio">Giới thiệu</label>
            <textarea id="bio" rows={3} value={bio} onChange={(e) => setBio(e.target.value)} maxLength={255} />
          </div>
          <button className="btn-primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Đang lưu..." : "Lưu thay đổi"}
          </button>
          <Link to="/devices" className="auth-footer-text" style={{ display: "block", marginTop: 16 }}>
             Quản lý thiết bị đang đăng nhập →
          </Link>
          <Link to="/blocked-users" className="auth-footer-text" style={{ display: "block", marginTop: 10 }}>
             Người dùng đã chặn →
          </Link>
        </form>
      </div>
    </div>
  );
}
