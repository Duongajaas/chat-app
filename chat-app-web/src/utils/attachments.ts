// Mirrors MediaValidation on the server; the server re-checks every file's content.
export const IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"];
export const DOCUMENT_EXTENSIONS = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".txt", ".csv", ".md"];
export const MAX_ATTACHMENT_BYTES = 8 * 1024 * 1024;

export const isImageType = (type: string | null | undefined) => !!type && IMAGE_TYPES.includes(type);

export function isAllowedDocument(name: string) {
  const dot = name.lastIndexOf(".");
  return dot > 0 && DOCUMENT_EXTENSIONS.includes(name.slice(dot).toLowerCase());
}

export function formatBytes(bytes: number | null | undefined) {
  if (bytes == null || !Number.isFinite(bytes) || bytes < 0) return "";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(bytes < 10 * 1024 ? 1 : 0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Short badge shown on a file card, e.g. "PDF", "DOCX". */
export function fileBadge(name: string | null | undefined) {
  const dot = name?.lastIndexOf(".") ?? -1;
  return dot > 0 ? name!.slice(dot + 1, dot + 5).toUpperCase() : "FILE";
}

/** Most files that can be picked at once; the server allows 10 uploads per minute. */
export const MAX_BATCH_FILES = 10;

export interface RejectedFile { name: string; reason: string; }
export interface BatchPlan { accepted: File[]; rejected: RejectedFile[]; }

/**
 * Splits a multi-file pick into the files worth uploading and the ones to explain to the user.
 * Order is preserved so messages arrive in the order the files were chosen.
 */
export function planAttachmentBatch(files: File[], kind: "image" | "file", max = MAX_BATCH_FILES): BatchPlan {
  const accepted: File[] = []; const rejected: RejectedFile[] = [];
  for (const file of files) {
    const name = file.name || "tệp không tên";
    if (kind === "image" && !isImageType(file.type)) rejected.push({ name, reason: "không phải ảnh JPEG, PNG hoặc WebP" });
    else if (kind === "file" && !isImageType(file.type) && !isAllowedDocument(file.name)) rejected.push({ name, reason: "định dạng không được hỗ trợ" });
    else if (file.size === 0) rejected.push({ name, reason: "file trống" });
    else if (file.size > MAX_ATTACHMENT_BYTES) rejected.push({ name, reason: "lớn hơn 8 MB" });
    else if (accepted.length >= max) rejected.push({ name, reason: `vượt quá ${max} mục mỗi lần` });
    else accepted.push(file);
  }
  return { accepted, rejected };
}

/** One readable sentence for everything that did not get sent; empty when all went through. */
export function describeBatchFailures(sent: number, total: number, failures: RejectedFile[], stopped?: string) {
  if (failures.length === 0 && !stopped) return "";
  const shown = failures.slice(0, 3).map(f => `${f.name} (${f.reason})`).join("; ");
  const more = failures.length > 3 ? ` và ${failures.length - 3} mục khác` : "";
  const parts: string[] = [];
  if (total > 1 || sent > 0) parts.push(`Đã gửi ${sent}/${total}.`);
  if (failures.length) parts.push(`Không gửi được: ${shown}${more}.`);
  if (stopped) parts.push(stopped);
  return parts.join(" ");
}
