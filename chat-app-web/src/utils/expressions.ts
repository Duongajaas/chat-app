export function insertEmojiAtSelection(value: string, emoji: string, start: number, end: number, limit = 4000) {
  const from = Math.max(0, Math.min(value.length, start));
  const to = Math.max(from, Math.min(value.length, end));
  const next = value.slice(0, from) + emoji + value.slice(to);
  return next.length > limit ? null : { value: next, caret: from + emoji.length };
}

interface EmojiUsage { emoji: string; unified: string; count: number; usedAt: number; }
export interface ExpressionHistory { recentStickers: string[]; favoriteStickers: string[]; emojis: EmojiUsage[]; }
const emptyHistory = (): ExpressionHistory => ({ recentStickers: [], favoriteStickers: [], emojis: [] });

export function readExpressionHistory(accountId: string): ExpressionHistory {
  try {
    const data: unknown = JSON.parse(localStorage.getItem(`chatapp-expressions:${accountId}`) ?? "null");
    if (!data || typeof data !== "object") return emptyHistory();
    const candidate = data as Partial<ExpressionHistory>;
    const ids = (value: unknown) => Array.isArray(value) ? value.filter((id): id is string => typeof id === "string" && id.length <= 80).slice(0, 60) : [];
    return { recentStickers: ids(candidate.recentStickers), favoriteStickers: ids(candidate.favoriteStickers),
      emojis: Array.isArray(candidate.emojis) ? candidate.emojis.filter(item => item && typeof item.emoji === "string" &&
        item.emoji.length <= 32 && typeof item.unified === "string" && /^[a-f0-9-]+$/i.test(item.unified) &&
        Number.isFinite(item.count) && item.count > 0 && Number.isFinite(item.usedAt)).slice(0, 60) : [] };
  } catch { return emptyHistory(); }
}

export function saveExpressionHistory(accountId: string, history: ExpressionHistory) {
  try { localStorage.setItem(`chatapp-expressions:${accountId}`, JSON.stringify(history)); }
  catch { /* Picking remains available when browser storage is full or disabled. */ }
}

export function rememberEmoji(history: ExpressionHistory, emoji: string, unified: string): ExpressionHistory {
  const previous = history.emojis.find(item => item.unified === unified);
  return { ...history, emojis: [{ emoji, unified, count: (previous?.count ?? 0) + 1, usedAt: Date.now() },
    ...history.emojis.filter(item => item.unified !== unified)].slice(0, 60) };
}

export function rememberSticker(history: ExpressionHistory, id: string): ExpressionHistory {
  return { ...history, recentStickers: [id, ...history.recentStickers.filter(item => item !== id)].slice(0, 30) };
}

export function toggleFavoriteSticker(history: ExpressionHistory, id: string): ExpressionHistory {
  return { ...history, favoriteStickers: history.favoriteStickers.includes(id)
    ? history.favoriteStickers.filter(item => item !== id) : [id, ...history.favoriteStickers].slice(0, 60) };
}

export function isSafeExpressionUrl(value: string | null | undefined, provider: "local" | "giphy"): value is string {
  if (!value) return false;
  if (provider === "local") return /^\/stickers\/[a-z0-9-]+\.(?:svg|webp|png|gif)$/.test(value);
  try {
    const url = new URL(value);
    return url.protocol === "https:" && !url.username && !url.password && !url.port &&
      /^media\d*\.giphy\.com$/.test(url.hostname) && /^\/media\//.test(url.pathname);
  } catch { return false; }
}
