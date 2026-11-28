import { useState } from "react";
import EmojiPicker, { Categories } from "emoji-picker-react";
import type { ExpressionHistory } from "../utils/expressions";

export default function EmojiPanel({ history, onSelect, dark }: {
  history: ExpressionHistory; onSelect: (emoji: string, unified: string) => void; dark: boolean;
}) {
  const [mode, setMode] = useState<"recent" | "frequent">("recent");
  const suggested = [...history.emojis].sort((a, b) => mode === "recent" ? b.usedAt - a.usedAt : b.count - a.count || b.usedAt - a.usedAt);
  return <div className="emoji-panel">
    <div className="expression-filters" role="group" aria-label="Lịch sử emoji">
      <button type="button" aria-pressed={mode === "recent"} onClick={() => setMode("recent")}>Gần đây</button>
      <button type="button" aria-pressed={mode === "frequent"} onClick={() => setMode("frequent")}>Thường dùng</button>
    </div>
    <EmojiPicker emojiStyle="native" theme={dark ? "dark" : "light"} width="100%" height={340}
      lazyLoadEmojis autoFocusSearch suggestedEmojis={suggested.map(item => item.unified)}
      labels={{ searchPlaceholder: "Tìm emoji…", searchLabel: "Tìm emoji", loading: "Đang tải emoji…", searchResultsNone: "Không tìm thấy emoji" }}
      previewConfig={{ showPreview: false }}
      categories={[
        { category: Categories.SUGGESTED, name: mode === "recent" ? "Gần đây" : "Thường dùng" },
        { category: Categories.SMILEYS_PEOPLE, name: "Khuôn mặt & con người" },
        { category: Categories.ANIMALS_NATURE, name: "Động vật & thiên nhiên" },
        { category: Categories.FOOD_DRINK, name: "Đồ ăn & thức uống" },
        { category: Categories.TRAVEL_PLACES, name: "Du lịch & địa điểm" },
        { category: Categories.ACTIVITIES, name: "Hoạt động" },
        { category: Categories.OBJECTS, name: "Đồ vật" },
        { category: Categories.SYMBOLS, name: "Biểu tượng" },
        { category: Categories.FLAGS, name: "Cờ" },
      ]} onEmojiClick={item => onSelect(item.emoji, item.unified)} />
  </div>;
}
