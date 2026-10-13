import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useAuth } from "../context/AuthContext";
import { useCallStore } from "../store/callStore";
import { acceptCall, cancelPreparingCall, dismissCall, endCall, mountCalls, rejectCall, resetCall, startCall, toggleCamera, toggleMic } from "../realtime/callManager";
import { PhoneIcon, VideoIcon, CloseIcon } from "./icons";

function Media({ stream, local = false, video = false }: { stream: MediaStream | null; local?: boolean; video?: boolean }) {
  const ref = useRef<HTMLVideoElement>(null);
  const [blocked, setBlocked] = useState(false);
  useEffect(() => {
    const element = ref.current; if (!element) return;
    element.srcObject = stream;
    if (stream) void element.play().then(() => setBlocked(false)).catch(() => setBlocked(true));
    return () => { element.pause(); element.srcObject = null; };
  }, [stream]);
  return <><video ref={ref} playsInline autoPlay muted={local} className={video ? local ? "call-screen__local" : "call-screen__remote" : "call-screen__audio"} />
    {blocked && !local && <button className="call-screen__audio-enable" onClick={() => void ref.current?.play().then(() => setBlocked(false)).catch(() => setBlocked(true))}>Bật âm thanh</button>}</>;
}
function Mic({ off }: { off: boolean }) {
  return <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2M12 19v3M8 22h8"/>{off && <path d="m3 3 18 18"/>}</svg>;
}
export function CallOverlay() {
  const { user } = useAuth();
  const s = useCallStore();
  const dialog = useRef<HTMLDialogElement>(null);
  const [now, setNow] = useState(Date.now());
  const [minimized, setMinimized] = useState(false);
  useEffect(() => { setMinimized(false); }, [s.call?.id]);
  useEffect(() => { if (s.call && !["Ringing", "Accepted"].includes(s.call.status)) setMinimized(false); }, [s.call?.status]);
  useEffect(() => { if (user) return mountCalls(); }, [user?.id]);
  const open = !!(s.call || s.preparing || s.error);
  useEffect(() => {
    if (minimized) dialog.current?.close();
    else if (open && !dialog.current?.open) dialog.current?.showModal();
  }, [open, minimized]);
  useEffect(() => { if (!open) return; const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, [open]);
  // Audible ringing is best effort. Visual controls remain usable if autoplay is denied.
  useEffect(() => {
    if (s.call?.status !== "Ringing" || s.call.hasEndpoint && !s.call.ownsEndpoint) return;
    let audio: AudioContext | undefined;
    try { audio = new AudioContext(); } catch { return; }
    const ring = () => {
      if (!audio || audio.state !== "running") return;
      const tone = audio.createOscillator(); const gain = audio.createGain();
      tone.frequency.value = s.call?.isCaller ? 440 : 660; gain.gain.value = 0.035;
      tone.connect(gain); gain.connect(audio.destination); tone.start(); tone.stop(audio.currentTime + 0.3);
    };
    void audio.resume().then(ring).catch(() => undefined); const timer = setInterval(ring, 2200);
    return () => { clearInterval(timer); void audio?.close().catch(() => undefined); };
  }, [s.call?.id, s.call?.status, s.call?.hasEndpoint, s.call?.ownsEndpoint]);
  if (!user || !open) return null;
  const call = s.call; const live = call?.status === "Ringing" || call?.status === "Accepted";
  const incoming = call?.status === "Ringing" && !call.isCaller && !call.hasEndpoint;
  const elsewhere = live && call?.hasEndpoint && !call.ownsEndpoint;
  const seconds = live && call?.connectedAt ? Math.max(0, Math.floor((now - Date.parse(call.connectedAt)) / 1000)) : call?.durationSeconds ?? 0;
  const time = `${Math.floor(seconds / 60).toString().padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`;
  const label = s.preparing ? "Đang chuẩn bị cuộc gọi…" : elsewhere ? "Cuộc gọi trên thiết bị khác" : incoming ? `Cuộc gọi ${call?.type === "Video" ? "video" : "thoại"} đến` :
    call?.status === "Ringing" ? "Đang gọi…" : call?.status === "Accepted" ? s.connected ? time : "Đang kết nối…" :
    call?.status === "Busy" ? "Người nhận đang bận" : call?.status === "Missed" ? "Không có phản hồi" : call?.status === "Rejected" ? "Cuộc gọi bị từ chối" : "Cuộc gọi đã kết thúc";
  return createPortal(<>
    {minimized && <aside className="call-mini" aria-label="Cuộc gọi đang diễn ra">
      <button className="call-mini__restore" onClick={() => setMinimized(false)} aria-label="Mở lại cuộc gọi"><PhoneIcon/><span><strong>{call?.peerName}</strong><small>{label}</small></span></button>
      <button className="call-mini__end" disabled={s.busy} onClick={() => void endCall()} aria-label="Cúp máy"><PhoneIcon/></button>
    </aside>}
    <dialog ref={dialog} className="call-screen" aria-labelledby="call-title" onCancel={e => { e.preventDefault(); if (live && call?.ownsEndpoint && !s.busy) setMinimized(true); else if (!live && !s.preparing) resetCall(); }}>
    <div className={`call-screen__body ${call?.type === "Video" && s.connected ? "call-screen__body--video" : ""}`}>
      {live && call?.ownsEndpoint && <button className="call-screen__minimize" aria-label="Thu nhỏ cuộc gọi" onClick={() => setMinimized(true)}><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M5 16h14"/></svg></button>}
      <Media stream={s.remote} video={call?.type === "Video"} />
      <Media stream={s.local} local video={call?.type === "Video" && !s.cameraOff} />
      <div className="call-screen__identity">
        <div className="call-screen__avatar">{call?.peerAvatar ? <img src={call.peerAvatar} alt="" /> : <span>{call?.peerName.slice(0, 1) ?? "C"}</span>}</div>
        <h2 id="call-title">{call?.peerName ?? "Cuộc gọi"}</h2><p role="status">{label}</p>
      </div>
      {s.error && <p className="call-screen__error" role="alert">{s.error}</p>}
      <div className="call-screen__controls">
        {s.preparing ? <button className="call-control call-control--end" onClick={cancelPreparingCall} aria-label="Hủy cuộc gọi"><PhoneIcon/><span>Hủy</span></button> : incoming ? <><button className="call-control call-control--end" disabled={s.busy} onClick={() => void rejectCall()} aria-label="Từ chối"><PhoneIcon/><span>Từ chối</span></button>
          <button className="call-control call-control--accept" disabled={s.busy} onClick={() => void acceptCall()} aria-label="Nhận cuộc gọi"><PhoneIcon/><span>{s.busy ? "Đang mở…" : "Nhận"}</span></button></> :
          live && call?.ownsEndpoint ? <>
            <button className="call-control" aria-pressed={s.muted} aria-label={s.muted ? "Bật micro" : "Tắt micro"} onClick={toggleMic}><Mic off={s.muted}/><span>{s.muted ? "Bật mic" : "Tắt mic"}</span></button>
            {call.type === "Video" && <button className="call-control" aria-pressed={s.cameraOff} disabled={!s.local?.getVideoTracks().length} aria-label={s.cameraOff ? "Bật camera" : "Tắt camera"} onClick={toggleCamera}><VideoIcon/><span>{s.cameraOff ? "Bật camera" : "Tắt camera"}</span></button>}
            <button className="call-control call-control--end" disabled={s.busy} onClick={() => void endCall()} aria-label="Cúp máy"><PhoneIcon/><span>Cúp máy</span></button>
          </> : !s.preparing && <button className="call-control" onClick={dismissCall} aria-label="Đóng cuộc gọi"><CloseIcon/><span>Đóng</span></button>}
      </div>
    </div>
  </dialog></>, document.body);
}
export function CallLogBubble({ content, isCaller, conversationId, disabled }: { content: string; isCaller: boolean; conversationId: string; disabled: boolean }) {
  let log: { callType?: string; status?: string; reason?: string; durationSeconds?: number };
  try { log = JSON.parse(content); if (!log || typeof log !== "object") throw new Error(); }
  catch { return <p>Cuộc gọi</p>; }
  const label = log.status === "Ended" ? "Cuộc gọi đã kết thúc" : log.reason === "Ended" && log.status === "Failed" ? "Cuộc gọi đã kết thúc" :
    log.status === "Missed" ? isCaller ? "Không có phản hồi" : "Cuộc gọi nhỡ" : log.status === "Busy" ? isCaller ? "Người nhận đang bận" : "Cuộc gọi nhỡ" :
    log.status === "Canceled" ? isCaller ? "Bạn đã hủy cuộc gọi" : "Cuộc gọi nhỡ" : log.status === "Rejected" ? isCaller ? "Cuộc gọi bị từ chối" : "Bạn đã từ chối" : "Cuộc gọi bị gián đoạn";
  const duration = Math.max(0, Number(log.durationSeconds) || 0);
  return <div className="call-log"><div>{log.callType === "Video" ? <VideoIcon/> : <PhoneIcon/>}<strong>{label}</strong></div>
    {duration > 0 && <small>{Math.floor(duration / 60)}:{(duration % 60).toString().padStart(2, "0")}</small>}
    <button disabled={disabled} onClick={() => void startCall(conversationId, log.callType === "Video" ? "Video" : "Audio")}>Gọi lại</button></div>;
}
