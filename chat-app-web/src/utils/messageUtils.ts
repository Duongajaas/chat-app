import type { ChatMessage } from "../types";

/**
 * Merge messages từ 3 nguồn khác nhau (optimistic, REST, SignalR):
 * - Dedupe dựa vào message.id (GUID thật từ server)
 * - Nếu message mới có clientMessageId trùng với optimistic entry, xóa entry optimistic đi
 * - Sort lại theo sequence (optimistic không có sequence → xử lý như Number.MAX_VALUE để ở cuối)
 * - Không tin thứ tự incoming
 */
export function mergeMessages(existing: ChatMessage[], incoming: ChatMessage[]): ChatMessage[] {
  const map = new Map<string, ChatMessage>();

  // Bước 1: đưa toàn bộ dữ liệu cũ vào map trước, key = id hiện tại của nó
  for (const m of existing) {
    map.set(m.id, m);
  }

  // Bước 2: xử lý từng message mới
  for (const m of incoming) {
    // Nếu message mới có clientMessageId trùng với 1 optimistic entry đang có
    // → xóa entry tạm đi, thay bằng bản thật
    if (m.clientMessageId && map.has(m.clientMessageId)) {
      map.delete(m.clientMessageId);
    }
    // Ghi đè/thêm mới theo id THẬT
    map.set(m.id, m);
  }

  // Bước 3: sort lại theo sequence
  // Optimistic không có sequence → treat as Number.MAX_VALUE để ở cuối
  return Array.from(map.values()).sort((a, b) => {
    const aSeq = a.sequence ?? Number.MAX_VALUE;
    const bSeq = b.sequence ?? Number.MAX_VALUE;
    return aSeq - bSeq;
  });
}
