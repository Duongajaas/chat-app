import { useEffect, useRef, useState } from "react";
import { groupsApi, type GroupInvite, type GroupMember } from "../api/groups";
import { friendsApi, type Friend } from "../api/friends";
import { extractErrorMessage } from "../api/auth";
import { refreshConversation } from "../realtime/connection";
import { getSessionVersion } from "../api/client";
import type { Conversation } from "../types";
import "../index.css";

export function GroupSettings({ conversation, onClose }: { conversation: Conversation; onClose: () => void }) {
  const [members, setMembers] = useState<GroupMember[]>([]);
  const [friends, setFriends] = useState<Friend[]>([]);
  const [invites, setInvites] = useState<GroupInvite[]>([]);
  const [name, setName] = useState(conversation.name);
  const [target, setTarget] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [link, setLink] = useState("");
  const [maxUses, setMaxUses] = useState(100);
  const inviteKey = useRef<string | null>(null);
  const mounted = useRef(true);
  const session = useRef(getSessionVersion());
  const current = () => mounted.current && session.current === getSessionVersion();
  const can = (permission: string) => conversation.permissions?.includes(permission) ?? false;
  const inactive = conversation.hasLeft || !!conversation.closedAt;
  const id = conversation.id;
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
    void load().catch(e => { if (current()) setError(extractErrorMessage(e)); });
    return () => { mounted.current = false; };
  }, [id, conversation.version, inactive]);

  async function run(action: () => Promise<unknown>) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      await action();
      if (!current()) return;
      refreshConversation(id);
      await load();
    } catch (e) {
      if (current()) { setError(extractErrorMessage(e)); refreshConversation(id); }
    } finally { if (current()) setBusy(false); }
  }

  return <section className="group-settings" aria-label="Thông tin nhóm">
    <header><h2>Thông tin nhóm</h2><button onClick={onClose}>Đóng</button></header>
    <p>Bạn chỉ xem được tin nhắn trong những khoảng thời gian là thành viên.</p>
    {error && <p role="alert">{error}</p>}
    {inactive ? <p>Nhóm đã đóng hoặc bạn đã rời nhóm. Lịch sử được phép xem vẫn được giữ.</p> : <>
      <p>Vai trò: {conversation.role} · {conversation.memberCount} thành viên</p>
      {can("RenameGroup") && <form onSubmit={e => { e.preventDefault(); void run(() => groupsApi.rename(id, name)); }}>
        <label>Tên nhóm<input value={name} onChange={e => setName(e.target.value)} maxLength={100} required /></label>
        <button disabled={busy || !name.trim()}>Lưu tên</button>
      </form>}
      <h3>Thành viên</h3>
      <ul>{members.map(m => <li key={m.userId}>
        <span>{m.fullName} · {m.role}</span>
        {m.canChangeRole && <button disabled={busy} onClick={() => void run(() => groupsApi.role(id, m.userId, m.role === "Admin" ? "Member" : "Admin"))}>
          {m.role === "Admin" ? "Hạ Admin" : "Cấp Admin"}
        </button>}
        {m.canKick && <button disabled={busy} onClick={() => {
          if (window.confirm(`Đưa ${m.fullName} ra khỏi nhóm? Chỉ Owner/Admin có thể thêm lại.`)) void run(() => groupsApi.kick(id, m.userId));
        }}>Mời ra khỏi nhóm</button>}
      </li>)}</ul>
      {can("AddMember") && <form onSubmit={e => { e.preventDefault(); void run(() => groupsApi.add(id, target)); }}>
        <label>Thêm bạn bè<select value={target} onChange={e => setTarget(e.target.value)} required>
          <option value="">Chọn bạn</option>
          {friends.filter(f => !members.some(m => m.userId === f.id)).map(f => <option key={f.id} value={f.id}>{f.fullName}</option>)}
        </select></label><button disabled={busy || !target}>Thêm vào nhóm</button>
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
        <ul>{invites.map(i => <li key={i.id}>
          <span>{i.usedCount}/{i.maxUses} lượt · Hết hạn {new Date(i.expiresAt).toLocaleString("vi-VN")}
            {i.revokedAt ? " · Đã thu hồi" : ""}</span>
          {!i.revokedAt && <button disabled={busy} onClick={() => void run(() => groupsApi.revoke(id, i.id))}>Thu hồi</button>}
        </li>)}</ul>
      </>}
      <button disabled={busy} onClick={() => {
        const successor = members.filter(m => m.role !== "Owner").sort((a, b) =>
          (a.role === "Admin" ? 0 : 1) - (b.role === "Admin" ? 0 : 1) || a.joinedAt.localeCompare(b.joinedAt))[0];
        const detail = conversation.role === "Owner"
          ? successor ? ` Owner dự kiến tiếp theo: ${successor.fullName}.` : " Nhóm sẽ đóng và thu hồi mọi link." : "";
        if (window.confirm("Rời nhóm? Bạn sẽ không đọc được tin gửi sau khi rời." + detail))
          void run(async () => { await groupsApi.leave(id); if (current()) { refreshConversation(id); onClose(); } });
      }}>Rời nhóm</button>
    </>}
  </section>;
}
