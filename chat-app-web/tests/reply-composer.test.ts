import { expect, it } from "vitest";
import { collectMentions, prependReplyMention } from "../src/utils/mentions";
const an = { userId: "an", username: "an" };
it("adds a confirmed reply mention without losing draft text", () => {
  const text = prependReplyMention("😀 bản nháp", an, "me");
  expect(text).toBe("@an 😀 bản nháp");
  expect(collectMentions(text, [an])).toEqual([{ userId: "an", start: 0, length: 3 }]);
});
it("does not duplicate mentions or insert an unconfirmed/self mention", () => {
  expect(prependReplyMention("chào @An nhé", an, "me")).toBe("chào @An nhé");
  expect(prependReplyMention("nháp", an, "an")).toBe("nháp");
  expect(prependReplyMention("nháp", undefined, "me")).toBe("nháp");
});
it("never truncates an existing draft to make room for a mention", () => {
  const text = "a".repeat(4000);
  expect(prependReplyMention(text, an, "me")).toBe(text);
});
