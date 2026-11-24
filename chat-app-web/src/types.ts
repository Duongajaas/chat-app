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

export type UserRole = "User" | "Moderator" | "Admin";

export interface User {
  id: string;
  username: string;
  email: string | null;
  phone: string | null;
  fullName: string;
  avatarUrl: string | null;
  bio: string | null;
  isVerified: boolean;
  role?: UserRole;
  isFlagged?: boolean;
  restrictedUntil?: string | null;
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
  avatarUrl?: string | null;
  lastMessage: string;
  lastMessageAt: string;
  unreadCount: number;
  isOnline: boolean;
  isBlocked?: boolean;
  isAdmin?: boolean;
  isHidden?: boolean;
  hasLeft?: boolean;
  role?: "Owner" | "Admin" | "Member";
  permissions?: string[];
  memberCount?: number;
  version?: number;
  closedAt?: string | null;
  requestStatus?: RequestStatus;
  createdAt?: string;
  updatedAt?: string;
  lastMessageId?: string | null;
  defaultMessageTtlSeconds?: number | null;
}

export interface PinnedMessage { id: string; pinnedBy?: string | null; createdAt: string; message: ChatMessage; }

export interface ChatMessage {
  expression?: ExpressionMedia | null;
  senderName?: string | null;
  senderUsername?: string | null;
  type?: string;
  deliveryState?: import("./realtime/sendQueue").DeliveryState;
  deliveryError?: string;
  pendingSourceMessageId?: string;

  replyToMessageId?: string | null;
  replyPreview?: { id: string; senderName: string | null; contentSnippet: string | null; isAvailable: boolean; sequence?: number } | null;
  isForwarded?: boolean;
  mentions?: import("./utils/mentions").Mention[];
  pendingMentions?: import("./utils/mentions").MentionInput[];
  assetId?: string | null; mediaUrl?: string | null;
  /** Local-only file name shown while an uploaded attachment is still being sent. */
  pendingAttachmentName?: string;
  voiceDuration?: number | null;
  waveformPoints?: string | null;
  attachments?: { id: string; fileName: string | null; fileType: string | null; fileSize: number | null }[];
  id: string;
  conversationId: string;
  senderId: string;
  sequence: number;
  content: string;
  createdAt: string;
  clientMessageId?: string;
  status?: "Sending" | "Sent" | "Failed" | "Deleted" | "Hidden" | "Unavailable" | "Expired";
  expiresAt?: string | null;
  ttlSeconds?: number | null;
}

export interface MessageListResponse {
  messages: ChatMessage[];
  nextCursor?: number | null;
  hasMore: boolean;
  peerReadSequence?: number;
}

export interface ConversationPage { items: Conversation[]; nextCursor: string | null; }

export interface ExpressionInput { kind: "sticker" | "gif"; id: string; packId?: string | null; }
export interface ExpressionMedia extends ExpressionInput {
  provider: "local" | "giphy";
  name: string;
  url?: string | null;
  thumbnailUrl?: string | null;
  width: number;
  height: number;
  fileType?: string | null;
  animated?: boolean;
}
export interface StickerPack { id: string; name: string; stickers: ExpressionMedia[]; }
export type ReportTargetType = "User" | "Message" | "Conversation";
export type ReportStatus = "Pending" | "Reviewing" | "Resolved" | "Dismissed";

export interface CreateReportRequest {
  targetType: ReportTargetType;
  targetId: string;
  reason: string;
  description?: string;
}

export interface Report {
  id: string;
  reporterId: string;
  reporterName?: string | null;
  targetType: ReportTargetType;
  targetId: string;
  targetUserId?: string | null;
  targetPreview?: string | null;
  reason: string;
  description?: string | null;
  status: ReportStatus;
  reviewedBy?: string | null;
  reviewedByName?: string | null;
  reviewedAt?: string | null;
  moderatorNotes?: string | null;
  createdAt: string;
}

export interface ModerationDecisionRequest {
  decision: "Dismiss" | "Warn" | "DeleteMessage" | "FlagUser" | "RestrictUser";
  moderatorNotes?: string;
  restrictionMinutes?: number;
}

export interface AuditLogEntry {
  id: string;
  action: string;
  userId?: string | null;
  actorName?: string | null;
  targetId?: string | null;
  ipAddress?: string | null;
  details?: string | null;
  createdAt: string;
}
