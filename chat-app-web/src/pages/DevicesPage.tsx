import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { devicesApi } from "../api/devices";
import { extractErrorMessage } from "../api/auth";
import { LaptopIcon, MobileIcon } from "../components/icons";
import type { Device } from "../types";

function formatLastActive(iso: string | null): string {
  if (!iso) return "Không rõ";
  const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
  if (minutes < 1) return "Vừa xong";
  if (minutes < 60) return `${minutes} phút trước`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} giờ trước`;
  return `${Math.floor(hours / 24)} ngày trước`;
}

export default function DevicesPage() {
  const [devices, setDevices] = useState<Device[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState("");
  const [revokingId, setRevokingId] = useState<string | null>(null);

  useEffect(() => {
    devicesApi
      .list()
      .then(setDevices)
      .catch((err) => setError(extractErrorMessage(err, "Không tải được danh sách thiết bị.")))
      .finally(() => setIsLoading(false));
  }, []);

  async function handleRevoke(deviceId: string) {
    setRevokingId(deviceId);
    try {
      await devicesApi.revoke(deviceId);
      setDevices((prev) => prev.filter((d) => d.id !== deviceId));
    } catch (err) {
      setError(extractErrorMessage(err, "Không thể đăng xuất thiết bị này."));
    } finally {
      setRevokingId(null);
    }
  }

  return (
    <div className="profile-page">
      <div className="profile-card">
        <Link to="/profile" className="profile-back-link">← Quay lại trang cá nhân</Link>

        <h2 style={{ fontFamily: "var(--font-display)", fontSize: 20, margin: "0 0 4px" }}>Thiết bị đang đăng nhập</h2>
        <p style={{ color: "var(--color-text-muted)", fontSize: 13.5, margin: "0 0 20px" }}>
          Đăng xuất thiết bị lạ nếu không phải bạn.
        </p>

        {error && <div className="alert alert-danger">{error}</div>}

        {isLoading ? (
          <p style={{ color: "var(--color-text-muted)", fontSize: 13.5 }}>Đang tải...</p>
        ) : devices.length === 0 ? (
          <p style={{ color: "var(--color-text-muted)", fontSize: 13.5 }}>Không có thiết bị nào.</p>
        ) : (
          <div className="device-list">
            {devices.map((d) => (
              <div key={d.id} className="device-item">
                <div className="device-item__icon">
                  {d.platform === "Web" || d.platform === "Desktop" ? <LaptopIcon /> : <MobileIcon />}
                </div>
                <div className="device-item__body">
                  <div className="device-item__name">{d.deviceName || "Thiết bị không tên"}</div>
                  <div className="device-item__meta">{d.platform} · Hoạt động {formatLastActive(d.lastActiveAt)}</div>
                </div>
                <button className="device-item__revoke" onClick={() => handleRevoke(d.id)} disabled={revokingId === d.id}>
                  {revokingId === d.id ? "Đang xử lý..." : "Đăng xuất"}
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}