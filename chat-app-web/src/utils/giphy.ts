/** Only embed HTTPS GIF media served by GIPHY; other messages remain text. */
export function getGiphyMediaUrl(content: string): string | null {
  try {
    const url = new URL(content.trim());
    if (url.protocol !== "https:" || url.username || url.password || url.port) return null;
    if (!/^media\d*\.giphy\.com$/.test(url.hostname)) return null;
    if (!/^\/media\/(?:[a-zA-Z0-9_.~-]+\/)+[a-zA-Z0-9_-]+\.gif$/.test(url.pathname)) return null;
    return url.href;
  } catch {
    return null;
  }
}
