import { useEffect, useRef, useState } from "react";
import { apiClient, getSessionVersion, onSessionCleared } from "../api/client";
import type { ChatMessage } from "../types";

type Attachment = NonNullable<ChatMessage["attachments"]>[number];
const isImage = (type: string | null | undefined) => type === "image/jpeg" || type === "image/png" || type === "image/webp";

export function PrivateAttachment({ messageId, attachment }: { messageId: string; attachment: Attachment }) {
  const [url, setUrl] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const currentUrl = useRef<string>();
  const pending = useRef<AbortController>();
  const container = useRef<HTMLDivElement>(null);
  const image = isImage(attachment.fileType);
  useEffect(() => {
    const clear = () => {
      pending.current?.abort(); pending.current = undefined;
      if (currentUrl.current) URL.revokeObjectURL(currentUrl.current);
      currentUrl.current = undefined; setUrl(undefined); setBusy(false);
    };
    const unsubscribe = onSessionCleared(clear);
    return () => { unsubscribe(); clear(); };
  }, [messageId, attachment.id]);
  // Images preview automatically, but only once scrolled into view, to stay within the media-read rate limit.
  // Audio and other files still load only on demand.
  useEffect(() => {
    if (!image || !container.current) return;
    if (typeof IntersectionObserver === "undefined") { void load(); return; }
    const observer = new IntersectionObserver(entries => {
      if (entries.some(e => e.isIntersecting)) { observer.disconnect(); void load(); }
    }, { rootMargin: "200px" });
    observer.observe(container.current);
    return () => observer.disconnect();
  }, [image, messageId, attachment.id]);
  async function load() {
    if (pending.current || currentUrl.current) return;
    const controller = new AbortController(); pending.current = controller;
    const session = getSessionVersion(); setBusy(true); setError("");
    try {
      const result = await apiClient.get<Blob>(`/messages/${encodeURIComponent(messageId)}/attachments/${encodeURIComponent(attachment.id)}`,
        { responseType: "blob", signal: controller.signal });
      if (controller.signal.aborted || session !== getSessionVersion()) return;
      const objectUrl = URL.createObjectURL(result.data);
      currentUrl.current = objectUrl; setUrl(objectUrl);
    } catch {
      if (!controller.signal.aborted && session === getSessionVersion()) setError("Không tải được media hoặc bạn không còn quyền đọc.");
    } finally {
      if (pending.current === controller) { pending.current = undefined; setBusy(false); }
    }
  }
  const name = attachment.fileName || (image ? "ảnh" : "media");
  return <div ref={container} className="private-attachment" onClick={event => event.stopPropagation()}>
    {!url && (image && !error
      ? <div className="private-attachment__image-placeholder" role="status">{busy ? "Đang tải ảnh…" : "Ảnh"}</div>
      : <button type="button" className="btn btn-secondary" disabled={busy} onClick={() => void load()}>
          {busy ? "Đang tải…" : image ? `Tải lại ${name}` : `Mở ${name}`}
        </button>)}
    {url && (image
      ? <a href={url} target="_blank" rel="noopener noreferrer" className="private-attachment__image-link" title="Mở ảnh gốc">
          <img src={url} alt={attachment.fileName || "Ảnh đã gửi"} className="private-attachment__image" />
        </a>
      : attachment.fileType?.startsWith("audio/")
        ? <audio controls preload="metadata" src={url} aria-label={attachment.fileName || "Tin nhắn thoại"} />
        : <a href={url} download={name}>Tải {name}</a>)}
    {error && <small role="alert">{error}</small>}
  </div>;
}
