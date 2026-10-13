import { useEffect, useRef, useState } from "react";
import { apiClient, getSessionVersion, onSessionCleared } from "../api/client";
import type { ChatMessage } from "../types";

type Attachment = NonNullable<ChatMessage["attachments"]>[number];
export function PrivateAttachment({ messageId, attachment }: { messageId: string; attachment: Attachment }) {
  const [url, setUrl] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const currentUrl = useRef<string>();
  const pending = useRef<AbortController>();
  useEffect(() => {
    const clear = () => {
      pending.current?.abort(); pending.current = undefined;
      if (currentUrl.current) URL.revokeObjectURL(currentUrl.current);
      currentUrl.current = undefined; setUrl(undefined); setBusy(false);
    };
    const unsubscribe = onSessionCleared(clear);
    return () => { unsubscribe(); clear(); };
  }, [messageId, attachment.id]);
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
  return <div className="private-attachment" onClick={event => event.stopPropagation()}>
    {!url && <button type="button" className="btn btn-secondary" disabled={busy} onClick={() => void load()}>
      {busy ? "Đang tải…" : `Mở ${attachment.fileName || "media"}`}
    </button>}
    {url && (attachment.fileType?.startsWith("audio/")
      ? <audio controls preload="metadata" src={url} aria-label={attachment.fileName || "Tin nhắn thoại"} />
      : <a href={url} download={attachment.fileName || "media"}>Tải {attachment.fileName || "media"}</a>)}
    {error && <small role="alert">{error}</small>}
  </div>;
}
