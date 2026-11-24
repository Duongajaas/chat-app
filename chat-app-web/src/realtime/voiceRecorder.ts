import { buildWaveform, MAX_VOICE_SECONDS, MIN_VOICE_SECONDS } from "../utils/voice";

export interface VoiceRecording { file: File; durationSeconds: number; waveform: number[]; }
export type VoiceErrorCode = "denied" | "no-device" | "unsupported" | "too-short" | "failed";

export class VoiceError extends Error {
  constructor(public code: VoiceErrorCode, message: string) { super(message); }
}

const FORMATS = [
  { mime: "audio/webm;codecs=opus", ext: "webm" },
  { mime: "audio/webm", ext: "webm" },
  { mime: "audio/ogg;codecs=opus", ext: "ogg" },
  { mime: "audio/mp4", ext: "m4a" }, // Safari
];

function pickFormat() {
  if (typeof MediaRecorder === "undefined") return null;
  return FORMATS.find(format => MediaRecorder.isTypeSupported(format.mime)) ?? null;
}

export function canRecordVoice() {
  return typeof navigator !== "undefined" && !!navigator.mediaDevices?.getUserMedia && pickFormat() !== null;
}

export function describeVoiceError(error: unknown) {
  if (error instanceof VoiceError) return error.message;
  return "Không ghi âm được. Vui lòng thử lại.";
}

function mapMicrophoneError(error: unknown) {
  const name = error instanceof DOMException ? error.name : "";
  if (name === "NotAllowedError" || name === "SecurityError")
    return new VoiceError("denied", "Bạn chưa cho phép dùng micro. Hãy cho phép micro trong cài đặt trình duyệt rồi thử lại.");
  if (name === "NotFoundError" || name === "OverconstrainedError")
    return new VoiceError("no-device", "Không tìm thấy micro trên thiết bị.");
  return new VoiceError("failed", "Không mở được micro. Có thể ứng dụng khác đang dùng nó.");
}

/** Records one voice message from the microphone. Create one instance per recording. */
export class VoiceRecorder {
  private stream?: MediaStream;
  private recorder?: MediaRecorder;
  private chunks: Blob[] = [];
  private finished?: Promise<void>;
  private format = FORMATS[0];
  private audioContext?: AudioContext;
  private analyser?: AnalyserNode;
  private levels: number[] = [];
  private startedAt = 0;
  private timer?: number;

  async start(onTick: (elapsedSeconds: number) => void) {
    const format = pickFormat();
    if (!format || !navigator.mediaDevices?.getUserMedia)
      throw new VoiceError("unsupported", "Trình duyệt này chưa hỗ trợ ghi âm.");
    let stream: MediaStream;
    try { stream = await navigator.mediaDevices.getUserMedia({ audio: true }); }
    catch (error) { throw mapMicrophoneError(error); }
    this.stream = stream; this.format = format;
    try {
      const recorder = new MediaRecorder(stream, { mimeType: format.mime });
      recorder.ondataavailable = event => { if (event.data.size > 0) this.chunks.push(event.data); };
      this.finished = new Promise<void>(resolve => { recorder.onstop = () => resolve(); });
      this.recorder = recorder;
      recorder.start();
    } catch {
      this.dispose();
      throw new VoiceError("failed", "Không bắt đầu ghi âm được.");
    }
    this.watchLevel(stream);
    this.startedAt = performance.now();
    this.timer = window.setInterval(() => {
      this.sampleLevel();
      onTick((performance.now() - this.startedAt) / 1000);
    }, 100);
  }

  /** Stops and returns the recording, or throws "too-short" for accidental taps. */
  async stop(): Promise<VoiceRecording> {
    const recorder = this.recorder;
    if (!recorder) throw new VoiceError("failed", "Chưa bắt đầu ghi âm.");
    const elapsed = (performance.now() - this.startedAt) / 1000;
    if (recorder.state !== "inactive") recorder.stop();
    await this.finished;
    const waveform = buildWaveform(this.levels);
    const type = this.format.mime.split(";")[0];
    const blob = new Blob(this.chunks, { type });
    const extension = this.format.ext;
    this.dispose();
    if (elapsed < MIN_VOICE_SECONDS || blob.size === 0)
      throw new VoiceError("too-short", "Tin nhắn thoại quá ngắn. Hãy giữ ghi âm lâu hơn một chút.");
    const durationSeconds = Math.min(MAX_VOICE_SECONDS, Math.max(MIN_VOICE_SECONDS, Math.round(elapsed)));
    const file = new File([blob], `tin-nhan-thoai-${Date.now()}.${extension}`, { type });
    return { file, durationSeconds, waveform };
  }

  /** Discards the recording and releases the microphone. */
  cancel() {
    try { if (this.recorder && this.recorder.state !== "inactive") this.recorder.stop(); } catch { /* already stopped */ }
    this.dispose();
  }

  private watchLevel(stream: MediaStream) {
    try {
      const context = new AudioContext();
      const analyser = context.createAnalyser();
      analyser.fftSize = 512;
      context.createMediaStreamSource(stream).connect(analyser);
      this.audioContext = context; this.analyser = analyser;
    } catch { /* The waveform falls back to neutral bars. */ }
  }

  private sampleLevel() {
    if (!this.analyser) return;
    const data = new Uint8Array(this.analyser.fftSize);
    this.analyser.getByteTimeDomainData(data);
    let sum = 0;
    for (const value of data) { const centred = (value - 128) / 128; sum += centred * centred; }
    this.levels.push(Math.sqrt(sum / data.length));
  }

  private dispose() {
    if (this.timer !== undefined) window.clearInterval(this.timer);
    this.timer = undefined;
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = undefined;
    void this.audioContext?.close().catch(() => undefined);
    this.audioContext = undefined; this.analyser = undefined;
  }
}
