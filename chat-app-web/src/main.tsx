import React from "react";
import ReactDOM from "react-dom/client";
import { GoogleOAuthProvider } from "@react-oauth/google";
import App from "./App";
import "./index.css";

const googleClientId = (import.meta.env.VITE_GOOGLE_CLIENT_ID || "").trim();
try {
  const savedTheme = localStorage.getItem("chatapp-theme");
  if (savedTheme === "light" || savedTheme === "dark") document.documentElement.dataset.theme = savedTheme;
} catch { /* Storage may be unavailable in private or restricted browsers. */ }
new MutationObserver(() => {
  const theme = document.documentElement.dataset.theme;
  if (theme === "light" || theme === "dark") {
    try { localStorage.setItem("chatapp-theme", theme); } catch { /* Keep the in-memory choice. */ }
  }
}).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <GoogleOAuthProvider clientId={googleClientId}>
      <App />
    </GoogleOAuthProvider>
  </React.StrictMode>
);
