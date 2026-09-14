import { useEffect, useRef, useState } from "react";
import { friendsApi, type Friend } from "../api/friends";
import { conversationsApi } from "../api/conversations";
import { extractErrorMessage } from "../api/auth";
import { getSessionVersion } from "../api/client";
import { refreshConversation } from "../realtime/connection";

export function CreateGroup({ onCreated, onClose }: { onCreated: (id: string) => void; onClose: () => void }) {
  const [friends, setFriends] = useState<Friend[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [name, setName] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const key = useRef(crypto.randomUUID());
  useEffect(() => {
    let current = true;
    void friendsApi.list().then(f => { if (current) setFriends(f); }).catch(e => { if (current) setError(extractErrorMessage(e)); });
    return () => { current = false; };
  }, []);
  return <form className="group-settings" onSubmit={async e => {
    e.preventDefault(); setBusy(true); setError("");
    const session = getSessionVersion();
    try {
      const group = await conversationsApi.createGroup({ name, memberIds: selected }, key.current);
      if (session !== getSessionVersion()) return;
      refreshConversation(group.id); onCreated(group.id); onClose();
    } catch (e) { if (session === getSessionVersion()) setError(extractErrorMessage(e)); }
    finally { if (session === getSessionVersion()) setBusy(false); }
  }}>
    <h3>Tạo nhóm</h3>
    <label>Tên nhóm<input required maxLength={100} value={name} disabled={busy} onChange={e => { setName(e.target.value); key.current = crypto.randomUUID(); }} /></label>
    <p>Chọn 1–99 bạn bè</p>
    {friends.map(f => <label key={f.id}>
      <span><input type="checkbox" checked={selected.includes(f.id)} disabled={busy}
        onChange={e => { setSelected(ids => e.target.checked ? [...ids, f.id] : ids.filter(id => id !== f.id)); key.current = crypto.randomUUID(); }} /> {f.fullName}</span>
    </label>)}
    {error && <p role="alert">{error}</p>}
    <button disabled={busy || !name.trim() || selected.length < 1 || selected.length > 99}>Tạo nhóm</button>
    <button type="button" disabled={busy} onClick={onClose}>Hủy</button>
  </form>;
}
