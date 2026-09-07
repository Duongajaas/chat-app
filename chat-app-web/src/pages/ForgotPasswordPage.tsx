import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { AuthLayout } from "../components/AuthLayout";
import { authApi, extractErrorMessage } from "../api/auth";

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [error, setError] = useState("");
  const [isSubmitted, setIsSubmitted] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError("");
    setIsSubmitting(true);
    try {
      await authApi.forgotPassword(email.trim());
      setIsSubmitted(true);
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <AuthLayout
      headline="Quên mật khẩu cũng không sao."
      subline="Chỉ cần email đã đăng ký, chúng tôi sẽ gửi hướng dẫn đặt lại mật khẩu cho bạn."
    >
      <h2 className="auth-card__title">Quên mật khẩu</h2>
      <p className="auth-card__sub">Nhập email bạn đã dùng để đăng ký tài khoản.</p>

      {error && <div className="alert alert-danger">{error}</div>}

      {isSubmitted ? (
        <div className="alert alert-success">
          Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi tới hộp thư của bạn.
        </div>
      ) : (
        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="email">Email</label>
            <input
              id="email"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>

          <button className="btn-primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Đang gửi..." : "Gửi hướng dẫn"}
          </button>
        </form>
      )}

      <p className="auth-footer-text">
        Nhớ ra mật khẩu rồi? <Link to="/login">Quay lại đăng nhập</Link>
      </p>
    </AuthLayout>
  );
}
