import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { QRCodeSVG } from "qrcode.react";
import { friendLinksApi, type FriendLink } from "../api/friendLinks";
import { extractErrorMessage } from "../api/auth";

export default function FriendLinkPage() {
  const [link, setLink] = useState<FriendLink | null>(null);
  const [error, setError] = useState("");
  const [copied, setCopied] = useState(false);
  const [isBusy, setIsBusy] = useState(false);

  useEffect(() => {
    friendLinksApi.getMine().then(setLink).catch((reason) => setError(extractErrorMessage(reason)));
  }, []);

  async function copyLink() {
    if (!link) return;
    await navigator.clipboard.writeText(link.url);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  }

  async function regenerate() {
    setIsBusy(true);
    setError("");
    try { setLink(await friendLinksApi.regenerate()); }
    catch (reason) { setError(extractErrorMessage(reason)); }
    finally { setIsBusy(false); }
  }

  async function revoke() {
    if (!link) return;
    setIsBusy(true);
    try { await friendLinksApi.revoke(link.id); setLink(null); }
    catch (reason) { setError(extractErrorMessage(reason)); }
    finally { setIsBusy(false); }
  }

  return (
    <main className="friend-link-page">
      <section className="friend-link-card">
        <Link className="profile-back-link" to="/">← Quay lại trò chuyện</Link>
        <h1>Link kết bạn</h1>
        <p className="friend-link-card__sub">Chia sẻ link hoặc mã QR này. Người nhận vẫn cần gửi lời mời kết bạn.</p>
        {error && <div className="alert alert-danger">{error}</div>}
        {!link && !error && <p className="friend-link-card__loading">Đang tạo link...</p>}
        {link && (
          <>
            <div className="friend-link-qr"><QRCodeSVG value={link.url} size={184} includeMargin /></div>
            <div className="friend-link-url">{link.url}</div>
            <button className="btn-primary" type="button" onClick={copyLink}>{copied ? "Đã sao chép" : "Sao chép link"}</button>
            <div className="friend-link-meta">Đã dùng {link.usedCount}{link.maxUses === null ? " lần" : `/${link.maxUses} lần`}</div>
            <div className="friend-link-actions">
              <button type="button" onClick={regenerate} disabled={isBusy}>Tạo link mới</button>
              <button type="button" onClick={revoke} disabled={isBusy}>Thu hồi link</button>
            </div>
          </>
        )}
      </section>
    </main>
  );
}