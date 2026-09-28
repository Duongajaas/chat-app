import type { Conversation } from "../types";
export function normalizeConversation(item: Conversation): Conversation {
  const id = String(item.id);
  const palette = ["#33d6a6", "#7fa8ff", "#ff9f6b", "#c792ea", "#f4c95d", "#ff7aa2"];
  return { ...item, id, name: item.name ?? "Cuộc trò chuyện", type: item.type === "Group" ? "Group" : "Direct",
    avatarColor: palette[Array.from(id).reduce((sum, c) => sum + c.charCodeAt(0), 0) % palette.length],
    lastMessage: item.lastMessage ?? "Bắt đầu cuộc trò chuyện", lastMessageAt: item.lastMessageAt ?? item.createdAt ?? "",
    unreadCount: Number(item.unreadCount ?? 0), isOnline: false, isBlocked: Boolean(item.isBlocked) };
}
