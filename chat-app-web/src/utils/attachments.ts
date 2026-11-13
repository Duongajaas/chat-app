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
