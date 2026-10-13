export interface MentionInput { userId: string; start: number; length: number; }
export interface Mention extends MentionInput { username: string; }
export function collectMentions(content: string, members: { userId: string; username: string }[]): MentionInput[] {
  const byName = new Map(members.map(m => [m.username.toLowerCase(), m]));
  const result: MentionInput[] = [];
  for (const match of content.matchAll(/(^|[^\p{L}\p{N}_@.])@([\p{L}\p{N}_.-]+)/gu)) {
    const member = byName.get(match[2].toLowerCase());
    if (member) result.push({ userId: member.userId, start: match.index! + match[1].length, length: match[2].length + 1 });
  }
  return result;
}

/** Keep the draft intact and do not duplicate a mention already present in the text. */
export function prependReplyMention(content: string, member: { userId: string; username: string } | undefined, currentUserId?: string) {
  if (!member?.username || member.userId === currentUserId) return content;
  if (collectMentions(content, [member]).length) return content;
  const next = `@${member.username} ${content}`;
  return next.length <= 4000 ? next : content;
}
