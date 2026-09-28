import { useEffect, type ReactNode } from "react";


function Brand() {
  return <div className="auth-entry__brand"><svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M20 11.5a8 8 0 0 1-8 8H5l-3 2v-10a9 9 0 0 1 18 0Z" /><path d="M7 10h8M7 14h5" /></svg>ChatApp</div>;
}

export function AuthEntryLayout({ children, register = false }: { children: ReactNode; register?: boolean }) {
  useEffect(() => { document.title = `${register ? "Đăng ký" : "Đăng nhập"} · ChatApp`; }, [register]);
  return <div className="auth-shell auth-entry">
    <aside className="auth-entry__aside">
      <Brand />
      <div className="auth-entry__intro">
        <p className="auth-entry__eyebrow">BẠN BÈ · NHÓM · TRÒ CHUYỆN</p>
        <h2>Một nơi để<br />giữ kết nối.</h2>
        <p>Những cuộc trò chuyện với bạn bè và nhóm của bạn, cùng trong ChatApp.</p>
        <div className="auth-entry__note"><span aria-hidden="true">↗</span><p>{register ? "Bắt đầu bằng một tài khoản của riêng bạn." : "Đăng nhập để tiếp tục cuộc trò chuyện."}</p></div>
      </div>
      <p className="auth-entry__copyright">© {new Date().getFullYear()} ChatApp</p>
    </aside>
    <main className="auth-panel auth-entry__panel">
      <div className="auth-card"><div className="auth-entry__mobile-brand"><Brand /></div>{children}</div>
    </main>
  </div>;
}
