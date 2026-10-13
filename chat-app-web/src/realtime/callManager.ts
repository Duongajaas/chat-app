import { apiClient, getSessionVersion, onSessionCleared } from "../api/client";
import { invokeCall } from "./connection";
import { useCallStore, type CallSnapshot } from "../store/callStore";

type Endpoint = { endpointId: string; credential: string; generation: number };
type Signal = { callId: string; epoch: number; signalId: string; type: "offer" | "answer" | "ice"; payload: string };
let endpoint: Endpoint | null = null;
let pc: RTCPeerConnection | null = null;
let operation = 0;
let epoch = 0;
let offeredEpoch = 0;
let candidates: RTCIceCandidateInit[] = [];
let early: Signal[] = [];
let seen = new Set<string>();
let chain = Promise.resolve();
let syncing = false;
let rebindPending = false;
let ticking = false;
let stopping = false;
let lastHeartbeat = 0;
let leaseDeadline = 0;
let iceExpires = 0;
let disconnectTimer: ReturnType<typeof setTimeout> | undefined;
let ending: string | null = null;
let dismissed: string | null = null;
let cancelPending = false;
let pendingStart: { conversationId: string; type: "Audio" | "Video"; clientCallId: string; endpoint: Endpoint } | null = null;
const state = () => useCallStore.getState();
const set = useCallStore.setState;
const alive = (c: CallSnapshot) => c.status === "Ringing" || c.status === "Accepted";
const proof = () => { if (!endpoint) throw new Error("Thiết bị không còn cuộc gọi."); return { ...endpoint }; };
const freshEndpoint = (): Endpoint => ({ endpointId: crypto.randomUUID(), generation: 1,
  credential: Array.from(crypto.getRandomValues(new Uint8Array(32)), b => b.toString(16).padStart(2, "0")).join("") });
