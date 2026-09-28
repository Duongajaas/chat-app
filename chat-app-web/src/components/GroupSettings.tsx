import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { groupsApi, type GroupInvite, type GroupMember } from "../api/groups";
import { friendsApi, type Friend } from "../api/friends";
import { extractErrorMessage } from "../api/auth";
import { refreshConversation } from "../realtime/connection";
import { getSessionVersion } from "../api/client";
import type { Conversation } from "../types";
import "../index.css";
import { Avatar } from "./Avatar";
import { ConfirmDialog } from "./ConfirmDialog";
import { ShieldIcon, CloseIcon } from "./icons";

export function GroupSettings({ conversation, onClose }: { conversation: Conversation; onClose: () => void }) {
  const [members, setMembers] = useState<GroupMember[]>([]);
  const [friends, setFriends] = useState<Friend[]>([]);
  const [invites, setInvites] = useState<GroupInvite[]>([]);
  const [name, setName] = useState(conversation.name);
  const [target, setTarget] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [confirmation, setConfirmation] = useState<{ title: string; description: string; action: () => Promise<unknown> } | null>(null);
  const [memberQuery, setMemberQuery] = useState("");
  const [link, setLink] = useState("");
  const [maxUses, setMaxUses] = useState(100);
  const inviteKey = useRef<string | null>(null);
  const mounted = useRef(true);
  const session = useRef(getSessionVersion());
  const panel = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const element = panel.current!;
    element.showModal();
    return () => { element.close(); if (previous?.isConnected) previous.focus(); };
  }, []);
  const current = () => mounted.current && session.current === getSessionVersion();
  const can = (permission: string) => conversation.permissions?.includes(permission) ?? false;
  const inactive = conversation.hasLeft || !!conversation.closedAt;
  const id = conversation.id;
  const roleName = (role?: string | null) => role === "Owner" ? "Trưởng nhóm" : role === "Admin" ? "Quản trị viên" : "Thành viên";
  const availableFriends = friends.filter(f => !members.some(m => m.userId === f.id));
  const visibleMembers = members.filter(m => `${m.fullName} ${m.username}`.toLocaleLowerCase("vi").includes(memberQuery.trim().toLocaleLowerCase("vi")));
  useEffect(() => () => { mounted.current = false; }, []);

  async function load() {
    if (inactive) return;
    const [people, available, links] = await Promise.all([
      groupsApi.members(id), friendsApi.list(), can("ManageInvites") ? groupsApi.invites(id) : Promise.resolve([]),
    ]);
    if (current()) { setMembers(people); setFriends(available); setInvites(links); }
  }
  useEffect(() => {
    mounted.current = true;
    void load().catch(e => { if (current()) setError(extractErrorMessage(e)); }).finally(() => { if (current()) setLoading(false); });
    return () => { mounted.current = false; };
  }, [id, conversation.version, inactive]);

  async function run(action: () => Promise<unknown>) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      await action();
      if (!current()) return;
      setConfirmation(null);
      refreshConversation(id);
      await load();
    } catch (e) {
      if (current()) { setError(extractErrorMessage(e)); refreshConversation(id); }
    } finally { if (current()) setBusy(false); }
  }

  return createPortal(<dialog ref={panel} className="group-settings group-settings--details" aria-label="Thông tin nhóm" aria-busy={busy || loading} onCancel={e => { e.preventDefault(); if (!busy) onClose(); }}>
    {confirmation && <ConfirmDialog title={confirmation.title} description={confirmation.description} confirmLabel="Xác nhận" busy={busy} error={error} onCancel={() => { setConfirmation(null); setError(""); }} onConfirm={() => void run(confirmation.action)} />}
    <header><h2>Thông tin nhóm</h2><button aria-label="Đóng thông tin nhóm" disabled={busy} onClick={onClose}><CloseIcon /></button></header>
    <div className="group-settings__identity"><Avatar src={conversation.avatarUrl} name={conversation.name} size={72} color="var(--color-surface-alt)" /><div><strong>{conversation.name}</strong><p>{conversation.memberCount} thành viên</p></div></div>
    <p>Bạn chỉ xem được tin nhắn trong những khoảng thời gian là thành viên.</p>
    {error && !confirmation && <p className="alert alert-danger" role="alert">{error}</p>}
    {loading && <p role="status">Đang tải thông tin nhóm…</p>}
    {inactive ? <p>Nhóm đã đóng hoặc bạn đã rời nhóm. Lịch sử được phép xem vẫn được giữ.</p> : <>
      <p className="group-settings__role">Vai trò của bạn: <strong>{roleName(conversation.role)}</strong></p><h3>Cài đặt nhóm</h3>
      {can("ChangeAvatar") && <label>Đổi avatar nhóm (ảnh công khai, tối đa 5 MB)
        <input type="file" accept="image/jpeg,image/png,image/webp" disabled={busy} onChange={e => {
          const file = e.target.files?.[0]; e.target.value = "";
          if (!file) return;
          if (file.size > 5 * 1024 * 1024 || !["image/jpeg", "image/png", "image/webp"].includes(file.type)) {
            setError("Chọn JPEG, PNG hoặc WebP tối đa 5 MB."); return;
          }
          void run(async () => {
            const intent = await groupsApi.avatarIntent(id);
            const form = new FormData();
            Object.entries(intent.fields).forEach(([key, value]) => form.append(key, value));
            form.append("file", file);
            const response = await fetch(intent.uploadUrl, { method: "POST", body: form });
            if (!response.ok) throw new Error("Upload avatar thất bại.");
            // Do not forward untrusted provider URLs/metadata; server inspects its own asset.
            if (current()) await groupsApi.setAvatar(id, intent.assetId, conversation.version ?? 0);
          });
        }} />
      </label>}
      {can("RenameGroup") && <form onSubmit={e => { e.preventDefault(); void run(() => groupsApi.rename(id, name)); }}>
        <label>Tên nhóm<input disabled={busy} value={name} onChange={e => setName(e.target.value)} maxLength={100} required /></label>
        <button disabled={busy || !name.trim() || name.trim() === conversation.name}>Lưu tên</button>
      </form>}
      <h3>Thành viên <span className="group-settings__count">{members.length}</span></h3>
      <label className="group-settings__search">Tìm thành viên<input type="search" placeholder="Tên hoặc username…" value={memberQuery} onChange={e => setMemberQuery(e.target.value)} /></label>
      {!loading && !visibleMembers.length && <p role="status">{memberQuery ? "Không tìm thấy thành viên phù hợp." : "Chưa tải được danh sách thành viên."}</p>}
      <ul className="group-settings__members">{visibleMembers.map(m => <li key={m.userId}>
        <div className="group-settings__member"><Avatar src={m.avatarUrl} name={m.fullName} size={36} color="var(--color-surface-alt)" /><span><strong>{m.fullName}</strong><small>@{m.username} · {roleName(m.role)}</small></span>{m.role !== "Member" && <ShieldIcon />}</div>
        {m.canChangeRole && <button disabled={busy} onClick={() => void run(() => groupsApi.role(id, m.userId, m.role === "Admin" ? "Member" : "Admin"))}>
          {m.role === "Admin" ? "Hạ Admin" : "Cấp Admin"}
        </button>}
        {m.canKick && <button disabled={busy} onClick={() => {
          setConfirmation({ title: `Đưa ${m.fullName} ra khỏi nhóm?`, description: "Chỉ Owner hoặc Admin có thể thêm lại người này.", action: () => groupsApi.kick(id, m.userId) });
        }}>Mời ra khỏi nhóm</button>}
      </li>)}</ul>
      {can("AddMember") && <form onSubmit={e => { e.preventDefault(); void run(() => groupsApi.add(id, target)); }}>
        <label>Thêm bạn bè<select disabled={busy || loading || !availableFriends.length} value={target} onChange={e => setTarget(e.target.value)} required>
          <option value="">Chọn bạn</option>
          {availableFriends.map(f => <option key={f.id} value={f.id}>{f.fullName}</option>)}
        </select></label><button disabled={busy || loading || !availableFriends.some(f => f.id === target)}>Thêm vào nhóm</button>
        {!loading && !availableFriends.length && <p>Tất cả bạn bè hiện có đã ở trong nhóm.</p>}
      </form>}
      {can("ManageInvites") && <>
        <h3>Link mời</h3>
        <p>Link có hiệu lực 7 ngày. Chỉ hiển thị link một lần khi tạo; người bị kick cần quản trị viên thêm lại.</p>
        <label>Số lượt dùng<input type="number" min={1} max={10000} value={maxUses} disabled={busy || !!inviteKey.current}
          onChange={e => setMaxUses(Number(e.target.value))} /></label>
        <button disabled={busy || maxUses < 1 || maxUses > 10000} onClick={() => void run(async () => {
          inviteKey.current ??= crypto.randomUUID();
          const result = await groupsApi.createInvite(id, inviteKey.current, maxUses);
          if (!current()) return;
          inviteKey.current = null;
          setLink(result.code ? `${window.location.origin}/join#code=${result.code}` : "");
          if (!result.code) setError("Link đã tạo ở lần yêu cầu trước. Không thể hiển thị lại; hãy thu hồi link đó và tạo link mới.");
        })}>Tạo link mời</button>
        {link && <label>Sao chép và lưu link ngay<input readOnly value={link} onFocus={e => e.target.select()} /></label>}
        {!loading && !invites.length && <p>Chưa có link mời nào.</p>}
        <ul>{invites.map(i => <li key={i.id}>
          <span>{i.usedCount}/{i.maxUses} lượt · Hết hạn {new Date(i.expiresAt).toLocaleString("vi-VN")}
            {i.revokedAt ? " · Đã thu hồi" : ""}</span>
          {!i.revokedAt && <button disabled={busy} onClick={() => void run(() => groupsApi.revoke(id, i.id))}>Thu hồi</button>}
        </li>)}</ul>
      </>}
      <button className="group-settings__leave" disabled={busy || loading} onClick={() => {
        const successor = members.filter(m => m.role !== "Owner").sort((a, b) =>
          (a.role === "Admin" ? 0 : 1) - (b.role === "Admin" ? 0 : 1) || a.joinedAt.localeCompare(b.joinedAt))[0];
        const detail = conversation.role === "Owner"
          ? successor ? ` Owner dự kiến tiếp theo: ${successor.fullName}.` : " Nhóm sẽ đóng và thu hồi mọi link." : "";
        setConfirmation({ title: "Rời nhóm?", description: "Bạn sẽ không đọc được tin gửi sau khi rời." + detail,
          action: async () => { await groupsApi.leave(id); if (current()) { refreshConversation(id); onClose(); } } });
      }}>Rời nhóm</button>
    </>}
  </dialog>, document.body);
}

