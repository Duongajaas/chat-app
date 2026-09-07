import type { ReactNode } from "react";

interface AuthLayoutProps {
  headline: string;
  subline: string;
  children: ReactNode;
}

export function AuthLayout({ headline, subline, children }: AuthLayoutProps) {
  return (
    <div className="auth-shell">
      <aside className="auth-brand">
        <div className="auth-brand__bubbles" aria-hidden="true">
          <span className="bubble" style={{ width: 120, height: 46, top: "18%", left: "62%" }} />
          <span className="bubble" style={{ width: 84, height: 40, top: "34%", left: "78%" }} />
          <span className="bubble" style={{ width: 100, height: 44, top: "58%", left: "60%" }} />
          <span className="bubble" style={{ width: 70, height: 38, top: "72%", left: "82%" }} />
        </div>

        <div className="auth-brand__logo">
          <span className="presence-dot" aria-hidden="true" />
          ChatApp
        </div>

        <div>
          <h1 className="auth-brand__headline">{headline}</h1>
          <p className="auth-brand__sub">{subline}</p>
        </div>

        <p className="auth-brand__foot">© {new Date().getFullYear()} ChatApp</p>
      </aside>

      <main className="auth-panel">
        <div className="auth-card">{children}</div>
      </main>
    </div>
  );
}
