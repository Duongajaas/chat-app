import { useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { AuthEntryLayout } from "../components/AuthEntryLayout";
import { PasswordInput } from "../components/PasswordInput";
import { GoogleLoginButton } from "../components/GoogleLoginButton";
import { useAuth } from "../context/AuthContext";
import { extractErrorMessage } from "../api/auth";

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const returnPath = (location.state as { from?: { pathname?: string; search?: string; hash?: string } } | null)?.from;

  function navigateAfterLogin() {
    navigate(returnPath ? `${returnPath.pathname ?? "/"}${returnPath.search ?? ""}${returnPath.hash ?? ""}` : "/", { replace: true });
  }

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (isSubmitting) return;
    setError("");
    setIsSubmitting(true);
    try {
      await login(username.trim(), password);
      navigateAfterLogin();
    } catch (err) {
      setError(extractErrorMessage(err, "Username hoặc mật khẩu không đúng."));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <AuthEntryLayout>
      <h1 className="auth-card__title">Đăng nhập</h1>
      <p className="auth-card__sub">Chào mừng trở lại! Nhập thông tin để tiếp tục.</p>

      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      <form onSubmit={handleSubmit} aria-busy={isSubmitting}>
        <div className="field">
          <label htmlFor="username">Username</label>
          <input
            id="username"
            autoCapitalize="none"
            spellCheck={false}
            placeholder="Nhập username của bạn"
            type="text"
            autoComplete="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="password">Mật khẩu</label>
          <PasswordInput
            id="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>

        <div className="field-row-between">
          <span />
          <Link to="/forgot-password">Quên mật khẩu?</Link>
        </div>

        <button className="btn-primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Đang đăng nhập..." : "Đăng nhập"}
        </button>
      </form>

      {import.meta.env.VITE_GOOGLE_CLIENT_ID?.trim() && <>
      <div className="divider">hoặc tiếp tục với</div>

      <GoogleLoginButton onSuccess={navigateAfterLogin} onError={(msg) => setError(msg)} />
      </>}

      <p className="auth-footer-text">
        Chưa có tài khoản? <Link to="/register">Đăng ký ngay</Link>
      </p>
    </AuthEntryLayout>
  );
}
