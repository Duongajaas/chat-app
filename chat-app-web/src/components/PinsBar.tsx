import { useEffect, useId, useRef, useState } from "react";
import type { ChatMessage, PinnedMessage } from "../types";
import { ChevronDownIcon, MoreIcon, PinIcon } from "./icons";

export function messagePreview(message: ChatMessage) {
  if (["Deleted", "Hidden", "Unavailable"].includes(message.status ?? "")) return "Tin nhắn gốc không khả dụng";
  if (message.type === "CallLog") return "Cuộc gọi";
  return message.content?.trim() || (message.voiceDuration != null || message.type === "Audio" ? "Tin nhắn thoại" : "Tệp đính kèm");
}

export function PinsBar({ pins, canUnpin, busyId, onUnpin, onOpen, senderName }: {
  pins: PinnedMessage[]; canUnpin: boolean; busyId: string | null;
  onUnpin: (pin: PinnedMessage) => void; onOpen: (message: ChatMessage) => void;
  senderName: (message: ChatMessage) => string;
}) {
  const [expanded, setExpanded] = useState(false);
  const [menuId, setMenuId] = useState<string | null>(null);
  const root = useRef<HTMLDivElement>(null);
  const toggle = useRef<HTMLButtonElement>(null);
  const listId = useId();
  useEffect(() => {
    if (!pins.length) setExpanded(false);
    if (menuId && !pins.some(p => p.id === menuId)) { setMenuId(null); toggle.current?.focus(); }
  }, [pins, menuId]);
  useEffect(() => {
    if (!expanded) return;
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) { setExpanded(false); setMenuId(null); }
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || document.querySelector("dialog[open]")) return;
      setExpanded(false); setMenuId(null); toggle.current?.focus();
    };
    document.addEventListener("keydown", escape);
    document.addEventListener("pointerdown", outside);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [expanded]);
  if (!pins.length) return null;
  const latest = pins[0];
  const close = () => { setExpanded(false); setMenuId(null); };
  const text = (pin: PinnedMessage) => <span className="pins-bar__text"><span className="pins-bar__label">Tin nhắn</span>
    <span className="pins-bar__preview">{senderName(pin.message)}: {messagePreview(pin.message)}</span></span>;
  return <div className="pins-bar" ref={root} aria-label="Tin nhắn ghim" onKeyDown={event => {
    if (event.key === "Escape" && expanded) { event.stopPropagation(); close(); toggle.current?.focus(); }
  }}>
    <button type="button" className="pins-bar__summary" onClick={() => onOpen(latest.message)}><PinIcon size={17}/>{text(latest)}</button>
    {pins.length > 1 && <button type="button" className="pins-bar__count" aria-expanded={expanded} aria-controls={listId}
      onClick={() => { setExpanded(v => !v); setMenuId(null); }}>+{pins.length - 1} ghim<span className={expanded ? "pins-bar__chevron is-expanded" : "pins-bar__chevron"}><ChevronDownIcon size={14}/></span></button>}
    <button type="button" ref={toggle} className="pins-bar__icon" aria-label="Danh sách tin nhắn ghim" aria-expanded={expanded} aria-controls={listId}
      onClick={() => { setExpanded(v => !v); setMenuId(null); }}><MoreIcon size={17}/></button>
    {expanded && <section className="pins-bar__dropdown" id={listId} aria-label="Danh sách ghim">
      <header className="pins-bar__dropdown-header"><strong>Danh sách ghim ({pins.length})</strong>
        <button type="button" onClick={() => { close(); toggle.current?.focus(); }}>Thu gọn<ChevronDownIcon size={14}/></button></header>
      <div className="pins-bar__dropdown-list">{pins.map(pin => <div className="pins-bar__item" key={pin.id}>
        <button type="button" className="pins-bar__item-main" onClick={() => { close(); onOpen(pin.message); }}><PinIcon size={16}/>{text(pin)}</button>
        {canUnpin && <button type="button" className="pins-bar__icon" aria-label={`Tùy chọn ghim của ${senderName(pin.message)}`}
          aria-expanded={menuId === pin.id} aria-controls={`${listId}-${pin.id}`} disabled={busyId !== null}
          onClick={() => setMenuId(id => id === pin.id ? null : pin.id)}><MoreIcon size={16}/></button>}
        {canUnpin && menuId === pin.id && <div className="user-menu pins-bar__menu" id={`${listId}-${pin.id}`}>
          <button type="button" className="user-menu__item user-menu__item--danger" disabled={busyId !== null}
            onClick={() => onUnpin(pin)}>{busyId === pin.id ? "Đang bỏ ghim…" : "Bỏ ghim"}</button></div>}
      </div>)}</div>
    </section>}
  </div>;
}
