import { apiClient } from "./client";
import type { StickerPack } from "../types";

export const expressionsApi = {
  stickerPacks: (signal?: AbortSignal) =>
    apiClient.get<StickerPack[]>("/expressions/sticker-packs", { signal }).then(response => response.data),
};
