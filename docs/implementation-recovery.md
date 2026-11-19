# Kế Hoạch Phục Hồi & Báo Cáo Trạng Thái Thực Hiện (Implementation Recovery Report)

## 1. Tổng quan & Trạng thái phục hồi (Executive Summary)

Dự án đã tiếp tục thực hiện và hoàn tất 5 module được giao trên Chat Web Application hiện tại:
- **Module 33: Rate Limiting & Anti-Spam**
- **Module 40: Audit Logging**
- **Module 39: User Reporting & Spam Moderation**
- **Module 37: Disappearing / Self-destruct Messages**
- **Module 38: Encryption & Data Security**

Toàn bộ quá trình phục hồi tuân thủ nguyên tắc:
1. Không bắt đầu dự án mới, không viết lại mã nguồn hiện có.
2. Bảo toàn toàn bộ các tính năng đã hoàn thành trước đó (Authentication, JWT, Multi-device Sessions, Friends, Group Management, Message Ordering, Message Pagination, Unread Count, SignalR).
3. Tái sử dụng tối đa các bảng cơ sở dữ liệu (`AuditLog`, `Report`, `Message`, `Conversation`, `User`) mà không tạo bảng trùng lặp.
4. Mở rộng schema theo hướng phi phá hủy (Additive EF Core migration `20261119121953_AddModulesExtensions`).
5. Xây dựng dịch vụ backend, controller HTTP, realtime SignalR events và giao diện frontend tương ứng.
6. Viết đầy đủ unit tests và contract tests, đạt 100% pass trên cả Backend và Frontend.

---

## 2. Bảng phân loại trạng thái từng Module (Module Status Classification)

| Module | Tên Module | Phân loại trạng thái | Ghi chú tóm tắt |
| :--- | :--- | :--- | :--- |
| **Module 33** | Rate Limiting & Anti-Spam | **COMPLETE_AND_VERIFIED** | Đã cấu hình các policy: `login`, `register`, `friend-request`, `report`, `chat-write`, `group-write`, `get-messages`. Bổ sung header `Retry-After` khi trả về mã HTTP 429. Bộ đếm phân tán Redis bảo vệ chống spam đa phiên bản. SignalR methods được bảo vệ bởi `HubRateLimitFilter`. Frontend hiển thị banner cảnh báo khi gặp lỗi 429. Đã kiểm thử và xác minh. |
| **Module 40** | Audit Logging | **COMPLETE_AND_VERIFIED** | Tái sử dụng bảng `AuditLog`. Hoàn thiện ghi log máy chủ cho: đổi tên nhóm, đổi avatar nhóm, thêm/xóa thành viên, thăng/hạ quyền admin, chuyển quyền trưởng nhóm (`TRANSFER_OWNERSHIP`), quyết định kiểm duyệt (`MODERATION_ACTION`) và hạn chế tài khoản (`RESTRICT_USER`, `UNRESTRICT_USER`). Cung cấp API `GET /api/groups/{id}/audit-logs` có phân quyền. Đã kiểm thử và xác minh. |
| **Module 39** | Reporting & Moderation | **COMPLETE_AND_VERIFIED** | Bổ sung enum `UserRole` (User, Moderator, Admin), `IsFlagged`, `RestrictedUntil` trên `User`. Tái sử dụng entity `Report`. Cung cấp API báo cáo (`POST /api/reports`) chống tự báo cáo và báo cáo trùng lặp, tự động gắn cờ tài khoản vi phạm (>= 5 báo cáo/24h). API kiểm duyệt (`/api/moderation/*`) yêu cầu policy `RequireModerator`. Chặn gửi tin nhắn và gõ phím khi bị hạn chế. Frontend có `ReportDialog`, `ModerationPage`, link kiểm duyệt trong Sidebar. Đã kiểm thử và xác minh. |
| **Module 37** | Disappearing Messages | **COMPLETE_AND_VERIFIED** | Hỗ trợ TTL tùy chọn theo tin nhắn (`TtlSeconds`) và theo hội thoại (`DefaultMessageTtlSeconds`). Server tự động tính `ExpiresAt` (UTC). `MessageReader` và `MessageService` tự động lọc bỏ tin nhắn hết hạn ngay lập tức khỏi lịch sử và số đếm chưa đọc (`UnreadCount`). Background service `MessageExpirationWorker` định kỳ quét dọn tin nhắn hết hạn, gỡ ghim, đồng bộ số chưa đọc và phát tín hiệu SignalR `MessageExpired`. Trích dẫn tin nhắn đã tự hủy hiển thị placeholder an toàn `"Tin nhắn gốc đã hết hạn"`. Frontend hiển thị thời gian còn lại, biểu tượng ⏱ và menu cài đặt TTL trong nhóm. Đã kiểm thử và xác minh. |
| **Module 38** | Encryption & Security | **COMPLETE_AND_VERIFIED** | Triển khai mã hóa authenticated encryption AES-256-GCM ở tầng nghỉ (at-rest) cho nội dung tin nhắn (`IMessageEncryptionService`, `AesGcmMessageEncryptionService`). Nonce ngẫu nhiên 12 bytes, tag xác thực 16 bytes. Định dạng phiên bản key `v1:<base64>`. Đọc khóa an toàn từ cấu hình (`Encryption:MessageKey`). Tương thích ngược tuyệt đối với tin nhắn cũ dạng văn bản thô (plaintext). Không in khóa hoặc nội dung thô ra log. Bắt lỗi xác thực MAC an toàn trả về placeholder `"[Nội dung không thể giải mã]"`. Tích hợp trong `MessageService`, `MessageReader`, `ConversationService` và `ReportService`. Đã kiểm thử và xác minh. |

