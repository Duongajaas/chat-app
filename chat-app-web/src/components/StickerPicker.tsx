import { useEffect, useState } from "react";
import { expressionsApi } from "../api/expressions";
import type { ExpressionMedia, StickerPack } from "../types";
import { isSafeExpressionUrl, type ExpressionHistory } from "../utils/expressions";
import { useReducedMotion } from "../hooks/useReducedMotion";

export default function StickerPicker({ history, onSelect, onFavorite }: {
  history: ExpressionHistory; onSelect: (sticker: ExpressionMedia) => void; onFavorite: (id: string) => void;
}) {
  const [packs, setPacks] = useState<StickerPack[]>([]);
  const [active, setActive] = useState("all");
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  const [preview, setPreview] = useState<ExpressionMedia | null>(null);
  const reduced = useReducedMotion();
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError("");
    expressionsApi.stickerPacks(controller.signal).then(items => { if (!controller.signal.aborted) setPacks(items); })
      .catch(() => { if (!controller.signal.aborted) setError("Không thể tải bộ sticker. Vui lòng thử lại."); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [attempt]);
  const all = packs.flatMap(pack => pack.stickers);
  const pool = active === "recent" ? history.recentStickers.map(id => all.find(item => item.id === id)).filter((item): item is ExpressionMedia => !!item)
    : active === "favorites" ? all.filter(item => history.favoriteStickers.includes(item.id))
    : active === "all" ? all : all.filter(item => item.packId === active);
  const visible = pool.filter(item => item.name.toLocaleLowerCase("vi").includes(query.toLocaleLowerCase("vi")));
  return <section className="sticker-picker" aria-label="Chọn sticker">
    <input autoFocus type="search" aria-label="Tìm sticker" placeholder="Tìm sticker…" value={query} onChange={event => setQuery(event.target.value)} />
    <div className="expression-filters" role="group" aria-label="Bộ sticker">
      {[{ id: "all", name: "Tất cả" }, { id: "recent", name: "Gần đây" }, { id: "favorites", name: "Yêu thích" }, ...packs].map(pack =>
        <button type="button" key={pack.id} aria-pressed={active === pack.id} onClick={() => { setActive(pack.id); setPreview(null); }}>{pack.name}</button>)}
    </div>
    {loading && <p role="status">Đang tải sticker…</p>}
    {error && <div role="alert"><p>{error}</p><button type="button" onClick={() => setAttempt(value => value + 1)}>Thử lại</button></div>}
    {!loading && !error && !visible.length && <p role="status">Chưa có sticker trong mục này.</p>}
    <div className="sticker-grid">
      {visible.map(item => {
        const thumbnail = item.thumbnailUrl ?? item.url;
        if (!isSafeExpressionUrl(thumbnail, "local")) return null;
        return <div className="sticker-tile" key={item.id}>
          <button type="button" className="sticker-tile__send" aria-label={`Gửi sticker ${item.name}`} onClick={() => onSelect(item)}>
            <img src={thumbnail} alt={item.name} loading="lazy" width={96} height={96} />
          </button>
          <div className="sticker-tile__tools">
            <button type="button" aria-label={`Xem trước ${item.name}`} onClick={() => setPreview(item)}>Xem</button>
            <button type="button" aria-label={`Yêu thích ${item.name}`} aria-pressed={history.favoriteStickers.includes(item.id)} onClick={() => onFavorite(item.id)}>
              {history.favoriteStickers.includes(item.id) ? "♥" : "♡"}
            </button>
          </div>
        </div>;
      })}
    </div>
    {preview && <div className="sticker-preview" aria-label={`Xem trước ${preview.name}`}>
      {isSafeExpressionUrl(reduced ? preview.thumbnailUrl : preview.url, "local") &&
        <img src={(reduced ? preview.thumbnailUrl : preview.url) ?? undefined} alt={preview.name} width={120} height={120} />}
      <div><strong>{preview.name}</strong><button type="button" onClick={() => onSelect(preview)}>Gửi sticker</button>
        <button type="button" onClick={() => setPreview(null)}>Đóng xem trước</button></div>
    </div>}
    <small className="expression-credit">Bộ sticker gốc của ChatApp</small>
  </section>;
}
