import { useEffect, useRef, useState, type FormEvent } from "react";
import { createPortal } from "react-dom";
import { friendsApi, type Friend } from "../api/friends";
import { conversationsApi } from "../api/conversations";
import { groupsApi } from "../api/groups";
import { extractErrorMessage } from "../api/auth";
import { getSessionVersion } from "../api/client";
import { refreshConversation } from "../realtime/connection";
import { useChatStore } from "../store/chatStore";
import { Avatar } from "./Avatar";
import { SearchIcon, CloseIcon } from "./icons";
import type { Conversation } from "../types";

export function CreateGroup({ onCreated, onClose }: { onCreated: (id: string) => void; onClose: () => void }) {
  const conversations = useChatStore(s => s.conversations);
  const [friends, setFriends] = useState<Friend[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [name, setName] = useState("");
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [busy, setBusy] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState("");
  const [created, setCreated] = useState<Conversation | null>(null);
  const key = useRef(crypto.randomUUID());
  const dialog = useRef<HTMLDialogElement>(null);
  const upload = useRef<HTMLInputElement>(null);
  const lock = useRef(false);
  const mounted = useRef(true);
  const session = useRef(getSessionVersion());
  const current = () => mounted.current && session.current === getSessionVersion();
  async function loadFriends() {
    setLoading(true); setLoadError("");
    try { const people = await friendsApi.list(); if (current()) setFriends(people); }
    catch (e) { if (current()) setLoadError(extractErrorMessage(e)); }
    finally { if (current()) setLoading(false); }
  }
  useEffect(() => {
    mounted.current = true;
    const previous = document.activeElement as HTMLElement | null;
    const element = dialog.current!; element.showModal();
    void loadFriends();
    return () => { mounted.current = false; element.close(); if (previous?.isConnected) previous.focus(); };
  }, []);
  useEffect(() => {
    if (!file) { setPreview(""); return; }
    const url = URL.createObjectURL(file); setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [file]);
  const recentIds = [...new Set([...conversations]
    .filter(c => c.type === "Direct" && c.peerUserId && !c.isBlocked && friends.some(f => f.id === c.peerUserId))
    .sort((a, b) => Date.parse(b.lastMessageAt) - Date.parse(a.lastMessageAt)).map(c => c.peerUserId!))].slice(0, 5);
  const q = query.trim().toLocaleLowerCase("vi");
  const recent = q ? [] : recentIds.map(id => friends.find(f => f.id === id)!);
  const remaining = friends.filter(f => q ? (f.fullName + " " + f.username).toLocaleLowerCase("vi").includes(q) : !recentIds.includes(f.id))
    .sort((a, b) => a.fullName.localeCompare(b.fullName, "vi"));
  const letters = new Map<string, Friend[]>();
  remaining.forEach(f => { const letter = f.fullName.trim().charAt(0).toLocaleUpperCase("vi") || "#"; letters.set(letter, [...(letters.get(letter) ?? []), f]); });
  function toggle(id: string) {
    if (busy || created) return;
    setSelected(ids => ids.includes(id) ? ids.filter(i => i !== id) : ids.length < 99 ? [...ids, id] : ids);
    key.current = crypto.randomUUID();
  }
  function finish(group: Conversation) { refreshConversation(group.id); onCreated(group.id); onClose(); }
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (lock.current || loading || selected.length < 2 || selected.length > 99) return;
    lock.current = true; setBusy(true); setError("");
    try {
      const autoName = selected.slice(0, 3).map(id => friends.find(f => f.id === id)?.fullName).filter(Boolean).join(", ");
      const finalName = name.trim() || autoName.slice(0, selected.length > 3 ? 97 : 100) + (selected.length > 3 ? "…" : "");
      const group = created ?? await conversationsApi.createGroup({ name: finalName, memberIds: selected }, key.current);
      if (!current()) return;
      setCreated(group);
      if (file) {
        const intent = await groupsApi.avatarIntent(group.id);
        if (!current()) return;
        const body = new FormData(); Object.entries(intent.fields).forEach(([k, v]) => body.append(k, v)); body.append("file", file);
        const response = await fetch(intent.uploadUrl, { method: "POST", body });
        if (!response.ok) throw new Error("Nhóm đã tạo nhưng tải ảnh thất bại. Thử lại ảnh hoặc vào nhóm ngay.");
        if (!current()) return;
        await groupsApi.setAvatar(group.id, intent.assetId, group.version ?? 0);
      }
      if (current()) finish(group);
    } catch (e) { if (current()) setError(extractErrorMessage(e, "Không thể hoàn tất. Vui lòng thử lại.")); }
    finally { lock.current = false; if (current()) setBusy(false); }
  }
  function row(person: Friend) {
    return <label className="create-group-contact" key={person.id}>
      <input type="checkbox" aria-label={person.fullName + " @" + person.username} checked={selected.includes(person.id)} disabled={busy || !!created || (selected.length >= 99 && !selected.includes(person.id))} onChange={() => toggle(person.id)} />
      <Avatar src={person.avatarUrl} name={person.fullName} size={36} /><span>{person.fullName}<small>@{person.username}</small></span>
    </label>;
  }
  return createPortal(<dialog ref={dialog} className="create-group-modal" aria-labelledby="create-group-title"
    onCancel={e => { e.preventDefault(); if (!busy) onClose(); }}
    onClick={e => { if (e.target === e.currentTarget && !busy) { const r = e.currentTarget.getBoundingClientRect(); if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) onClose(); } }}>
    <form onSubmit={submit} aria-busy={busy}>
      <header><h2 id="create-group-title">Tạo nhóm</h2><button type="button" className="icon-btn" aria-label="Đóng" disabled={busy} onClick={onClose}><CloseIcon /></button></header>
      <div className="create-group-body">
        <div className="create-group-identity">
          <button type="button" className="create-group-avatar" aria-label="Chọn ảnh nhóm" disabled={busy || !!created} onClick={() => upload.current?.click()}>{preview ? <img src={preview} alt="Ảnh nhóm đã chọn" /> : <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M3 7h4l2-3h6l2 3h4v13H3Z" /><circle cx="12" cy="13" r="4" /></svg>}</button>
          <input ref={upload} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={e => {
            const next = e.target.files?.[0]; e.target.value = ""; if (!next) return;
            if (next.size > 5 * 1024 * 1024 || !["image/jpeg", "image/png", "image/webp"].includes(next.type)) { setError("Chọn ảnh JPEG, PNG hoặc WebP tối đa 5 MB."); return; }
            setError(""); setFile(next);
          }} />
          <input aria-label="Tên nhóm" placeholder="Nhập tên nhóm…" maxLength={100} disabled={busy || !!created} value={name} onChange={e => { setName(e.target.value); key.current = crypto.randomUUID(); }} />
        </div>
        <p className="create-group-hint">Bỏ trống tên để dùng tên thành viên. Ảnh nhóm là ảnh công khai.</p>
        {file && !created && <button type="button" className="create-group-remove" disabled={busy} onClick={() => setFile(null)}>Bỏ ảnh đã chọn</button>}
        <label className="create-group-search"><SearchIcon /><input aria-label="Tìm bạn bè" placeholder="Nhập tên hoặc username bạn bè…" value={query} onChange={e => setQuery(e.target.value)} /></label>
        {selected.length > 0 && <div className="create-group-chips">{selected.map(id => <span key={id}>{friends.find(f => f.id === id)?.fullName}<button type="button" disabled={busy || !!created} aria-label={"Bỏ chọn " + friends.find(f => f.id === id)?.fullName} onClick={() => toggle(id)}><CloseIcon /></button></span>)}</div>}
        <div className="create-group-list">
          {loading ? <p role="status">Đang tải danh sách bạn bè…</p> : loadError ? <div className="group-load-error"><p role="alert">{loadError}</p><button type="button" onClick={() => void loadFriends()}>Thử tải lại</button></div> : <>
            {recent.length > 0 && <><h3>Trò chuyện gần đây</h3>{recent.map(row)}</>}
            {[...letters].map(([letter, people]) => <section key={letter}><h3>{letter}</h3>{people.map(row)}</section>)}
            {!recent.length && !remaining.length && <p>{q ? "Không tìm thấy bạn bè phù hợp." : "Chưa có bạn bè để thêm vào nhóm."}</p>}
          </>}
        </div>
      </div>
      {error && <p className="alert alert-danger" role="alert">{error}</p>}
      {created && <p className="create-group-hint" role="status">Nhóm đã được tạo. Thử lại chỉ cập nhật ảnh, không tạo nhóm mới.</p>}
      <footer><span>{selected.length ? "Đã chọn " + selected.length + "/99 người" : "Chọn tối thiểu 2 người"}</span><div>
        <button type="button" disabled={busy} onClick={() => created ? finish(created) : onClose()}>{created ? "Vào nhóm" : "Hủy"}</button>
        <button className="btn-primary" disabled={busy || loading || selected.length < 2}>{busy ? "Đang xử lý…" : created ? "Thử lại ảnh" : "Tạo nhóm"}</button>
      </div></footer>
    </form>
  </dialog>, document.body);
}

