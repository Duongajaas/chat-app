import { useRef, useState } from "react";
import { PauseIcon, PlayIcon } from "./icons";
import { formatDuration, parseWaveform } from "../utils/voice";

interface VoicePlayerProps {
  /** Fetches the private audio through the authorized API and returns a local URL. */
  load: () => Promise<string | undefined>;
  busy: boolean;
  duration?: number | null;
  waveform?: string | null;
}

export function VoicePlayer({ load, busy, duration, waveform }: VoicePlayerProps) {
  const audio = useRef<HTMLAudioElement>(null);
  const loadedUrl = useRef<string>();
  const [playing, setPlaying] = useState(false);
  const [current, setCurrent] = useState(0);
  const bars = parseWaveform(waveform);
  const total = duration && duration > 0 ? duration : 0;
  const progress = total > 0 ? Math.min(1, current / total) : 0;

  async function toggle() {
    const element = audio.current;
    if (!element) return;
    if (playing) { element.pause(); return; }
    if (loadedUrl.current !== element.getAttribute("src")) element.removeAttribute("src");
    if (!element.getAttribute("src")) {
      const url = await load();
      if (!url) return;
      loadedUrl.current = url; element.src = url;
    }
    try { await element.play(); } catch { /* The browser asked for another tap; the audio is already loaded. */ }
  }

  return <div className="voice-player">
    <button type="button" className="voice-player__button" onClick={() => void toggle()} disabled={busy && !playing}
      aria-label={playing ? "Tạm dừng tin nhắn thoại" : "Phát tin nhắn thoại"}>
      {playing ? <PauseIcon /> : <PlayIcon />}
    </button>
    <div className="voice-player__bars" role="progressbar" aria-label="Tiến độ tin nhắn thoại"
      aria-valuemin={0} aria-valuemax={Math.max(total, 1)} aria-valuenow={Math.round(current)}>
      {bars.map((height, index) => <span key={index} className={index / bars.length < progress ? "is-played" : undefined} style={{ height: `${height}%` }} />)}
    </div>
    <span className="voice-player__time">{busy && !playing ? "Đang tải…" : formatDuration(playing || current > 0 ? current : total)}</span>
    <audio ref={audio} preload="none"
      onPlay={() => setPlaying(true)} onPause={() => setPlaying(false)}
      onEnded={() => { setPlaying(false); setCurrent(0); }}
      onTimeUpdate={event => setCurrent(event.currentTarget.currentTime)} />
  </div>;
}
