import { apiClient } from "./client";

export interface UploadedMedia { assetId: string; url: string; fileName: string; contentType: string; }
export async function uploadMedia(file: File, signal?: AbortSignal): Promise<UploadedMedia> {
  if (!file.size || file.size > 8 * 1024 * 1024) throw new Error("File phải có dung lượng từ 1 byte đến 8 MB.");
  const form = new FormData(); form.append("file", file);
  return (await apiClient.post<UploadedMedia>("/files/upload", form, { signal, timeout: 45000 })).data;
}