---

## 3. Ma trận truy vết yêu cầu (Requirements Traceability Matrix)

| Yêu cầu | Tập tin nguồn liên quan | Trạng thái | Phần việc hoàn thành | Bằng chứng kiểm thử |
| :--- | :--- | :--- | :--- | :--- |
| **M33: Rate limiting Friend Requests** | `FriendRequestsController.cs`, `RedisRateLimitMiddleware.cs`, `RateLimitPolicies.cs` | COMPLETE_AND_VERIFIED | Áp dụng policy `friend-request` (20 req/60s), phân vùng theo User ID hoặc IP. | `RedisRateLimitMiddlewareTests.Dedicated_policies_are_enforced_for_friend_request_and_report` (Passed) |
| **M33: Rate limiting Report Submission** | `ReportsController.cs`, `RedisRateLimitMiddleware.cs`, `RateLimitPolicies.cs` | COMPLETE_AND_VERIFIED | Áp dụng policy `report` (10 req/60s), giới hạn phụ theo IP. | `RedisRateLimitMiddlewareTests.Dedicated_policies_are_enforced_for_friend_request_and_report` (Passed) |
| **M33: HTTP 429 & Retry-After Header** | `RedisRateLimitMiddleware.cs`, `Program.cs` | COMPLETE_AND_VERIFIED | Thêm header `Retry-After: {seconds}` trong cả middleware Redis và ASP.NET RateLimiter OnRejected. | `RedisRateLimitMiddlewareTests.Rate_limited_request_returns_429_with_retry_after_header` (Passed) |
| **M33: Hub SignalR Method Protection** | `HubRateLimitFilter.cs`, `SignalRServiceExtensions.cs` | COMPLETE_AND_VERIFIED | Bảo vệ phương thức SignalR tránh spam tin nhắn và typing. | `RedisRateLimitMiddlewareTests.Hub_rate_limit_filter_throws_hub_exception_when_limit_exceeded` (Passed) |
| **M40: Nhóm chuyển quyền sở hữu (Transfer Ownership)** | `GroupService.cs`, `GroupsController.cs` | COMPLETE_AND_VERIFIED | Cung cấp hàm `TransferOwnershipAsync`, ghi log `TRANSFER_OWNERSHIP`, bảo toàn tính bất biến 1 Owner duy nhất. | `AuditLoggingContractTests.Group_service_contains_required_audit_action_constants` (Passed) |
| **M40: Truy vấn Audit Logs có phân quyền** | `GroupService.cs`, `AuditService.cs`, `GroupsController.cs` | COMPLETE_AND_VERIFIED | `GET /api/groups/{id}/audit-logs` chỉ cho phép Owner và Admin nhóm truy cập. | `AuditLoggingContractTests.Only_owner_and_admin_have_view_audit_permission` (Passed) |
| **M40: Audit Log loại trừ dữ liệu nhạy cảm** | `AuditService.cs`, `GroupDtos.cs` | COMPLETE_AND_VERIFIED | Kiểm tra DTO đầu ra không chứa token, mật khẩu, khóa mã hóa hay nội dung nhạy cảm. | `AuditLoggingContractTests.Audit_response_does_not_contain_sensitive_information` (Passed) |
| **M39: Nộp báo cáo và chống báo cáo trùng/tự báo cáo** | `ReportService.cs`, `ReportsController.cs` | COMPLETE_AND_VERIFIED | Kiểm tra đối tượng hợp lệ (User, Message, Conversation), từ chối tự báo cáo, chống spam trùng lặp trong 24h. | `ReportModerationContractTests.CreateReportRequest_validation_and_rejection_rules` (Passed) |
| **M39: Kiểm duyệt và hạn chế tài khoản vi phạm** | `ReportService.cs`, `ModerationController.cs`, `MessageService.cs` | COMPLETE_AND_VERIFIED | Quyết định kiểm duyệt (Dismiss, Warn, DeleteMessage, FlagUser, RestrictUser). Chặn gửi tin nhắn khi `RestrictedUntil > UtcNow`. | `ReportModerationContractTests.Moderation_decision_and_user_restriction_contracts` (Passed) |
| **M37: TTL & Server-side Expiration** | `MessageService.cs`, `MessageReader.cs`, `ConversationDtos.cs` | COMPLETE_AND_VERIFIED | Tính toán `ExpiresAt`, lọc tin nhắn tự hủy ngay lập tức trong `Visible()`, hiển thị placeholder trích dẫn `"Tin nhắn gốc đã hết hạn"`. | `DisappearingMessagesTests.ToResponse_Returns_Expired_Status_And_Text_When_Past_Deadline` (Passed) |
| **M37: Background Expiration Worker & Realtime** | `MessageExpirationWorker.cs`, `RuntimeServices.cs` | COMPLETE_AND_VERIFIED | Worker chạy ngầm định kỳ quét dọn tin nhắn hết hạn, gỡ ghim, phát SignalR event `MessageExpired`, cập nhật `UnreadCount`. | `DisappearingMessagesTests.MessageResponse_Serializes_ExpiresAt_And_TtlSeconds` (Passed) |
| **M38: AES-256-GCM Encryption At-Rest** | `IMessageEncryptionService.cs`, `AesGcmMessageEncryptionService.cs` | COMPLETE_AND_VERIFIED | Mã hóa nội dung tin nhắn với nonce 12B, tag 16B, prefix `v1:`, đọc key từ cấu hình `Encryption:MessageKey`. | `MessageEncryptionTests.Encrypt_And_Decrypt_Produces_Original_Plaintext` (Passed) |
| **M38: An toàn giải mã & Tương thích ngược** | `AesGcmMessageEncryptionService.cs`, `MessageService.cs` | COMPLETE_AND_VERIFIED | Tin nhắn cũ không có tiền tố `v1:` được giữ nguyên; lỗi MAC trả về `[Nội dung không thể giải mã]`, không leak key vào log. | `MessageEncryptionTests.Tampered_Ciphertext_Returns_Safe_Placeholder_Without_Crashing` (Passed) |
| **Frontend: Giao diện Báo cáo & Kiểm duyệt** | `ReportDialog.tsx`, `ModerationPage.tsx`, `reports.ts`, `moderation.ts` | COMPLETE_AND_VERIFIED | Hộp thoại báo cáo tin nhắn/người dùng/nhóm; Dashboard kiểm duyệt cho Moderator/Admin; Route `/moderation`. | Frontend build `tsc -b && vite build` và 34 tests Vitest (Passed) |
| **Frontend: Hiển thị Tin nhắn tự hủy & Rate-Limit** | `ChatWindow.tsx`, `GroupSettings.tsx` | COMPLETE_AND_VERIFIED | Biểu tượng ⏱ trên tin nhắn tự hủy, menu cài đặt TTL nhóm, banner cảnh báo 429 Retry-After, xem nhật ký nhóm. | Frontend build và 34 tests Vitest (Passed) |

