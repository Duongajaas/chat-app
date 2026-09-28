import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { groupsApi } from "../api/groups";
import { extractErrorMessage } from "../api/auth";
import { getSessionVersion } from "../api/client";

export default function JoinGroupPage() {
  const navigate = useNavigate();
  const [code] = useState(() => new URLSearchParams(window.location.hash.slice(1)).get("code") ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => { window.history.replaceState(window.history.state, "", window.location.pathname); }, []);
  async function join() {
    const session = getSessionVersion();
    setBusy(true); setError("");
    try {
      const group = await groupsApi.join(code);
      if (session === getSessionVersion()) navigate("/", { replace: true, state: { activeConversationId: group.id } });
    } catch (e) { if (session === getSessionVersion()) { setError(extractErrorMessage(e)); setBusy(false); } }
  }
  return <main className="group-settings">
    <h1>Tham gia nhóm</h1>
    <p>Bạn chỉ đọc được các tin nhắn trong khoảng thời gian là thành viên. Khi vào lại, tin gửi trong lúc vắng mặt vẫn không hiển thị.</p>
    {!code && <p>Link thiếu mã mời. Hãy mở lại link được chia sẻ.</p>}
    {error && <p role="alert">{error}</p>}
    <button disabled={busy || !code} onClick={() => void join()}>{busy ? "Đang tham gia…" : "Tham gia nhóm"}</button>
    <p><Link to="/">Về trò chuyện</Link></p>
  </main>;
}