const message = (error: unknown) => error instanceof Error ? error.message.replace(/^An unexpected error occurred invoking '.*?' on the server\. HubException: /, "") : "Không thể thực hiện cuộc gọi.";
function releaseMedia() {
  clearTimeout(disconnectTimer);
  const old = pc; pc = null;
  if (old) { old.ontrack = null; old.onicecandidate = null; old.onconnectionstatechange = null; old.oniceconnectionstatechange = null; old.close(); }
  state().local?.getTracks().forEach(t => t.stop());
  candidates = []; early = []; seen = new Set(); chain = Promise.resolve(); epoch = 0; offeredEpoch = 0;
  set({ local: null, remote: null, connected: false, muted: false, cameraOff: false });
}
export function resetCall() {
  operation++; cancelPending = false; rebindPending = false; releaseMedia(); endpoint = null; pendingStart = null; ending = null; leaseDeadline = 0;
  set({ call: null, preparing: false, busy: false, error: "" });
}
onSessionCleared(() => { dismissed = null; resetCall(); });
async function media(type: "Audio" | "Video") {
  try { return await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true }, video: type === "Video" }); }
  catch (error) {
    // console.log("Media error:", error);
    if (type === "Video" && error instanceof DOMException && error.name === "NotFoundError") {
      set({ error: "Không tìm thấy camera, tham gia bằng âm thanh." });
      return navigator.mediaDevices.getUserMedia({ audio: true });
    }
    throw new Error("Không mở được micro/camera. Hãy kiểm tra quyền thiết bị và HTTPS.");
  }
}
async function command(action: string): Promise<CallSnapshot> {
  const call = state().call;
  if (!call) throw new Error("Cuộc gọi không khả dụng.");
  const session = getSessionVersion(); const op = operation;
  const result = await invokeCall<CallSnapshot>("UpdateCall", call.id, { action, endpoint: endpoint && proof(), epoch: call.epoch });
  if (session !== getSessionVersion() || op !== operation) throw new Error("Phiên cuộc gọi đã thay đổi.");
  return result;
}
async function apply(call: CallSnapshot) {
  if (!call || typeof call.id !== "string" || typeof call.status !== "string") return;
  if (dismissed === call.id && alive(call) && call.hasEndpoint && !call.ownsEndpoint) return;
  const current = state().call;
  if (current && current.id !== call.id && alive(current)) return;
  if (current?.id === call.id && call.version < current.version) return;
  if (call.ownsEndpoint && endpoint) endpoint.generation = call.generation;
  if (call.leaseExpiresAt) leaseDeadline = Date.now() + Math.max(0, Date.parse(call.leaseExpiresAt) - Date.parse(call.serverTime));
  set({ call });
  if (!alive(call)) { operation++; cancelPending = false; releaseMedia(); endpoint = null; ending = null; pendingStart = null; set({ preparing: false, busy: false }); return; }
  if (call.hasEndpoint && !call.ownsEndpoint) { operation++; cancelPending = false; releaseMedia(); endpoint = null; set({ preparing: false, busy: false }); return; }
  if (call.status === "Accepted" && call.ownsEndpoint && state().local && !ending) {
    await ensurePeer(call);
    if (call.isCaller && offeredEpoch < call.epoch) await offer(call);
  }
}
export async function startCall(conversationId: string, type: "Audio" | "Video") {
  if (state().preparing || (state().call && alive(state().call!))) return;
  resetCall(); const op = operation; const session = getSessionVersion();
  endpoint = freshEndpoint(); set({ preparing: true, busy: true });
  try {
    const stream = await media(type);
    if (op !== operation || session !== getSessionVersion()) { stream.getTracks().forEach(t => t.stop()); return; }
    set({ local: stream });
    pendingStart = { conversationId, type, clientCallId: crypto.randomUUID(), endpoint: proof() };
    const call = await invokeCall<CallSnapshot>("InitiateCall", pendingStart);
    if (session !== getSessionVersion()) return;
    if (op !== operation) return;
    pendingStart = null; await apply(call); if (cancelPending) await endCall();
  } catch (error) {
    if (op !== operation || session !== getSessionVersion()) return;
    if (pendingStart) {
      // Keep the original ID until the server outcome is known. Never create a second call on timeout.
      set({ error: "Đang kiểm tra trạng thái cuộc gọi…" });
      await reconcileStart();
    } else { releaseMedia(); endpoint = null; set({ error: message(error) }); }
  } finally { if (op === operation) set({ preparing: !!pendingStart, busy: false }); }
}
async function reconcileStart() {
  const request = pendingStart; if (!request) return;
  const session = getSessionVersion(); const op = operation;
  try {
    let { data } = await apiClient.get<CallSnapshot | null>(`/calls/by-client-id/${request.clientCallId}`, { params: { endpointId: request.endpoint.endpointId } });
    if (op !== operation || session !== getSessionVersion() || pendingStart !== request) return;
    if (!data) data = await invokeCall<CallSnapshot>("InitiateCall", request);
    if (op !== operation || session !== getSessionVersion()) return;
    pendingStart = null; set({ preparing: false, error: "" }); await apply(data); if (cancelPending) await endCall();
  } catch (error) {
    if (op !== operation) return;
    // A received server rejection is definitive; transport failures keep the original request pending.
    if ((error instanceof Error && error.message.includes("HubException")) || (error as { response?: { status?: number } }).response?.status === 403) {
      pendingStart = null; releaseMedia(); endpoint = null; set({ preparing: false, error: message(error) });
    }
  }
}
export async function acceptCall() {
  if (state().busy || !state().call || state().call!.status !== "Ringing") return;
  const callId = state().call!.id; const op = ++operation; const session = getSessionVersion();
  endpoint = freshEndpoint(); set({ busy: true, error: "" });
  try {
    const stream = await media(state().call!.type);
    if (op !== operation || session !== getSessionVersion() || state().call?.id !== callId || state().call?.status !== "Ringing") { stream.getTracks().forEach(t => t.stop()); return; }
    set({ local: stream });
    const call = await command("accept");
    if (op !== operation || session !== getSessionVersion()) return;
    await apply(call);
  } catch (error) {
    if (op === operation) {
      set({ error: message(error) });
      await syncCall();
      if (!state().call?.ownsEndpoint) { releaseMedia(); endpoint = null; }
    }
  } finally { if (op === operation) set({ busy: false }); }
}
export async function rejectCall() {
  const session = getSessionVersion(); const op = operation;
  if (state().busy) return; set({ busy: true, error: "" });
  try { await apply(await command("reject")); }
  catch (error) { if (session === getSessionVersion() && op === operation) set({ error: message(error) }); }
  finally { if (session === getSessionVersion() && op === operation) set({ busy: false }); }
}
export async function endCall() {
  const call = state().call;
  if (!call || !endpoint) { if (!pendingStart) resetCall(); return; }
  ending = call.id; releaseMedia(); set({ busy: true, error: "Đang kết thúc cuộc gọi…" });
  await flushEnd();
}
async function flushEnd() {
  if (!ending || !endpoint) return;
  const id = ending; const session = getSessionVersion();
  try {
    const { data } = await apiClient.post<CallSnapshot>(`/calls/${id}/end`, proof());
    if (session === getSessionVersion() && ending === id) { set({ error: "" }); await apply(data); }
  } catch { /* retry independently until the lease or authenticated snapshot ends the call */ }
}
export function toggleMic() { const muted = !state().muted; state().local?.getAudioTracks().forEach(t => { t.enabled = !muted; }); set({ muted }); }
export function toggleCamera() { const cameraOff = !state().cameraOff; state().local?.getVideoTracks().forEach(t => { t.enabled = !cameraOff; }); set({ cameraOff }); }
async function send(type: Signal["type"], payload: unknown, call: CallSnapshot, targetEpoch: number) {
  await invokeCall("SendSignal", call.id, { epoch: targetEpoch, signalId: crypto.randomUUID(), type, payload: JSON.stringify(payload), endpoint: proof() });
}
let creating: Promise<void> | null = null;
async function ensurePeer(call: CallSnapshot) {
  if (pc) return;
  if (creating) return creating;
  const op = operation;
  const task = (async () => {
    const { data } = await apiClient.post<{ iceServers: RTCIceServer[]; expiresAt: string; relayAvailable: boolean }>(`/calls/${call.id}/ice-servers`, proof());
    if (op !== operation || state().call?.id !== call.id || !state().local) return;
    iceExpires = Date.parse(data.expiresAt);
    if (!data.relayAvailable) set({ error: "Nếu không kết nối được cuộc gọi, hãy thử mạng khác." });
    const peer = new RTCPeerConnection({ iceServers: data.iceServers }); pc = peer; epoch = call.epoch;
    state().local!.getTracks().forEach(t => peer.addTrack(t, state().local!));
    peer.ontrack = event => {
      if (pc !== peer) return;
      const remote = event.streams[0] ?? state().remote ?? new MediaStream();
      if (!remote.getTracks().includes(event.track)) remote.addTrack(event.track);
      set({ remote });
    };
    peer.onicecandidate = event => {
      if (pc !== peer || !event.candidate) return;
      // Ignore candidates from a superseded ICE generation.
      const ufrag = event.candidate.usernameFragment;
      if (ufrag && !peer.localDescription?.sdp.includes(`a=ice-ufrag:${ufrag}`)) return;
      void send("ice", event.candidate.toJSON(), call, epoch).catch(() => scheduleRestart(peer));
    };
    peer.oniceconnectionstatechange = () => {
      if (pc === peer && ["connected", "completed"].includes(peer.iceConnectionState)) void command("connected").then(apply).catch(() => undefined);
    };
    peer.onconnectionstatechange = () => {
      if (pc !== peer) return;
      const connected = peer.connectionState === "connected"; set({ connected });
      if (connected) { clearTimeout(disconnectTimer); void command("connected").then(apply).catch(() => undefined); }
      else if (["disconnected", "failed"].includes(peer.connectionState)) scheduleRestart(peer);
    };
    const queued = early; early = []; queued.forEach(receiveSignal);
  })().finally(() => { if (creating === task) creating = null; });
  creating = task; return task;
}
function scheduleRestart(peer: RTCPeerConnection) {
  clearTimeout(disconnectTimer);
  disconnectTimer = setTimeout(() => { if (pc === peer && !ending) void restart(); }, 3000);
}
async function restart() {
  if (!state().call || !endpoint) return;
  try { await apply(await command("restart")); }
  catch { /* snapshot polling and server recovery deadline remain active */ }
}
async function offer(call: CallSnapshot) {
  const peer = pc; if (!peer || offeredEpoch >= call.epoch) return;
  offeredEpoch = call.epoch; epoch = call.epoch; candidates = []; seen.clear();
  try {
    if (peer.signalingState !== "stable") await peer.setLocalDescription({ type: "rollback" });
    const description = await peer.createOffer({ iceRestart: call.epoch > 1 });
    if (pc !== peer || epoch !== call.epoch) return;
    await peer.setLocalDescription(description); await send("offer", peer.localDescription, call, call.epoch);
  } catch { if (pc === peer) scheduleRestart(peer); }
}
export function receiveSignal(signal: Signal) {
  if (!state().call || signal.callId !== state().call!.id || !endpoint || ending) return;
  if (!pc || signal.epoch > state().call!.epoch) { if (early.length < 256) early.push(signal); void syncCall(); return; }
  const peer = pc; const op = operation;
  chain = chain.then(async () => {
    const call = state().call;
    if (!call || pc !== peer || op !== operation || signal.epoch !== call.epoch || seen.has(signal.signalId)) return;
    if (seen.size > 512) throw new Error("Quá nhiều tín hiệu."); seen.add(signal.signalId);
    if (epoch !== signal.epoch) { epoch = signal.epoch; candidates = []; }
    const payload = JSON.parse(signal.payload);
    if (signal.type === "ice") {
      if (peer.remoteDescription && peer.remoteDescription.sdp && epoch === signal.epoch && !candidates.length) {
        const ufrag = payload.usernameFragment;
        if (!ufrag || peer.remoteDescription.sdp.includes(`a=ice-ufrag:${ufrag}`)) await peer.addIceCandidate(payload);
        else if (candidates.length < 256) candidates.push(payload);
      } else if (candidates.length < 256) candidates.push(payload);
      return;
    }
    if (signal.type === "offer" && call.isCaller || signal.type === "answer" && !call.isCaller) return;
    await peer.setRemoteDescription(payload);
    for (const candidate of candidates.splice(0)) {
      if (!candidate.usernameFragment || peer.remoteDescription?.sdp.includes(`a=ice-ufrag:${candidate.usernameFragment}`)) await peer.addIceCandidate(candidate);
    }
    if (signal.type === "offer") { await peer.setLocalDescription(await peer.createAnswer()); await send("answer", peer.localDescription, call, signal.epoch); }
  }).catch(() => { if (pc === peer) scheduleRestart(peer); });
}
export async function syncCall(rebind = false) {
  rebindPending ||= rebind;
  if (syncing) return; syncing = true;
  const shouldResume = rebindPending; rebindPending = false;
  const session = getSessionVersion(); const op = operation;
  try {
    if (pendingStart) { await reconcileStart(); return; }
    const current = state().call && alive(state().call!) ? state().call : null;
    const { data } = await apiClient.get<CallSnapshot | null>(current ? `/calls/${current.id}` : "/calls/active", { params: { endpointId: endpoint?.endpointId } });
    if (session !== getSessionVersion() || op !== operation) return;
    if (data && typeof data.id === "string") {
      if (shouldResume && data.ownsEndpoint && alive(data) && endpoint && !ending) {
        endpoint.generation = data.generation;
        set({ call: data });
        await apply(await command("resume"));
        if (pc && pc.connectionState !== "connected") await restart();
      }
      if (!shouldResume || !data.ownsEndpoint || !alive(data)) await apply(data);
      if (pc && early.length) { const queued = early; early = []; queued.forEach(receiveSignal); }
    }
  } catch {
    // A reconnect observed during another fetch must survive until an authenticated rebind succeeds.
    if (session === getSessionVersion() && op === operation) rebindPending ||= shouldResume;
  }
  finally { syncing = false; }
}
async function tick() {
  if (ticking || stopping) return; ticking = true;
  const session = getSessionVersion(); const op = operation;
  try {
    if (endpoint && leaseDeadline && Date.now() > leaseDeadline && !pendingStart) { resetCall(); set({ error: "Cuộc gọi đã kết thúc do mất kết nối." }); return; }
    if (ending) { await flushEnd(); return; }
    const call = state().call;
    if (call && alive(call) && call.ownsEndpoint && endpoint && Date.now() - lastHeartbeat > 12000) {
      lastHeartbeat = Date.now();
      const { data } = await apiClient.post<CallSnapshot>(`/calls/${call.id}/heartbeat`, proof());
      if (session !== getSessionVersion() || op !== operation) return;
      await apply(data);
      if (pc && iceExpires - Date.now() < 120000) {
        const { data: ice } = await apiClient.post<{ iceServers: RTCIceServer[]; expiresAt: string }>(`/calls/${call.id}/ice-servers`, proof());
        if (session !== getSessionVersion() || op !== operation) return;
        pc?.setConfiguration({ iceServers: ice.iceServers }); iceExpires = Date.parse(ice.expiresAt); await restart();
      }
    }
    await syncCall();
  } catch { /* A later tick retries even without browser events. */ }
  finally { ticking = false; }
}
export function mountCalls() {
  stopping = false;
  const timer = setInterval(() => { void tick(); }, 3000);
  const reconnect = () => { void syncCall(true); };
  const visible = () => { if (document.visibilityState === "visible") void tick(); };
  const changed = () => { void syncCall(); };
  const signal = (event: Event) => receiveSignal((event as CustomEvent<Signal>).detail);
  const unload = () => { if (state().call?.ownsEndpoint) void endCall(); };
  window.addEventListener("call:changed", changed); window.addEventListener("call:signal", signal);
  window.addEventListener("chat:reconnected", reconnect); window.addEventListener("online", reconnect);
  window.addEventListener("pagehide", unload); document.addEventListener("visibilitychange", visible);
  void syncCall();
  return () => {
    stopping = true; clearInterval(timer); window.removeEventListener("call:changed", changed); window.removeEventListener("call:signal", signal);
    window.removeEventListener("chat:reconnected", reconnect); window.removeEventListener("online", reconnect);
    window.removeEventListener("pagehide", unload); document.removeEventListener("visibilitychange", visible); resetCall();
  };
}

export function dismissCall() { dismissed = state().call?.id ?? null; resetCall(); }


export function cancelPreparingCall() {
  if (!pendingStart) { resetCall(); return; }
  cancelPending = true; releaseMedia(); set({ error: "Đang xác nhận hủy cuộc gọi…" });
}
