import type { ChatMessage } from "../types";
import { CloseIcon, QuoteIcon } from "./icons";
import { messagePreview } from "./PinsBar";

export function ReplyingToBar({ message, senderName, onCancel }: { message: ChatMessage; senderName: string; onCancel: () => void }) {
  return <div className="replying-to-bar" aria-label="Tin nhắn đang trả lời">
    <span className="replying-to-bar__accent"/><QuoteIcon size={17}/>
    <div className="replying-to-bar__text"><span className="replying-to-bar__name">Trả lời {senderName}</span>
      <span className="replying-to-bar__preview">{messagePreview(message)}</span></div>
    <button type="button" className="replying-to-bar__cancel" onClick={onCancel} aria-label="Hủy trả lời"><CloseIcon size={17}/></button>
  </div>;
}
