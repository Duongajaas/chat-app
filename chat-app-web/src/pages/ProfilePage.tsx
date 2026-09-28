import { ArrowLeftIcon, ArrowRightIcon } from "../components/icons";
import { useState, type FormEvent } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { usersApi } from "../api/users";
import { extractErrorMessage } from "../api/auth";
import { Avatar } from "../components/Avatar";

export default function ProfilePage() {
  const { user, setUser, logout } = useAuth();
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
        <Link to="/" className="profile-back-link"><ArrowLeftIcon /><span>Quay lại trò chuyện</span></Link>

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
          <Link to="/blocked-users" className="auth-footer-text" style={{ display: "block", marginTop: 10 }}>
             Người dùng đã chặn <ArrowRightIcon />
          </Link>
        </form>
        <Link to="/settings" state={{ returnTo: "/profile" }} className="settings-link">Cài đặt <ArrowRightIcon /></Link>
        <button type="button" className="device-item__revoke" style={{ marginTop: 16 }} onClick={logout}>Đăng xuất</button>

      </div>
    </div>
  );
}

export function SettingsPage() {
  const location = useLocation();
  const returnTo = location.state?.returnTo === "/profile" ? "/profile" : "/";
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme === "dark" ? "dark" : "light");
  function selectTheme(value: "light" | "dark") {
    document.documentElement.dataset.theme = value;
    setTheme(value);
    try { localStorage.setItem("chatapp-theme", value); } catch { /* Theme still works when storage is unavailable. */ }
  }
  return <main className="profile-page">
    <div className="profile-card settings-card">
      <Link to={returnTo} className="profile-back-link"><ArrowLeftIcon /><span>{returnTo === "/profile" ? "Quay lại trang cá nhân" : "Quay lại trò chuyện"}</span></Link>
      <h1>Cài đặt</h1>
      <p className="settings-description">Tùy chỉnh giao diện và quản lý tài khoản của bạn.</p>
      <section className="settings-section">
        <h2>Giao diện</h2>
        <p className="settings-description">Lựa chọn được lưu trên trình duyệt này.</p>
        <fieldset className="settings-themes"><legend className="settings-legend">Chọn giao diện</legend>
          <label><input type="radio" name="theme" value="light" checked={theme === "light"} onChange={() => selectTheme("light")} /><span>Sáng <small>Light theme</small></span></label>
          <label><input type="radio" name="theme" value="dark" checked={theme === "dark"} onChange={() => selectTheme("dark")} /><span>Tối <small>Dark theme</small></span></label>
        </fieldset>
      </section>
      <section className="settings-section"><h2>Kết nối và thiết bị</h2>
        <Link to="/friend-link" state={{ returnTo }} className="settings-link"><span>Link kết bạn<small>Chia sẻ liên kết hoặc mã QR của bạn</small></span><ArrowRightIcon /></Link>
        <Link to="/devices" state={{ returnTo }} className="settings-link"><span>Quản lý thiết bị<small>Xem và đăng xuất các phiên đăng nhập</small></span><ArrowRightIcon /></Link>
      </section>
    </div>
  </main>;
}


