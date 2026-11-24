import React, { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { moderationApi } from "../api/moderation";
import { extractErrorMessage } from "../api/auth";
import type { Report, ReportStatus, ModerationDecisionRequest } from "../types";
import { ArrowLeftIcon } from "../components/icons";

export default function ModerationPage() {
  const { user } = useAuth();
  const navigate = useNavigate();

  const [reports, setReports] = useState<Report[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [typeFilter, setTypeFilter] = useState<string>("");
  const [selectedReport, setSelectedReport] = useState<Report | null>(null);

  // Decision modal state
  const [decisionAction, setDecisionAction] = useState<ModerationDecisionRequest["decision"]>("Dismiss");
  const [moderatorNotes, setModeratorNotes] = useState("");
  const [restrictionMinutes, setRestrictionMinutes] = useState(60);
  const [submittingDecision, setSubmittingDecision] = useState(false);

  useEffect(() => {
    if (user && user.role !== "Moderator" && user.role !== "Admin") {
      navigate("/", { replace: true });
    }
  }, [user, navigate]);

  const loadReports = async () => {
    setLoading(true);
    setError("");
    try {
      const data = await moderationApi.listReports({
        status: statusFilter || undefined,
        targetType: typeFilter || undefined,
      });
      setReports(data);
    } catch (err) {
      setError(extractErrorMessage(err, "Không thể tải danh sách báo cáo."));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadReports();
  }, [statusFilter, typeFilter]);

  const handleApplyDecision = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedReport) return;

    setSubmittingDecision(true);
    setError("");
    try {
      await moderationApi.applyDecision(selectedReport.id, {
        decision: decisionAction,
        moderatorNotes: moderatorNotes.trim() || undefined,
        restrictionMinutes: decisionAction === "RestrictUser" ? restrictionMinutes : undefined,
      });
      setSelectedReport(null);
      setModeratorNotes("");
      await loadReports();
    } catch (err) {
      setError(extractErrorMessage(err, "Không thể áp dụng quyết định kiểm duyệt."));
    } finally {
      setSubmittingDecision(false);
    }
  };

  const getStatusBadge = (status: ReportStatus) => {
    switch (status) {
      case "Pending":
        return <span style={{ padding: "3px 8px", borderRadius: 4, background: "#fef3c7", color: "#92400e", fontSize: "0.8rem", fontWeight: 600 }}>Chờ xử lý</span>;
      case "Reviewing":
        return <span style={{ padding: "3px 8px", borderRadius: 4, background: "#dbeafe", color: "#1e40af", fontSize: "0.8rem", fontWeight: 600 }}>Đang xem</span>;
      case "Resolved":
        return <span style={{ padding: "3px 8px", borderRadius: 4, background: "#dcfce7", color: "#166534", fontSize: "0.8rem", fontWeight: 600 }}>Đã xử lý</span>;
      case "Dismissed":
        return <span style={{ padding: "3px 8px", borderRadius: 4, background: "#f3f4f6", color: "#4b5563", fontSize: "0.8rem", fontWeight: 600 }}>Đã bác bỏ</span>;
    }
  };

  return (
    <div style={{ maxWidth: 1000, margin: "0 auto", padding: "20px 16px" }}>
      <header style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 24, borderBottom: "1px solid var(--color-border, #e5e7eb)", paddingBottom: 16 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <Link to="/" className="icon-btn" title="Về trang chat" style={{ display: "flex", alignItems: "center", textDecoration: "none" }}>
            <ArrowLeftIcon />
          </Link>
          <h1 style={{ margin: 0, fontSize: "1.5rem", fontWeight: 700 }}>Bảng kiểm duyệt (Moderation)</h1>
        </div>
        <button
          type="button"
          onClick={loadReports}
          disabled={loading}
          style={{ padding: "6px 14px", borderRadius: 6, border: "1px solid var(--color-border, #ccc)", cursor: "pointer", background: "var(--color-surface, #fff)" }}
        >
          {loading ? "Đang tải..." : "Làm mới"}
        </button>
      </header>

      {error && (
        <div className="alert alert-danger" role="alert" style={{ color: "#dc2626", background: "#fee2e2", padding: "10px 14px", borderRadius: 6, marginBottom: 16 }}>
          {error}
        </div>
      )}

      {/* Filters */}
      <div style={{ display: "flex", gap: 16, marginBottom: 20, flexWrap: "wrap" }}>
        <div>
          <label style={{ fontSize: "0.85rem", fontWeight: 600, display: "block", marginBottom: 4 }}>Trạng thái:</label>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            style={{ padding: "6px 12px", borderRadius: 6, border: "1px solid var(--color-border, #ccc)" }}
          >
            <option value="">Tất cả trạng thái</option>
            <option value="Pending">Chờ xử lý</option>
            <option value="Reviewing">Đang xem</option>
            <option value="Resolved">Đã giải quyết</option>
            <option value="Dismissed">Đã bác bỏ</option>
          </select>
        </div>

        <div>
          <label style={{ fontSize: "0.85rem", fontWeight: 600, display: "block", marginBottom: 4 }}>Đối tượng:</label>
          <select
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
            style={{ padding: "6px 12px", borderRadius: 6, border: "1px solid var(--color-border, #ccc)" }}
          >
            <option value="">Tất cả loại đối tượng</option>
            <option value="Message">Tin nhắn</option>
            <option value="User">Người dùng</option>
            <option value="Conversation">Hội thoại</option>
          </select>
        </div>
      </div>

      {/* Reports List */}
      {loading ? (
        <p style={{ textAlign: "center", color: "#888", padding: 40 }}>Đang tải danh sách báo cáo vi phạm…</p>
      ) : reports.length === 0 ? (
        <p style={{ textAlign: "center", color: "#888", padding: 40 }}>Không có báo cáo nào phù hợp.</p>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          {reports.map((report) => (
            <div
              key={report.id}
              style={{
                border: "1px solid var(--color-border, #e5e7eb)",
                borderRadius: 8,
                padding: 16,
                background: "var(--color-surface, #fff)",
                boxShadow: "0 1px 3px rgba(0,0,0,0.05)",
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 8 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  {getStatusBadge(report.status)}
                  <span style={{ fontSize: "0.85rem", color: "#666" }}>
                    Báo cáo {report.targetType === "User" ? "Người dùng" : report.targetType === "Message" ? "Tin nhắn" : "Hội thoại"}
                  </span>
                  <span style={{ fontSize: "0.85rem", color: "#aaa" }}>•</span>
                  <span style={{ fontSize: "0.85rem", color: "#888" }}>
                    {new Date(report.createdAt).toLocaleString("vi-VN")}
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => setSelectedReport(report)}
                  style={{
                    padding: "4px 12px",
                    borderRadius: 4,
                    background: "#2563eb",
                    color: "#fff",
                    border: "none",
                    fontSize: "0.85rem",
                    cursor: "pointer",
                  }}
                >
                  Xử lý
                </button>
              </div>

              <div style={{ marginBottom: 6 }}>
                <strong>Lý do: </strong>
                <span>{report.reason}</span>
                {report.description && <span style={{ color: "#666" }}> — "{report.description}"</span>}
              </div>

              {report.targetPreview && (
                <div style={{ background: "var(--color-surface-alt, #f9fafb)", padding: "8px 12px", borderRadius: 6, fontSize: "0.9rem", marginBottom: 6 }}>
                  <strong>Nội dung xem trước: </strong>
                  <span>{report.targetPreview}</span>
                </div>
              )}

              {report.moderatorNotes && (
                <div style={{ fontSize: "0.85rem", color: "#4b5563", fontStyle: "italic" }}>
                  Ghi chú KTV: {report.moderatorNotes} ({report.reviewedByName ?? "KTV"})
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Decision Modal */}
      {selectedReport && (
        <div className="confirm-dialog-backdrop" role="dialog" aria-modal="true">
          <div className="confirm-dialog" style={{ maxWidth: 480, width: "100%" }}>
            <h3 style={{ marginTop: 0 }}>Quyết định kiểm duyệt</h3>
            <p style={{ fontSize: "0.9rem", color: "#666", marginBottom: 12 }}>
              Báo cáo ID: <code>{selectedReport.id.substring(0, 8)}</code> ({selectedReport.targetType})
            </p>

            <form onSubmit={handleApplyDecision} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
              <div>
                <label style={{ display: "block", fontSize: "0.85rem", fontWeight: 600, marginBottom: 4 }}>
                  Hành động:
                </label>
                <select
                  value={decisionAction}
                  onChange={(e) => setDecisionAction(e.target.value as any)}
                  disabled={submittingDecision}
                  style={{ width: "100%", padding: "8px 10px", borderRadius: 6, border: "1px solid #ccc" }}
                >
                  <option value="Dismiss">Bác bỏ báo cáo (Không vi phạm)</option>
                  <option value="Warn">Cảnh cáo người dùng</option>
                  {selectedReport.targetType === "Message" && (
                    <option value="DeleteMessage">Thu hồi tin nhắn vi phạm</option>
                  )}
                  <option value="FlagUser">Đánh dấu tài khoản khả nghi</option>
                  <option value="RestrictUser">Tạm khóa tính năng gửi tin nhắn</option>
                </select>
              </div>

              {decisionAction === "RestrictUser" && (
                <div>
                  <label style={{ display: "block", fontSize: "0.85rem", fontWeight: 600, marginBottom: 4 }}>
                    Thời gian hạn chế (phút):
                  </label>
                  <select
                    value={restrictionMinutes}
                    onChange={(e) => setRestrictionMinutes(Number(e.target.value))}
                    disabled={submittingDecision}
                    style={{ width: "100%", padding: "8px 10px", borderRadius: 6, border: "1px solid #ccc" }}
                  >
                    <option value={15}>15 phút</option>
                    <option value={60}>1 giờ (60 phút)</option>
                    <option value={1440}>24 giờ (1 ngày)</option>
                    <option value={10080}>7 ngày</option>
                  </select>
                </div>
              )}

              <div>
                <label style={{ display: "block", fontSize: "0.85rem", fontWeight: 600, marginBottom: 4 }}>
                  Ghi chú của kiểm duyệt viên:
                </label>
                <textarea
                  value={moderatorNotes}
                  onChange={(e) => setModeratorNotes(e.target.value)}
                  disabled={submittingDecision}
                  rows={2}
                  maxLength={500}
                  placeholder="Lý do đưa ra quyết định..."
                  style={{ width: "100%", padding: "8px 10px", borderRadius: 6, border: "1px solid #ccc" }}
                />
              </div>

              <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 8 }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setSelectedReport(null)}
                  disabled={submittingDecision}
                  style={{ padding: "8px 14px", borderRadius: 6, cursor: "pointer" }}
                >
                  Đóng
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={submittingDecision}
                  style={{
                    padding: "8px 14px",
                    borderRadius: 6,
                    background: "#2563eb",
                    color: "#fff",
                    border: "none",
                    cursor: submittingDecision ? "not-allowed" : "pointer",
                  }}
                >
                  {submittingDecision ? "Đang lưu..." : "Xác nhận quyết định"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