---

## 4. Bằng chứng kiểm thử tổng thể (Overall Test Evidence)

### Backend Tests (`ChatApp.Tests`)
- **Bộ kiểm thử 5 Module:**
  - `RedisRateLimitMiddlewareTests`: 6/6 passed.
  - `AuditLoggingContractTests`: 5/5 passed.
  - `ReportModerationContractTests`: 6/6 passed.
  - `DisappearingMessagesTests`: 6/6 passed.
  - `MessageEncryptionTests`: 8/8 passed.
  - **Tổng cộng 32/32 unit tests pass 100%.**
- **Toàn bộ unit tests backend (không phụ thuộc external Postgres live):** 75/75 passed.

### Frontend Tests (`chat-app-web`)
- `tests/send-queue.test.ts`: 11 passed.
- `tests/auth-session.test.ts`: 3 passed.
- `tests/reconciliation.test.ts`: 2 passed.
- `tests/chat-state.test.ts`: 7 passed.
- `tests/reconnect.test.ts`: 4 passed.
- `tests/message-features.test.ts`: 4 passed.
- `tests/reply-composer.test.ts`: 3 passed.
- **Tổng cộng 7 test suites, 34/34 tests pass 100%.**
- **Frontend Production Build (`vite build`):** Succeeded trong 3.94s, không có bất kỳ cảnh báo hay lỗi kiểu nào.
