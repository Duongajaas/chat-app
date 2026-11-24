import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { GiphyFetch } from "@giphy/js-fetch-api";
import type { ExpressionMedia } from "../types";
import { isSafeExpressionUrl } from "../utils/expressions";
import { useReducedMotion } from "../hooks/useReducedMotion";

interface GifItem extends ExpressionMedia { viewed?: string; clicked?: string; sent?: string; }

function analyticsUrl(gif: unknown, action: "onload" | "onclick" | "onsent") {
  const value = (gif as { analytics?: Record<string, { url?: unknown }> }).analytics?.[action]?.url;
  return typeof value === "string" ? value : undefined;
}

function trackGiphyAction(value?: string) {
  if (!value) return;
  try {
    const url = new URL(value);
    if (url.protocol === "https:" && url.hostname === "giphy-analytics.giphy.com" && !url.username && !url.password && !url.port)
      void fetch(url.href, { mode: "no-cors", credentials: "omit", keepalive: true }).catch(() => undefined);
  } catch { /* Analytics never interrupts sending. */ }
}

export default function GifPicker({ onSelect, onClose }: { onSelect: (media: ExpressionMedia) => void; onClose: () => void }) {
  const apiKey = import.meta.env.VITE_GIPHY_API_KEY?.trim();
  const client = useMemo(() => apiKey ? new GiphyFetch(apiKey) : null, [apiKey]);
  const [query, setQuery] = useState("");
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<GifItem[]>([]);
  const [nextOffset, setNextOffset] = useState<number | null>(0);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [preview, setPreview] = useState<GifItem | null>(null);
  const [animate, setAnimate] = useState(false);
  const reduced = useReducedMotion();
  const results = useRef<HTMLDivElement>(null);
  const sentinel = useRef<HTMLDivElement>(null);
  const generation = useRef(0);
  const inFlight = useRef(false);
  const lastFailure = useRef(0);
  useEffect(() => {
    const timer = setTimeout(() => setSearch(query.trim()), 350);
    return () => clearTimeout(timer);
  }, [query]);
  const loadPage = useCallback(async (offset: number, replace = false) => {
    if (!client || inFlight.current) return;
    const version = generation.current;
    inFlight.current = true; setLoading(true); setError("");
    try {
      const delay = lastFailure.current + 6100 - Date.now();
      if (delay > 0) await new Promise(resolve => setTimeout(resolve, delay));
      if (version !== generation.current) return;
      const options = { offset, limit: 20, rating: "g" as const, lang: "vi" };
      const response = search ? await client.search(search, options) : await client.trending(options);
      if (version !== generation.current) return;
      const rows: GifItem[] = response.data.map(gif => ({
        kind: "gif", provider: "giphy", id: String(gif.id), name: gif.title || "GIF từ GIPHY",
        url: gif.images.fixed_height?.url, thumbnailUrl: gif.images.fixed_height_still?.url,
        width: Number(gif.images.fixed_height?.width) || 240, height: Number(gif.images.fixed_height?.height) || 240,
        animated: true, fileType: "image/gif", viewed: analyticsUrl(gif, "onload"),
        clicked: analyticsUrl(gif, "onclick"), sent: analyticsUrl(gif, "onsent"),
      }));
      setItems(previous => replace ? rows : [...previous, ...rows.filter(row => !previous.some(item => item.id === row.id))]);
      const next = response.pagination.offset + response.pagination.count;
      setNextOffset(response.pagination.count > 0 && next < response.pagination.total_count && next <= (search ? 4999 : 499) ? next : null);
    } catch {
      if (version === generation.current) { lastFailure.current = Date.now(); setError("Không thể tải GIF. Vui lòng thử lại."); }
    } finally {
      if (version === generation.current) { inFlight.current = false; setLoading(false); }
    }
  }, [client, search]);
  useEffect(() => {
    generation.current++; inFlight.current = false;
    setItems([]); setNextOffset(0); setPreview(null); results.current?.scrollTo({ top: 0 });
    void loadPage(0, true);
    return () => { generation.current++; };
  }, [loadPage]);
  useEffect(() => {
    if (!sentinel.current || nextOffset == null || loading || error) return;
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) void loadPage(nextOffset);
    }, { root: results.current, rootMargin: "80px" });
    observer.observe(sentinel.current);
    return () => observer.disconnect();
  }, [nextOffset, loading, error, loadPage]);
  function send(item: GifItem) {
    if (!isSafeExpressionUrl(item.url, "giphy")) { setError("GIF này không còn khả dụng. Hãy chọn GIF khác."); return; }
    trackGiphyAction(item.clicked); trackGiphyAction(item.sent);
    const { viewed: _viewed, clicked: _clicked, sent: _sent, ...media } = item;
    onSelect(media);
  }
  return <section className="gif-picker" role="region" aria-label="Chọn GIF" onKeyDown={event => { if (event.key === "Escape") onClose(); }}>
    {!client ? <p role="status">Tính năng GIF chưa được cấu hình. Vui lòng liên hệ quản trị viên.</p> : <>
      <input autoFocus type="search" maxLength={50} value={query} placeholder="Tìm GIF…" aria-label="Tìm GIF" onChange={event => setQuery(event.target.value)} />
      <label className="gif-picker__motion"><input type="checkbox" checked={animate && !reduced} disabled={reduced}
        onChange={event => setAnimate(event.target.checked)} />Xem trước ảnh động</label>
      <div className="gif-picker__results" ref={results}>
        <div className="gif-grid">{items.map(item => {
          const source = animate && !reduced ? item.url : item.thumbnailUrl;
          return <div className="gif-tile" key={item.id}>
            <button type="button" aria-label={"Gửi " + item.name} onClick={() => send(item)}>
              {isSafeExpressionUrl(source, "giphy") ? <img src={source} alt={item.name} loading="lazy" decoding="async"
                width={item.width} height={item.height} onLoad={() => trackGiphyAction(item.viewed)} />
                : <span>Không có ảnh xem trước</span>}
            </button>
            <button type="button" className="gif-tile__preview" aria-label={"Xem trước " + item.name} onClick={() => setPreview(item)}>Xem</button>
          </div>;
        })}</div>
        {loading && <p role="status">Đang tải GIF…</p>}
        {!loading && !error && !items.length && <p role="status">Không tìm thấy GIF. Hãy thử từ khóa khác.</p>}
        {error && <div role="alert"><p>{error}</p><button type="button" onClick={() => void loadPage(nextOffset ?? 0, !items.length)}>Thử lại</button></div>}
        <div ref={sentinel} />
        {!loading && !error && nextOffset != null && items.length > 0 && <button type="button" onClick={() => void loadPage(nextOffset)}>Tải thêm GIF</button>}
      </div>
      {preview && <div className="gif-preview" aria-label="Xem trước GIF">
        {isSafeExpressionUrl(reduced ? preview.thumbnailUrl : preview.url, "giphy") && <img src={(reduced ? preview.thumbnailUrl : preview.url) ?? undefined} alt={preview.name} />}
        <button type="button" onClick={() => send(preview)}>Gửi GIF này</button>
        <button type="button" onClick={() => setPreview(null)}>Đóng xem trước</button>
      </div>}
    </>}
    <a className="gif-picker__credit" href="https://giphy.com/" target="_blank" rel="noopener noreferrer">Powered by GIPHY</a>
  </section>;
}
