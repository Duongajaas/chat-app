import type { DeviceInfo } from "../types";

const DEVICE_TOKEN_KEY = "chatapp_device_token";

function getOrCreateDeviceToken(): string {
  let token = localStorage.getItem(DEVICE_TOKEN_KEY);
  if (!token) {
    token = crypto.randomUUID();
    localStorage.setItem(DEVICE_TOKEN_KEY, token);
  }
  return token;
}

function getDeviceName(): string {
  const ua = navigator.userAgent;

  let browser = "Trình duyệt";
  if (ua.includes("Edg/")) browser = "Edge";
  else if (ua.includes("Chrome/")) browser = "Chrome";
  else if (ua.includes("Firefox/")) browser = "Firefox";
  else if (ua.includes("Safari/")) browser = "Safari";

  let os = "";
  if (ua.includes("Windows")) os = "Windows";
  else if (ua.includes("Mac OS")) os = "macOS";
  else if (ua.includes("Android")) os = "Android";
  else if (ua.includes("iPhone") || ua.includes("iPad")) os = "iOS";
  else if (ua.includes("Linux")) os = "Linux";

  return os ? `${browser} trên ${os}` : browser;
}

export function getDeviceInfo(): DeviceInfo {
  return {
    deviceToken: getOrCreateDeviceToken(),
    deviceName: getDeviceName(),
    platform: "Web",
  };
}