import React, { useState } from "react";
import { reportsApi } from "../api/reports";
import { extractErrorMessage } from "../api/auth";
import type { ReportTargetType } from "../types";
import { CloseIcon } from "./icons";

interface ReportDialogProps {
  target: { type: ReportTargetType; id: string } | null;
  onClose: () => void;
  onSuccess?: () => void;
}

const REPORT_REASONS = [
  { value: "Spam", label: "Spam / Tin nhắn rác" },
  { value: "Harassment", label: "Quấy rối / Đe dọa" },
  { value: "InappropriateContent", label: "Nội dung phản cảm / Đồi trụy" },
  { value: "Scam", label: "Lừa đảo / Mạo danh" },
  { value: "Violence", label: "Bạo lực / Kích động thù hận" },
  { value: "Other", label: "Lý do khác" },
];

export const ReportDialog: React.FC<ReportDialogProps> = ({ target, onClose, onSuccess }) => {
  const [reason, setReason] = useState(REPORT_REASONS[0].value);
  const [description, setDescription] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [submitted, setSubmitted] = useState(false);

  if (!target) return null;

  const targetTypeName =
    target.type === "User" ? "người dùng" : target.type === "Message" ? "tin nhắn" : "hội thoại";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");

    try {
      await reportsApi.create({
        targetType: target.type,
        targetId: target.id,
        reason,
        description: description.trim() || undefined,
      });
      setSubmitted(true);
      setTimeout(() => {
        onSuccess?.();
        onClose();
      }, 1500);
    } catch (err) {
      setError(extractErrorMessage(err, "Không thể gửi báo cáo. Vui lòng thử lại sau."));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="confirm-dialog-backdrop" role="dialog" aria-modal="true" aria-labelledby="report-dialog-title">
      <div className="confirm-dialog" style={{ maxWidth: 440, width: "100%" }}>
        <header style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 16 }}>
          <h3 id="report-dialog-title" style={{ margin: 0, fontSize: "1.15rem", fontWeight: 600 }}>
            Báo cáo {targetTypeName}
          </h3>
          <button
            type="button"
            className="icon-btn"
            onClick={onClose}
            disabled={busy}
            aria-label="Đóng"
            style={{ border: "none", background: "none", cursor: "pointer" }}
          >
            <CloseIcon />
          </button>
        </header>

        {submitted ? (
          <div style={{ textAlign: "center", padding: "20px 0" }}>
            <p style={{ color: "var(--color-success, #22c55e)", fontSize: "1.1rem", fontWeight: 600 }}>
              ✓ Báo cáo đã được gửi!
            </p>
            <p style={{ color: "var(--color-text-muted, #888)", fontSize: "0.9rem" }}>
              Cảm ơn bạn. Ban kiểm duyệt sẽ xem xét và xử lý vi phạm.
            </p>
          </div>
        ) : (
          <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {error && (
              <div className="alert alert-danger" role="alert" style={{ color: "#ef4444", fontSize: "0.875rem" }}>
                {error}
              </div>
            )}

            <div>
              <label htmlFor="report-reason" style={{ display: "block", fontSize: "0.875rem", marginBottom: 6, fontWeight: 500 }}>
                Lý do báo cáo:
              </label>
              <select
                id="report-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                disabled={busy}
                style={{ width: "100%", padding: "8px 10px", borderRadius: 6, border: "1px solid var(--color-border, #ccc)" }}
              >
                {REPORT_REASONS.map((r) => (
                  <option key={r.value} value={r.value}>
                    {r.label}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label htmlFor="report-description" style={{ display: "block", fontSize: "0.875rem", marginBottom: 6, fontWeight: 500 }}>
                Mô tả chi tiết (tùy chọn):
              </label>
              <textarea
                id="report-description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                disabled={busy}
                rows={3}
                maxLength={500}
                placeholder="Cung cấp thêm ngữ cảnh hoặc chi tiết vi phạm..."
                style={{ width: "100%", padding: "8px 10px", borderRadius: 6, border: "1px solid var(--color-border, #ccc)", resize: "vertical" }}
              />
              <small style={{ color: "var(--color-text-muted, #888)", display: "block", textAlign: "right" }}>
                {description.length}/500
              </small>
            </div>

            <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 10 }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onClose}
                disabled={busy}
                style={{ padding: "8px 16px", borderRadius: 6, cursor: "pointer" }}
              >
                Hủy
              </button>
              <button
                type="submit"
                className="btn btn-danger"
                disabled={busy}
                style={{
                  padding: "8px 16px",
                  borderRadius: 6,
                  backgroundColor: "#dc2626",
                  color: "#fff",
                  border: "none",
                  cursor: busy ? "not-allowed" : "pointer",
                }}
              >
                {busy ? "Đang gửi..." : "Gửi báo cáo"}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
};
