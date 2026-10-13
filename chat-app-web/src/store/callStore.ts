import { create } from "zustand";
export interface CallSnapshot {
  id: string; conversationId: string; clientCallId?: string; type: "Audio" | "Video";
  status: string; version: number; isCaller: boolean; ownsEndpoint: boolean; hasEndpoint: boolean;
  generation: number; epoch: number; peerName: string; peerAvatar?: string;
  expiresAt?: string; connectedAt?: string; leaseExpiresAt?: string; recoveryDeadlineAt?: string;
  durationSeconds: number; reason?: string; serverTime: string;
}
export interface CallState {
  call: CallSnapshot | null; preparing: boolean; busy: boolean; error: string;
  local: MediaStream | null; remote: MediaStream | null; muted: boolean; cameraOff: boolean; connected: boolean;
}
export const useCallStore = create<CallState>(() => ({ call: null, preparing: false, busy: false,
  error: "", local: null, remote: null, muted: false, cameraOff: false, connected: false }));
