import { useEffect, useRef, useState } from "react";
import { apiClient, getSessionVersion, onSessionCleared } from "../api/client";
import type { ChatMessage } from "../types";
import { fileBadge, formatBytes, isImageType } from "../utils/attachments";

type Attachment = NonNullable<ChatMessage["attachments"]>[number];

export function PrivateAttachment({ messageId, attachment }: { messageId: string; attachment: Attachment }) {
  const [url, setUrl] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const currentUrl = useRef<string>();
  const pending = useRef<Promise<string | undefined>>();
  const controllerRef = useRef<AbortController>();
  const container = useRef<HTMLDivElement>(null);
  const image = isImageType(attachment.fileType);
  const audio = !!attachment.fileType?.startsWith("audio/");
  useEffect(() => {
    const clear = () => {
      controllerRef.current?.abort(); controllerRef.current = undefined; pending.current = undefined;
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

  /** Fetches the attachment once (through the authorized API) and returns a local object URL. */
  function load(): Promise<string | undefined> {
    if (currentUrl.current) return Promise.resolve(currentUrl.current);
    if (pending.current) return pending.current;
    const controller = new AbortController(); controllerRef.current = controller;
    const session = getSessionVersion(); setBusy(true); setError("");
    const request = (async () => {
      try {
        const result = await apiClient.get<Blob>(`/messages/${encodeURIComponent(messageId)}/attachments/${encodeURIComponent(attachment.id)}`,
          { responseType: "blob", signal: controller.signal });
        if (controller.signal.aborted || session !== getSessionVersion()) return undefined;
        const objectUrl = URL.createObjectURL(result.data);
        currentUrl.current = objectUrl; setUrl(objectUrl);
        return objectUrl;
      } catch {
        if (!controller.signal.aborted && session === getSessionVersion()) setError("Không tải được file hoặc bạn không còn quyền đọc.");
        return undefined;
      } finally {
        if (controllerRef.current === controller) { controllerRef.current = undefined; pending.current = undefined; setBusy(false); }
      }
    })();
    pending.current = request;
    return request;
  }

  async function download() {
    const objectUrl = await load();
    if (!objectUrl) return;
    // Files are always saved, never rendered, so documents cannot run in the app's origin.
    const link = document.createElement("a");
    link.href = objectUrl; link.download = attachment.fileName || "tep-dinh-kem";
    document.body.appendChild(link); link.click(); link.remove();
  }

  const name = attachment.fileName || (image ? "ảnh" : "tệp đính kèm");
  if (!image && !audio) {
    const size = formatBytes(attachment.fileSize);
    return <div ref={container} className="private-attachment" onClick={event => event.stopPropagation()}>
      <button type="button" className="file-card" disabled={busy} onClick={() => void download()}
        aria-label={`Tải xuống ${name}${size ? `, ${size}` : ""}`} title={`Tải xuống ${name}`}>
        <span className="file-card__badge" aria-hidden="true">{fileBadge(attachment.fileName)}</span>
        <span className="file-card__info">
          <span className="file-card__name">{name}</span>
          <span className="file-card__meta">{busy ? "Đang tải xuống…" : [size, "Nhấn để tải xuống"].filter(Boolean).join(" · ")}</span>
        </span>
      </button>
      {error && <small role="alert">{error}</small>}
    </div>;
  }
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
      : <audio controls preload="metadata" src={url} aria-label={attachment.fileName || "Tin nhắn thoại"} />)}
    {error && <small role="alert">{error}</small>}
  </div>;
}
