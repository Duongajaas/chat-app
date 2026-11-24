import { useEffect, useRef, useState } from "react";
import { GiphyFetch } from "@giphy/js-fetch-api";
import type { ExpressionMedia } from "../types";
import { isSafeExpressionUrl } from "../utils/expressions";
import { useReducedMotion } from "../hooks/useReducedMotion";

export function GifMessage({ media, url, onLoad }: { media?: ExpressionMedia; url?: string; onLoad?: () => void }) {
  const local = media?.provider === "local";
  const reduced = useReducedMotion();
  const container = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);
  const [resolved, setResolved] = useState<{ url: string; thumbnail: string; width: number; height: number } | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [replay, setReplay] = useState(0);
  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) { setVisible(true); observer.disconnect(); }
    }, { rootMargin: "150px" });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    if (!visible || local || !media?.id) return;
    const key = import.meta.env.VITE_GIPHY_API_KEY?.trim();
    if (!key) { setFailed(true); return; }
    let current = true;
    setFailed(false);
    new GiphyFetch(key).gif(media.id).then(response => {
      const gif = response.data;
      const image = gif?.images?.fixed_height;
      const still = gif?.images?.fixed_height_still;
      if (!image || String(gif.id) !== media.id || !isSafeExpressionUrl(image.url, "giphy") || !isSafeExpressionUrl(still?.url, "giphy"))
        throw new Error("GIF không còn khả dụng.");
      if (current) setResolved({ url: image.url, thumbnail: still.url, width: Number(image.width) || 240, height: Number(image.height) || 240 });
    }).catch(() => { if (current) setFailed(true); });
    return () => { current = false; };
  }, [visible, local, media?.id, attempt]);
  const animated = playing || !reduced;
  const source = local ? (animated || !media?.animated ? media?.url : media?.thumbnailUrl)
    : resolved ? (animated ? resolved.url : resolved.thumbnail) : animated ? media?.url ?? url : media?.thumbnailUrl;
  const safe = isSafeExpressionUrl(source, local ? "local" : "giphy");
  const name = media?.name || "GIF từ GIPHY";
  return <div ref={container} className={local ? "message-sticker" : "message-gif"}>
    {failed || (!safe && (local || !media?.id) && !(reduced && !playing && !local)) ? <div role="status"><span>{local ? "Sticker" : "GIF"} không còn khả dụng.</span>
      {!local && <button type="button" onClick={() => { setFailed(false); setResolved(null); setAttempt(value => value + 1); }}>Tải lại GIF</button>}</div>
      : visible && safe ? <img key={`${source}:${replay}`} src={source} alt={name} loading="lazy" decoding="async" referrerPolicy="no-referrer"
        width={resolved?.width ?? media?.width ?? 240} height={resolved?.height ?? media?.height ?? 240}
        onLoad={onLoad} onError={() => setFailed(true)} /> : <span role="status">{reduced && !playing && !local && !media?.id ? "Ảnh động đang tạm dừng." : "Đang tải biểu cảm…"}</span>}
    {!failed && (media?.animated || !local) && <button className="expression-replay" type="button" onClick={() => { setPlaying(value => reduced ? !value : true); setReplay(value => value + 1); }}>
      {reduced ? playing ? "Dừng ảnh động" : "Phát ảnh động" : "Phát lại"}</button>}
    {!local && <a className="message-gif__credit" href="https://giphy.com/" target="_blank" rel="noopener noreferrer">Powered by GIPHY</a>}
  </div>;
}
