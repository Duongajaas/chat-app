import type { ReactNode } from "react";
import type { Mention } from "../utils/mentions";

export function MessageText({ content, mentions = [] }: { content: string; mentions?: Mention[] }) {
  const parts: ReactNode[] = []; let offset = 0;

  if (mentions && mentions.length > 0) {
    for (const mention of [...mentions].sort((a, b) => a.start - b.start)) {
      if (mention.start < offset || mention.length < 2 || mention.start + mention.length > content.length) continue;
      parts.push(content.slice(offset, mention.start));
      parts.push(<span key={mention.start} className="message-mention">@{mention.username}</span>);
      offset = mention.start + mention.length;
    }
  }

  parts.push(content.slice(offset));
  return <>{parts}</>;
}
