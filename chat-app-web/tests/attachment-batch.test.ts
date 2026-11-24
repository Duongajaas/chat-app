import { describe, expect, it } from "vitest";
import { MAX_ATTACHMENT_BYTES, MAX_BATCH_FILES, describeBatchFailures, planAttachmentBatch } from "../src/utils/attachments";

const file = (name: string, type: string, size = 100) => new File([new Uint8Array(size)], name, { type });
const png = (name: string) => file(name, "image/png");
const pdf = (name: string) => file(name, "application/pdf");

describe("planAttachmentBatch", () => {
  it("keeps every valid image in the order they were chosen", () => {
    const plan = planAttachmentBatch([png("b.png"), png("a.png"), file("c.webp", "image/webp")], "image");
    expect(plan.accepted.map(f => f.name)).toEqual(["b.png", "a.png", "c.webp"]);
    expect(plan.rejected).toEqual([]);
  });
  it("accepts documents and images in the file picker, and explains the rest", () => {
    const plan = planAttachmentBatch([pdf("ok.pdf"), png("p.png"), file("setup.exe", "application/octet-stream"), file("notes.txt", "text/plain")], "file");
    expect(plan.accepted.map(f => f.name)).toEqual(["ok.pdf", "p.png", "notes.txt"]);
    expect(plan.rejected).toEqual([{ name: "setup.exe", reason: "định dạng không được hỗ trợ" }]);
  });
  it("only accepts real images in the image picker", () => {
    const plan = planAttachmentBatch([pdf("ok.pdf"), png("p.png"), file("anim.gif", "image/gif")], "image");
    expect(plan.accepted.map(f => f.name)).toEqual(["p.png"]);
    expect(plan.rejected.map(r => r.name)).toEqual(["ok.pdf", "anim.gif"]);
  });
  it("rejects empty and oversized files individually without blocking the others", () => {
    const plan = planAttachmentBatch([png("good.png"), file("empty.png", "image/png", 0), file("big.png", "image/png", MAX_ATTACHMENT_BYTES + 1), file("edge.png", "image/png", MAX_ATTACHMENT_BYTES)], "image");
    expect(plan.accepted.map(f => f.name)).toEqual(["good.png", "edge.png"]);
    expect(plan.rejected).toEqual([{ name: "empty.png", reason: "file trống" }, { name: "big.png", reason: "lớn hơn 8 MB" }]);
  });
  it("caps one batch and reports the overflow", () => {
    const files = Array.from({ length: MAX_BATCH_FILES + 3 }, (_, i) => png(`${i}.png`));
    const plan = planAttachmentBatch(files, "image");
    expect(plan.accepted).toHaveLength(MAX_BATCH_FILES);
    expect(plan.accepted[0].name).toBe("0.png");
    expect(plan.rejected.map(r => r.name)).toEqual(["10.png", "11.png", "12.png"]);
    expect(plan.rejected.every(r => r.reason.includes(String(MAX_BATCH_FILES)))).toBe(true);
  });
  it("does not count rejected files against the cap", () => {
    const files = [...Array.from({ length: 5 }, (_, i) => file(`bad${i}.gif`, "image/gif")), ...Array.from({ length: MAX_BATCH_FILES }, (_, i) => png(`${i}.png`))];
    expect(planAttachmentBatch(files, "image").accepted).toHaveLength(MAX_BATCH_FILES);
  });
  it("handles an empty pick", () => {
    expect(planAttachmentBatch([], "file")).toEqual({ accepted: [], rejected: [] });
  });
});

describe("describeBatchFailures", () => {
  it("is empty when everything was sent", () => {
    expect(describeBatchFailures(3, 3, [])).toBe("");
  });
  it("names the files that failed and how many went through", () => {
    expect(describeBatchFailures(2, 3, [{ name: "a.pdf", reason: "lớn hơn 8 MB" }])).toBe("Đã gửi 2/3. Không gửi được: a.pdf (lớn hơn 8 MB).");
  });
  it("summarises long failure lists", () => {
    const failures = Array.from({ length: 5 }, (_, i) => ({ name: `f${i}`, reason: "lỗi" }));
    const text = describeBatchFailures(0, 5, failures);
    expect(text).toContain("f0 (lỗi); f1 (lỗi); f2 (lỗi)");
    expect(text).toContain("và 2 mục khác");
    expect(text).not.toContain("f3");
  });
  it("explains why a batch stopped early", () => {
    expect(describeBatchFailures(1, 4, [], "Bạn thao tác quá nhanh, 3 mục còn lại chưa được gửi.")).toBe("Đã gửi 1/4. Bạn thao tác quá nhanh, 3 mục còn lại chưa được gửi.");
  });
  it("does not prefix a single-file failure with a count", () => {
    expect(describeBatchFailures(0, 1, [{ name: "x.exe", reason: "định dạng không được hỗ trợ" }])).toBe("Không gửi được: x.exe (định dạng không được hỗ trợ).");
  });
});
