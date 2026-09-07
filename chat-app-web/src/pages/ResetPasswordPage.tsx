import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { AuthLayout } from "../components/AuthLayout";
import { authApi, extractErrorMessage } from "../api/auth";

export default function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") || "";
  const navigate = useNavigate();

  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError("");

    if (!token) {
      setError("Liên kết đặt lại mật khẩu không hợp lệ. Vui lòng yêu cầu lại.");
      return;
    }
    if (newPassword.length < 8) {
      setError("Mật khẩu phải có ít nhất 8 ký tự.");
      return;
    }
    if (newPassword !== confirmPassword) {
      setError("Mật khẩu nhập lại không khớp.");
      return;
    }

    setIsSubmitting(true);
    try {
      await authApi.resetPassword(token, newPassword);
      navigate("/login", { state: { resetSuccess: true } });
    } catch (err) {
      setError(extractErrorMessage(err, "Token không hợp lệ hoặc đã hết hạn."));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <AuthLayout
      headline="Đặt lại mật khẩu mới."
      subline="Sau khi đổi mật khẩu, bạn sẽ được đăng xuất khỏi tất cả thiết bị khác để đảm bảo an toàn."
    >
      <h2 className="auth-card__title">Mật khẩu mới</h2>
      <p className="auth-card__sub">Nhập mật khẩu mới cho tài khoản của bạn.</p>

      {error && <div className="alert alert-danger">{error}</div>}

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="newPassword">Mật khẩu mới</label>
          <input
            id="newPassword"
            type="password"
            autoComplete="new-password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            required
            minLength={8}
          />
        </div>

        <div className="field">
          <label htmlFor="confirmPassword">Nhập lại mật khẩu</label>
          <input
            id="confirmPassword"
            type="password"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
            minLength={8}
          />
        </div>

        <button className="btn-primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Đang lưu..." : "Đặt lại mật khẩu"}
        </button>
      </form>

      <p className="auth-footer-text">
        <Link to="/login">Quay lại đăng nhập</Link>
      </p>
    </AuthLayout>
  );
}
