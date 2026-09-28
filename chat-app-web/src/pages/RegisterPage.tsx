import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { AuthEntryLayout } from "../components/AuthEntryLayout";
import { PasswordInput } from "../components/PasswordInput";
import { GoogleLoginButton } from "../components/GoogleLoginButton";
import { useAuth } from "../context/AuthContext";
import { extractErrorMessage } from "../api/auth";
import type { RegisterPayload } from "../types";

export default function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();

  const [form, setForm] = useState<RegisterPayload>({
    fullName: "",
    username: "",
    email: "",
    password: "",
  });
  const [error, setError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  function updateField(key: keyof RegisterPayload, value: string) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (isSubmitting) return;
    setError("");

    if (form.password.length < 8) {
      setError("Mật khẩu phải có ít nhất 8 ký tự.");
      return;
    }

    setIsSubmitting(true);
    try {
      await register({
        fullName: form.fullName.trim(),
        username: form.username.trim(),
        email: form.email.trim(),
        password: form.password,
      });
      navigate("/");
    } catch (err) {
      setError(extractErrorMessage(err, "Đăng ký thất bại. Vui lòng thử lại."));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <AuthEntryLayout register>
      <h1 className="auth-card__title">Đăng ký</h1>
      <p className="auth-card__sub">Điền thông tin bên dưới để tạo tài khoản mới.</p>

      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      <form onSubmit={handleSubmit} aria-busy={isSubmitting}>
        <div className="field">
          <label htmlFor="fullName">Họ và tên</label>
          <input
            id="fullName"
            autoComplete="name"
            placeholder="Tên hiển thị của bạn"
            type="text"
            value={form.fullName}
            onChange={(e) => updateField("fullName", e.target.value)}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="username">Username</label>
          <input
            id="username"
            autoCapitalize="none"
            spellCheck={false}
            placeholder="Nhập username của bạn"
            type="text"
            autoComplete="username"
            value={form.username}
            onChange={(e) => updateField("username", e.target.value)}
            required
            minLength={3}
          />
        </div>

        <div className="field">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            autoCapitalize="none"
            spellCheck={false}
            placeholder="ban@example.com"
            type="email"
            autoComplete="email"
            value={form.email}
            onChange={(e) => updateField("email", e.target.value)}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="password">Mật khẩu</label>
          <PasswordInput
            id="password"
            autoComplete="new-password"
            value={form.password}
            onChange={(e) => updateField("password", e.target.value)}
            required
            minLength={8}
            aria-describedby="password-hint"
          />
          <p className="auth-entry__hint" id="password-hint">Sử dụng ít nhất 8 ký tự cho mật khẩu.</p>
        </div>

        <button className="btn-primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Đang tạo tài khoản..." : "Tạo tài khoản"}
        </button>
      </form>

      {import.meta.env.VITE_GOOGLE_CLIENT_ID?.trim() && <>
      <div className="divider">hoặc tiếp tục với</div>

      <GoogleLoginButton onSuccess={() => navigate("/")} onError={(msg) => setError(msg)} />
      </>}

      <p className="auth-footer-text">
        Đã có tài khoản? <Link to="/login">Đăng nhập</Link>
      </p>
    </AuthEntryLayout>
  );
}
