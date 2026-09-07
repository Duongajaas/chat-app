export type DevicePlatform = "Web" | "Android" | "Ios" | "Desktop";

export interface DeviceInfo {
  deviceToken: string;
  deviceName: string;
  platform: DevicePlatform;
}

export interface Device {
  id: string;
  deviceName: string | null;
  platform: DevicePlatform;
  lastActiveAt: string | null;
}

export interface BlockedUser {
  id: string;
  username: string;
  fullName: string;
  avatarUrl: string | null;
  blockedAt: string;
}

export interface User {
  id: string;
  username: string;
  email: string | null;
  phone: string | null;
  fullName: string;
  avatarUrl: string | null;
  bio: string | null;
  isVerified: boolean;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: User;
}

export interface RegisterPayload {
  fullName: string;
  username: string;
  email: string;
  password: string;
}

export interface ApiErrorResponse {
  message: string;
}

export interface UpdateProfilePayload {
  fullName: string;
  bio?: string;
  avatarUrl?: string;
}

export type ConversationType = "Direct" | "Group";
export type RequestStatus = "Accepted" | "Pending" | "Rejected";

export interface Conversation {
  id: string;
  name: string;
  type: ConversationType;
  peerUserId?: string;
  avatarColor: string;
  lastMessage: string;
  lastMessageAt: string;
  unreadCount: number;
  isOnline: boolean;
  isBlocked?: boolean;
  isAdmin?: boolean;
  requestStatus?: RequestStatus;
  createdAt?: string;
  updatedAt?: string;
  lastMessageId?: string | null;
}

export interface ChatMessage {
  id: string;
  conversationId: string;
  senderId: string;
  sequence: number;
  content: string;
  createdAt: string;
  clientMessageId?: string;
  status?: "Sending" | "Sent" | "Failed" | "Deleted";
}

export interface MessageListResponse {
  messages: ChatMessage[];
  nextCursor?: number | null;
  hasMore: boolean;
}
