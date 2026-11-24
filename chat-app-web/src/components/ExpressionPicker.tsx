import { Component, Suspense, lazy, useEffect, useId, useRef, useState, type ReactNode } from "react";
import type { ExpressionMedia } from "../types";
import { readExpressionHistory, rememberEmoji, rememberSticker, saveExpressionHistory, toggleFavoriteSticker, type ExpressionHistory } from "../utils/expressions";

const EmojiPanel = lazy(() => import("./EmojiPanel"));
const StickerPicker = lazy(() => import("./StickerPicker"));
const GifPicker = lazy(() => import("./GifPicker"));
export type ExpressionTab = "emoji" | "sticker" | "gif";
const tabs: { id: ExpressionTab; name: string }[] = [{ id: "emoji", name: "😀 Emoji" }, { id: "sticker", name: "🐱 Sticker" }, { id: "gif", name: "GIF" }];

class PickerBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? <p role="alert">Không thể mở bảng chọn. Đóng rồi mở lại để thử lại.</p> : this.props.children; }
}

export default function ExpressionPicker({ accountId, initialTab, onEmoji, onSelect, onClose }: {
  accountId: string; initialTab: ExpressionTab; onEmoji: (emoji: string) => boolean;
  onSelect: (media: ExpressionMedia) => void; onClose: (restoreFocus?: boolean) => void;
}) {
  const [tab, setTab] = useState<ExpressionTab>(initialTab);
  const [history, setHistory] = useState(() => readExpressionHistory(accountId));
  const [dark, setDark] = useState(document.documentElement.dataset.theme === "dark");
  const panel = useRef<HTMLElement>(null);
  const id = useId();
  useEffect(() => {
    const outside = (event: PointerEvent) => {
      if (!panel.current?.contains(event.target as Node) && !(event.target as Element).closest?.("[data-expression-trigger]")) onClose(false);
    };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { event.preventDefault(); onClose(); } };
    const observer = new MutationObserver(() => setDark(document.documentElement.dataset.theme === "dark"));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    const composer = panel.current?.parentElement?.querySelector(".chat-window__composer");
    const size = new ResizeObserver(() => {
      if (panel.current && composer) panel.current.style.setProperty("--expression-offset", `${composer.getBoundingClientRect().height + 8}px`);
    });
    if (composer) size.observe(composer);
    document.addEventListener("pointerdown", outside); document.addEventListener("keydown", escape);
    panel.current?.querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]')?.focus();
    return () => { observer.disconnect(); size.disconnect(); document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [onClose]);
  function updateHistory(next: ExpressionHistory) { saveExpressionHistory(accountId, next); setHistory(next); }
  return <section ref={panel} id="expression-picker" className="expression-picker" role="dialog" aria-label="Emoji, Sticker và GIF">
    <header className="expression-picker__header">
      <div role="tablist" aria-label="Loại biểu cảm">
        {tabs.map((item, index) => <button key={item.id} type="button" role="tab" id={`${id}-${item.id}`} aria-controls={`${id}-panel`}
          aria-selected={tab === item.id} tabIndex={tab === item.id ? 0 : -1} onClick={() => setTab(item.id)}
          onKeyDown={event => {
            const next = event.key === "ArrowRight" ? (index + 1) % tabs.length : event.key === "ArrowLeft" ? (index + tabs.length - 1) % tabs.length
              : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
            if (next != null) { event.preventDefault(); setTab(tabs[next].id); document.getElementById(`${id}-${tabs[next].id}`)?.focus(); }
          }}>{item.name}</button>)}
      </div>
      <button type="button" className="expression-picker__close" aria-label="Đóng bảng biểu cảm" onClick={() => onClose()}>×</button>
    </header>
    <div id={`${id}-panel`} role="tabpanel" aria-labelledby={`${id}-${tab}`} className="expression-picker__panel">
      <PickerBoundary key={tab}><Suspense fallback={<p role="status">Đang mở bảng chọn…</p>}>
        {tab === "emoji" && <EmojiPanel dark={dark} history={history} onSelect={(emoji, unified) => {
          if (onEmoji(emoji)) updateHistory(rememberEmoji(history, emoji, unified));
        }} />}
        {tab === "sticker" && <StickerPicker history={history} onSelect={media => { updateHistory(rememberSticker(history, media.id)); onSelect(media); }}
          onFavorite={stickerId => updateHistory(toggleFavoriteSticker(history, stickerId))} />}
        {tab === "gif" && <GifPicker onSelect={onSelect} onClose={() => onClose()} />}
      </Suspense></PickerBoundary>
    </div>
  </section>;
}
