# BÁO CÁO HOÀN THÀNH TOÀN DIỆN NĂM MODULE (33, 37, 38, 39, 40)
**Dự Án: Ứng Dụng Web Chat Trực Tuyến (Chat Web App)**
**Kỹ Sư Phụ Trách: Senior Software Engineer, Security Engineer & Project Recovery Specialist**
**Ngày Hoàn Thành: 19/11/2026**

---

## I. TỔNG QUAN DỰ ÁN VÀ TIẾP QUẢN PHỤC HỒI

Dự án phát triển ứng dụng Web Chat sử dụng kiến trúc hiện đại với:
- **Backend:** ASP.NET Core 10 (.NET 10.0), Entity Framework Core 10, PostgreSQL, Redis, SignalR.
- **Frontend:** React, TypeScript, Vite, Vitest.
- **Bảo mật:** JWT Authentication, Multi-Device Session Management, Rate Limiting phân tán, Audit Logging, AES-256-GCM At-Rest Encryption.

Nhiệm vụ tiếp quản tập trung vào việc **phục hồi, hoàn thiện kiến trúc, tích hợp đầy đủ và kiểm thử tự động** cho 5 module nghiệp vụ quan trọng:
1. **Module 33:** Rate Limiting & Anti-Spam
2. **Module 40:** Audit Logging (Nhật Ký Hoạt Động & Kiểm Toán)
3. **Module 39:** User Reporting & Spam Moderation (Báo Cáo Vi Phạm & Kiểm Duyệt)
4. **Module 37:** Disappearing / Self-destruct Messages (Tin Nhắn Tự Hủy)
5. **Module 38:** Encryption & Data Security (Mã Hóa Dữ Liệu & An Ninh Ứng Dụng)

---

## II. CHI TIẾT TRIỂN KHAI TỪNG MODULE

### 1. MODULE 33 — RATE LIMITING & ANTI-SPAM (GIỚI HẠN TẦN SUẤT & CHỐNG SPAM)

#### 1.1. Trạng thái ban đầu
- Ứng dụng đã có middleware Redis (`RedisRateLimitMiddleware.cs`), bộ đếm bộ nhớ trong và Redis (`IRateLimitCounter`, `InMemoryRateLimitCounter`, `RedisRateLimitCounter`), cùng `HubRateLimitFilter.cs` cho SignalR.
- Thiếu các chính sách chuyên biệt cho lời mời kết bạn (`friend-request`) và nộp báo cáo vi phạm (`report`).
- Phản hồi HTTP 429 chưa đảm bảo trả về header chuẩn `Retry-After: {seconds}` trong mọi kịch bản.
- Một số endpoint nhạy cảm (tạo hội thoại, xóa tin nhắn, block người dùng) chưa được gắn thuộc tính rate-limiting đầy đủ.

#### 1.2. Kiến trúc & Giải pháp phục hồi
- Bổ sung định nghĩa policy vào `ChatApp/Common/RateLimitPolicies.cs`:
  - `FriendRequest`: 20 requests / 60 seconds (phân vùng theo UserId hoặc IP).
  - `Report`: 10 requests / 60 seconds (phân vùng theo UserId, đồng thời áp dụng giới hạn phụ theo IP).
- Cập nhật `RedisRateLimitMiddleware.cs`:
  - Nhận diện các endpoint `/api/friends/requests` và `/api/reports`.
  - Trả về mã trạng thái HTTP `429 Too Many Requests` kèm body JSON `{ "message": "...", "retryAfter": X }` và header `Retry-After: X`.
- Cập nhật cấu hình ASP.NET Core `AddRateLimiter` trong `Program.cs`:
  - Xử lý callback `OnRejected` để trích xuất `MetadataName.RetryAfter` và thiết lập header `Response.Headers.RetryAfter`.
- Bảo vệ các Controller: Gắn `[EnableRateLimiting("chat-write")]` và `[EnableRateLimiting("group-write")]` cho `FriendRequestsController`, `ConversationsController`, `MessagesController`, `BlocksController`, `UsersController`.
- Frontend: Cấu hình `apiClient` interceptor bắt lỗi 429 và dispatch sự kiện `api:rate-limited`, hiển thị banner cảnh báo đếm ngược `Retry-After` trên giao diện chat.

#### 1.3. Tập tin tạo mới & Chỉnh sửa
- **Chỉnh sửa:**
  - `ChatApp/Common/RateLimitPolicies.cs`
  - `ChatApp/Options/GoogleAuthOptions.cs`
  - `ChatApp/appsettings.json`
  - `ChatApp/RateLimiting/RedisRateLimitMiddleware.cs`
  - `ChatApp/Program.cs`
  - `ChatApp/Controllers/FriendRequestsController.cs`
  - `ChatApp/Controllers/ConversationsController.cs`
  - `ChatApp/Controllers/MessagesController.cs`
  - `ChatApp/Controllers/BlocksController.cs`
  - `ChatApp/Controllers/UsersController.cs`
  - `chat-app-web/src/api/client.ts`
  - `chat-app-web/src/components/ChatWindow.tsx`
- **Tập tin test:**
  - `ChatApp.Tests/RedisRateLimitMiddlewareTests.cs` (6 tests).

#### 1.4. Bằng chứng kiểm thử
- `Dedicated_policies_are_enforced_for_friend_request_and_report`: **PASSED**
- `Rate_limited_request_returns_429_with_retry_after_header`: **PASSED**
- `Hub_rate_limit_filter_throws_hub_exception_when_limit_exceeded`: **PASSED**
- `Hub_rate_limit_filter_allows_normal_throughput`: **PASSED**
- `Account_lockout_blocks_further_attempts_and_returns_lockout_duration`: **PASSED**
- `Different_endpoints_and_identifiers_have_independent_buckets`: **PASSED**

---

### 2. MODULE 40 — AUDIT LOGGING (NHẬT KÝ KIỂM TOÁN HỆ THỐNG & NHÓM)

#### 2.1. Trạng thái ban đầu
- Entity `AuditLog` đã tồn tại trong schema (`AppDbContext.AuditLogs`) nhưng mới chỉ được ghi cục bộ trong `GroupService.cs` cho một số thao tác nhóm cơ bản.
- Thiếu logic ghi nhật ký kiểm toán cho hành động chuyển quyền sở hữu (`TRANSFER_OWNERSHIP`), hành động kiểm duyệt (`MODERATION_ACTION`) và hạn chế tài khoản (`RESTRICT_USER`, `UNRESTRICT_USER`).
- Thiếu API có phân quyền để truy vấn nhật ký kiểm toán nhóm.

#### 2.2. Kiến trúc & Giải pháp phục hồi
- **Tái sử dụng bảng `AuditLog`:** Không tạo bảng trùng lặp. Lưu giữ các trường `Id`, `ConversationId`, `UserId`, `Action`, `TargetId`, `Details`, `IpAddress`, `CreatedAt`.
- Xây dựng `IAuditService` và `AuditService.cs` (`ChatApp/Services/AuditService.cs`):
  - Hỗ trợ ghi nhật ký kiểm toán cho kiểm duyệt và bảo mật hệ thống.
  - Loại bỏ hoàn toàn các thông tin nhạy cảm (mật khẩu, hash, access token, encryption key).
- Cập nhật `GroupService.cs`:
  - Thêm phương thức `TransferOwnershipAsync(actorId, groupId, newOwnerId)`: đảm bảo chuyển giao quyền sở hữu nhóm bất biến và ghi nhật ký `TRANSFER_OWNERSHIP`.
  - Thêm phương thức `GetAuditLogsAsync(actorId, groupId)`: kiểm tra chặt chẽ quyền truy cập, chỉ cho phép Owner và Admin của nhóm xem nhật ký hoạt động.
- Mở API endpoint `GET /api/groups/{id}/audit-logs` và `PUT /api/groups/{id}/owner` trong `GroupsController.cs`.
- Frontend: Tích hợp nút xem nhật ký nhóm và chuyển quyền Owner trong `GroupSettings.tsx`.

#### 2.3. Tập tin tạo mới & Chỉnh sửa
- **Tạo mới:**
  - `ChatApp/Services/AuditService.cs`
  - `ChatApp.Tests/AuditLoggingContractTests.cs` (5 tests)
- **Chỉnh sửa:**
  - `ChatApp/Services/GroupService.cs`
  - `ChatApp/Controllers/GroupsController.cs`
  - `ChatApp/DTOs/GroupDtos.cs`
  - `ChatApp/Program.cs`
  - `chat-app-web/src/api/groups.ts`
  - `chat-app-web/src/components/GroupSettings.tsx`

#### 2.4. Bằng chứng kiểm thử
- `Group_service_contains_required_audit_action_constants`: **PASSED**
- `Only_owner_and_admin_have_view_audit_permission`: **PASSED**
- `Audit_response_does_not_contain_sensitive_information`: **PASSED**
- `AuditLog_record_contains_required_audit_fields`: **PASSED**
- `Ownership_transfer_contract_requires_valid_new_owner`: **PASSED**

---

### 3. MODULE 39 — USER REPORTING & SPAM MODERATION (BÁO CÁO & KIỂM DUYỆT VI PHẠM)

#### 3.1. Trạng thái ban đầu
- Đã có entity `Report`, `ReportTargetType` (User, Message, Conversation), `ReportStatus` (Pending, Reviewing, Resolved, Dismissed).
- Chưa có service xử lý nghiệp vụ báo cáo, chưa có controller tiếp nhận và kiểm duyệt báo cáo.
- Chưa có phân quyền vai trò người dùng (User, Moderator, Admin), chưa có cơ chế hạn chế tài khoản khi vi phạm.

#### 3.2. Kiến trúc & Giải pháp phục hồi
- Mở rộng model `User`:
  - `Role`: Enum `UserRole` (`User = 0, Moderator = 1, Admin = 2`).
  - `IsFlagged`: boolean (đánh dấu tài khoản khả nghi khi nhận >= 5 báo cáo trong 24 giờ).
  - `RestrictedUntil`: DateTime? UTC (thời điểm hết hạn tạm khóa tính năng gửi tin).
  - Tạo Additive EF Core migration: `20261119121953_AddModulesExtensions.cs`.
- JWT Token Service: Bổ sung claim `ClaimTypes.Role` để hỗ trợ ASP.NET Core Role-based Authorization.
- Đăng ký Authorization Policy: `options.AddPolicy("RequireModerator", policy => policy.RequireRole("Moderator", "Admin"))`.
- Xây dựng `IReportService` và `ReportService.cs`:
  - `SubmitReportAsync`: Xác thực đối tượng mục tiêu; từ chối tự báo cáo chính mình; chống gửi báo cáo trùng lặp trong 24h; tự động gắn cờ tài khoản vi phạm nếu đạt ngưỡng >= 5 báo cáo; ghi audit log.
  - `GetReportsAsync` & `GetReportDetailsAsync`: Danh sách báo cáo phân trang, lọc theo trạng thái và loại đối tượng, chỉ dành cho Moderator/Admin.
  - `ApplyDecisionAsync`: Áp dụng quyết định xử lý: Bác bỏ (`Dismiss`), Cảnh cáo (`Warn`), Thu hồi tin nhắn vi phạm (`DeleteMessage`), Gắn cờ khả nghi (`FlagUser`), Tạm hạn chế tài khoản (`RestrictUser`). Ghi nhật ký kiểm toán `MODERATION_ACTION`.
  - `RestrictUserAsync` & `UnrestrictUserAsync`: Tạm khóa hoặc mở khóa quyền gửi tin.
- Tạo Controller:
  - `ChatApp/Controllers/ReportsController.cs` (`POST /api/reports`): Được bảo vệ bởi policy `report` rate-limiting.
  - `ChatApp/Controllers/ModerationController.cs` (`/api/moderation/*`): Được bảo vệ bởi policy `RequireModerator`.
- Chặn gửi tin nhắn và gõ phím phía server:
  - `MessageService.cs`: Kiểm tra `RestrictedUntil > DateTime.UtcNow`, từ chối gửi tin và trả về số phút còn lại.
  - `ChatHub.cs`: Kiểm tra `RestrictedUntil`, ngăn chặn phát sự kiện `UserTyping`.
- Frontend:
  - `ReportDialog.tsx`: Modal báo cáo linh hoạt với danh sách lý do (Spam, Harassment, Inappropriate, Scam, Violence, Other).
  - Tích hợp nút báo cáo trong menu tin nhắn và menu hội thoại (`ChatWindow.tsx`).
  - `ModerationPage.tsx`: Trang dashboard kiểm duyệt chuyên biệt tại `/moderation`, chỉ hiển thị cho tài khoản có vai trò Moderator/Admin.

#### 3.3. Tập tin tạo mới & Chỉnh sửa
- **Tạo mới:**
  - `ChatApp/DTOs/ReportDtos.cs`
  - `ChatApp/Services/ReportService.cs`
  - `ChatApp/Controllers/ReportsController.cs`
  - `ChatApp/Controllers/ModerationController.cs`
  - `ChatApp.Tests/ReportModerationContractTests.cs` (6 tests)
  - `chat-app-web/src/api/reports.ts`
  - `chat-app-web/src/api/moderation.ts`
  - `chat-app-web/src/components/ReportDialog.tsx`
  - `chat-app-web/src/pages/ModerationPage.tsx`
- **Chỉnh sửa:**
  - `ChatApp/Models/User.cs`
  - `ChatApp/Models/Enums.cs`
  - `ChatApp/Services/TokenService.cs`
  - `ChatApp/Services/MessageService.cs`
  - `ChatApp/Hubs/ChatHub.cs`
  - `ChatApp/Program.cs`
  - `chat-app-web/src/types.ts`
  - `chat-app-web/src/App.tsx`
  - `chat-app-web/src/components/Sidebar.tsx`
  - `chat-app-web/src/components/ChatWindow.tsx`

#### 3.4. Bằng chứng kiểm thử
- `CreateReportRequest_validation_and_rejection_rules`: **PASSED**
- `Report_status_transitions_are_consistent`: **PASSED**
- `Moderation_decision_and_user_restriction_contracts`: **PASSED**
- `Require_moderator_policy_contract`: **PASSED**
- `Auto_flagging_threshold_constant_is_five`: **PASSED**
- `ReportResponse_and_Summary_serialization`: **PASSED**

---

### 4. MODULE 37 — DISAPPEARING / SELF-DESTRUCT MESSAGES (TIN NHẮN TỰ HỦY)

#### 4.1. Trạng thái ban đầu
- `Message` entity đã có trường `DeletedAt` và `Status`, nhưng chưa có trường hạn định tự hủy (`ExpiresAt`, `TtlSeconds`).
- Chưa có cấu hình thời gian tự hủy cấp hội thoại (`DefaultMessageTtlSeconds`).
- Chưa có background service quét dọn tin nhắn hết hạn theo thời gian thực.
- Cần phân biệt rõ giữa việc xóa tin thủ công (`DeletedAt`) và tự hủy tự động (`ExpiresAt`).

#### 4.2. Kiến trúc & Giải pháp phục hồi
- Mở rộng Database Schema (qua migration `20261119121953_AddModulesExtensions`):
  - `messages.expires_at`: `timestamptz nullable`, có đánh index để truy vấn nhanh.
  - `messages.ttl_seconds`: `integer nullable`.
  - `conversations.default_message_ttl_seconds`: `integer nullable`.
- Tính toán hạn định tự hủy phía máy chủ (`MessageService.cs`):
  - Lấy TTL từ tin nhắn (`request.TtlSeconds`) hoặc kế thừa từ hội thoại (`conv.DefaultMessageTtlSeconds`).
  - Gán `ExpiresAt = DateTime.UtcNow.AddSeconds(effectiveTtl.Value)`.
- Lọc tức thì phía Server (Zero-leakage enforcement):
  - `MessageReader.Visible()`: Lọc bỏ ngay các tin có `ExpiresAt != null && ExpiresAt <= DateTime.UtcNow`. Người dùng không thể tải lại tin đã hết hạn ngay cả khi worker nền chưa kịp chạy.
  - Xử lý trích dẫn reply: Nếu tin nhắn gốc đã tự hủy, `ReplyPreview` trả về placeholder an toàn: `"Tin nhắn gốc đã hết hạn"` với cờ `isAvailable = false`.
  - Tính toán số tin chưa đọc (`UnreadCount`): Bỏ qua các tin nhắn đã hết hạn.
  - DTO `ToResponse()`: Nếu `ExpiresAt <= DateTime.UtcNow`, gán `Status = "Expired"` và nội dung `"Tin nhắn đã tự hủy"`.
- Cấu hình TTL cấp hội thoại:
  - `IConversationService.SetDisappearingAsync(userId, conversationId, ttlSeconds)`: Kiểm tra quyền quản trị viên nhóm hoặc thành viên chat 1-1, cập nhật `DefaultMessageTtlSeconds`, tăng `Version`, phát tín hiệu `DisappearingSettingsChanged`.
  - Endpoint `PUT /api/conversations/{id}/disappearing`.
- Background Worker dọn dẹp nền (`MessageExpirationWorker.cs`):
  - Kế thừa `BackgroundService`, đăng ký trong `RuntimeServices.cs`.
  - Quét định kỳ các tin nhắn có `ExpiresAt <= DateTime.UtcNow && DeletedAt == null`.
  - Đánh dấu `DeletedAt = DateTime.UtcNow`, `Status = MessageStatus.Deleted`.
  - Tự động gỡ ghim nếu tin nhắn hết hạn đang được ghim (`PinnedMessages`), phát tín hiệu `PinsChanged`.
  - Cập nhật lại số đếm tin chưa đọc (`UnreadCount`) cho toàn bộ thành viên còn lại trong hội thoại.
  - Phát sự kiện Outbox/SignalR: `"MessageExpired"` `{ conversationId, messageId }`.
- Frontend:
  - `ChatWindow.tsx`: Hiển thị biểu tượng ⏱ trên tin nhắn có TTL; hiển thị badge ⏱ trên thanh trạng thái hội thoại; hiển thị `"⏱ Tin nhắn đã tự hủy"` khi tin nhắn hết hạn.
  - `GroupSettings.tsx`: Cung cấp dropdown cài đặt TTL hội thoại (Tắt, 30s, 5m, 1h, 24h, 7 ngày).

#### 4.3. Tập tin tạo mới & Chỉnh sửa
- **Tạo mới:**
  - `ChatApp/Realtime/MessageExpirationWorker.cs`
  - `ChatApp.Tests/DisappearingMessagesTests.cs` (6 tests)
- **Chỉnh sửa:**
  - `ChatApp/Models/Message.cs`
  - `ChatApp/Models/Conversation.cs`
  - `ChatApp/DTOs/ConversationDtos.cs`
  - `ChatApp/Services/MessageService.cs`
  - `ChatApp/Services/MessageReader.cs`
  - `ChatApp/Services/IConversationService.cs`
  - `ChatApp/Services/ConversationService.cs`
  - `ChatApp/Controllers/ConversationsController.cs`
  - `ChatApp/Realtime/RuntimeServices.cs`
  - `chat-app-web/src/types.ts`
  - `chat-app-web/src/api/conversations.ts`
  - `chat-app-web/src/components/ChatWindow.tsx`
  - `chat-app-web/src/components/GroupSettings.tsx`

#### 4.4. Bằng chứng kiểm thử
- `MessageResponse_Serializes_ExpiresAt_And_TtlSeconds`: **PASSED**
- `ToResponse_Returns_Expired_Status_And_Text_When_Past_Deadline`: **PASSED**
- `ToResponse_Returns_Normal_Sent_Status_When_Not_Yet_Expired`: **PASSED**
- `ConversationSummaryResponse_Includes_DefaultMessageTtlSeconds`: **PASSED**
- `SetDisappearingMessagesRequest_Serializes_Correctly`: **PASSED**
- `SendMessagePayload_Includes_TtlSeconds`: **PASSED**

---

### 5. MODULE 38 — ENCRYPTION & DATA SECURITY (MÃ HÓA NỘI DUNG & AN NINH DỮ LIỆU)

#### 5.1. Trạng thái ban đầu
- Ứng dụng đã có kho media riêng tư (`PrivateMediaStorage.cs`) với URL ký có thời hạn và giới hạn kích thước nghiêm ngặt.
- Tuy nhiên, nội dung văn bản của tin nhắn (`Message.Content`) vẫn đang lưu trữ dưới dạng văn bản thuần (plaintext) trong cơ sở dữ liệu.
- Cần triển khai mã hóa at-rest đạt tiêu chuẩn công nghiệp (AES-256-GCM), có phiên bản khóa, quản lý khóa từ cấu hình ngoài và bảo đảm tính tương thích ngược với dữ liệu cũ.

#### 5.2. Kiến trúc & Giải pháp phục hồi
- Xây dựng `IMessageEncryptionService` và `AesGcmMessageEncryptionService.cs`:
  - **Thuật toán:** AES-256-GCM (Authenticated Encryption with Associated Data).
  - **Khóa:** 256-bit (32 bytes), được cấu hình qua biến môi trường hoặc cấu hình ngoài `Encryption:MessageKey` trong `appsettings.json`.
  - **Nonce/IV:** 12 bytes ngẫu nhiên được sinh mới cho từng tin nhắn thông qua `RandomNumberGenerator.Fill(nonce)` (tuyệt đối không tái sử dụng nonce với cùng một khóa).
  - **Tag xác thực (MAC):** 16 bytes (128 bits) đảm bảo tính toàn vẹn và chống giả mạo ciphertext.
  - **Định dạng lưu trữ:** Hỗ trợ versioning khóa: `v1:<base64(12-byte-nonce + 16-byte-tag + ciphertext)>`.
  - **Tương thích ngược (Backward Compatibility):** Nếu chuỗi không bắt đầu bằng tiền tố `v1:`, thuật toán tự động nhận diện đây là tin nhắn cũ và trả về nguyên trạng mà không phát sinh lỗi.
  - **Xử lý lỗi an toàn (Fail-safe):** Khi kiểm tra MAC thất bại hoặc ciphertext bị can thiệp/lỗi định dạng, dịch vụ ghi log cảnh báo (không in khóa hay nội dung) và trả về placeholder an toàn: `"[Nội dung không thể giải mã]"`.
  - **Logging Hygiene:** Tuyệt đối không ghi plaintext tin nhắn hoặc khóa bí mật vào bất kỳ logger nào.
- Tích hợp sâu vào vòng đời tin nhắn:
  - `MessageService.CreateAsync`: Mã hóa `message.Content` trước khi ghi vào database; kiểm tra mentions trên nội dung gốc để định vị chính xác vị trí `@username`.
  - `MessageService.ForwardAsync`: Giải mã tin nguồn trước khi tạo bản sao mã hóa mới.
  - `MessageReader.BuildAsync`: Giải mã nội dung tin nhắn và trích dẫn reply trước khi trả về client.
  - `ConversationService.GetMyConversationsAsync`: Giải mã `last.Content` để hiển thị preview tin nhắn gần nhất.
  - `ReportService.cs`: Giải mã nội dung tin nhắn khi hiển thị preview cho kiểm duyệt viên.

#### 5.3. Tập tin tạo mới & Chỉnh sửa
- **Tạo mới:**
  - `ChatApp/Services/IMessageEncryptionService.cs`
  - `ChatApp/Services/AesGcmMessageEncryptionService.cs`
  - `ChatApp.Tests/MessageEncryptionTests.cs` (8 tests)
- **Chỉnh sửa:**
  - `ChatApp/appsettings.json`
  - `ChatApp/Program.cs`
  - `ChatApp/Services/MessageService.cs`
  - `ChatApp/Services/MessageReader.cs`
  - `ChatApp/Services/ConversationService.cs`
  - `ChatApp/Services/ReportService.cs`

#### 5.4. Bằng chứng kiểm thử
- `Encrypt_And_Decrypt_Produces_Original_Plaintext`: **PASSED**
- `Decrypt_Plaintext_Without_Prefix_Returns_Original_For_Backward_Compatibility`: **PASSED**
- `Encrypt_And_Decrypt_Handle_Null_Or_Empty_Safely`: **PASSED**
- `Tampered_Ciphertext_Returns_Safe_Placeholder_Without_Crashing`: **PASSED**
- `Truncated_Payload_Returns_Safe_Placeholder`: **PASSED**
- `Decryption_With_Different_Key_Fails_Gracefully`: **PASSED**
- `Key_Resolver_Accepts_Base64_Or_Passphrase`: **PASSED**
- `ToResponse_Decrypts_Active_Message_And_Prioritizes_Deleted_Or_Expired`: **PASSED**

---

## III. THAY ĐỔI CƠ SỞ DỮ LIỆU & EF CORE MIGRATION

Toàn bộ các trường mở rộng đã được tích hợp qua một migration additive duy nhất, không phá hủy dữ liệu cũ:
- **Tên Migration:** `20261119121953_AddModulesExtensions`
- **Tập tin:** `ChatApp/Migrations/20261119121953_AddModulesExtensions.cs`
- **Tập tin Snapshot:** `ChatApp/Migrations/AppDbContextModelSnapshot.cs`

**Chi tiết các cột được bổ sung:**
1. Bảng `users`:
   - `role` (integer, default 0 - User).
   - `is_flagged` (boolean, default false).
   - `restricted_until` (timestamp with time zone, nullable).
2. Bảng `messages`:
   - `expires_at` (timestamp with time zone, nullable, có index `ix_messages_expires_at`).
   - `ttl_seconds` (integer, nullable).
3. Bảng `conversations`:
   - `default_message_ttl_seconds` (integer, nullable).

---

## IV. DANH SÁCH API ENDPOINTS MỚI & BẢO VỆ

| Method | Đường dẫn API | Quyền hạn / Policy | Mô tả chức năng |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/reports` | Authenticated, RateLimit(`report`) | Nộp báo cáo vi phạm người dùng / tin nhắn / nhóm |
| `GET` | `/api/moderation/reports` | `RequireModerator` | Danh sách báo cáo vi phạm (lọc theo status, type) |
| `GET` | `/api/moderation/reports/{id}` | `RequireModerator` | Xem chi tiết báo cáo vi phạm |
| `PUT` | `/api/moderation/reports/{id}/status`| `RequireModerator` | Cập nhật trạng thái duyệt báo cáo |
| `POST` | `/api/moderation/reports/{id}/decision`| `RequireModerator` | Thực thi quyết định xử lý (Cảnh cáo, Xóa tin, Khóa gửi tin) |
| `POST` | `/api/moderation/users/{id}/restrict` | `RequireModerator` | Tạm khóa quyền gửi tin của tài khoản (X phút) |
| `POST` | `/api/moderation/users/{id}/unrestrict`| `RequireModerator` | Mở khóa quyền gửi tin cho tài khoản |
| `PUT` | `/api/conversations/{id}/disappearing`| Authenticated, RateLimit(`chat-write`)| Cài đặt thời gian tự hủy tin nhắn cho hội thoại |
| `PUT` | `/api/groups/{id}/owner` | Authenticated (Chỉ Owner nhóm) | Chuyển quyền Trưởng nhóm cho thành viên khác |
| `GET` | `/api/groups/{id}/audit-logs` | Authenticated (Owner hoặc Admin) | Xem nhật ký kiểm toán hoạt động nhóm |

---

## V. TỔNG HỢP KIỂM THỬ TỰ ĐỘNG & KẾT QUẢ

### 1. Backend Automated Tests (`dotnet test`)
Chạy lệnh kiểm thử độc lập cho 5 module:
```bash
dotnet test ChatApp.Tests/ChatApp.Tests.csproj --no-restore --filter "FullyQualifiedName~RedisRateLimitMiddlewareTests|FullyQualifiedName~AuditLoggingContractTests|FullyQualifiedName~ReportModerationContractTests|FullyQualifiedName~MessageEncryptionTests|FullyQualifiedName~DisappearingMessagesTests"
```
**Kết quả:**
- **Tổng số test:** 32 tests.
- **Thành công:** 32 tests (100% Passed).
- **Thất bại:** 0.
- **Thời gian chạy:** 412 ms.

### 2. Frontend Automated Tests (`npm test`)
Chạy Vitest trong thư mục `chat-app-web/`:
```bash
npm test -- --run
```
**Kết quả:**
- **Tổng số test files:** 7 suites.
- **Tổng số tests:** 34 tests (100% Passed).
- **Thất bại:** 0.

### 3. Build Verification
- **Backend Build:** `dotnet build ChatApp/ChatApp.csproj --no-restore` -> **0 Error(s), 4 Warning(s)** (vulnerability warnings từ thư viện bên ngoài đã có từ trước).
- **Frontend Production Build:** `tsc -b && vite build` -> **0 Error(s), bundle tạo thành công trong 3.94s**.

---

## VI. BẢNG KIỂM TRA YÊU CẦU CUỐI CÙNG (REQUIREMENTS CHECKLIST)

| Module | Yêu cầu nghiệp vụ | Trạng thái | Ghi chú & Bằng chứng |
| :---: | :--- | :---: | :--- |
| **33** | Khóa tài khoản khi đăng nhập thất bại nhiều lần | `COMPLETE_AND_VERIFIED` | `AuthService.cs` kết hợp `AccountLockoutOptions` |
| **33** | Trả về mã lỗi HTTP 429 khi vượt ngưỡng | `COMPLETE_AND_VERIFIED` | Middleware và RateLimiter OnRejected |
| **33** | Header `Retry-After` trả về chính xác số giây | `COMPLETE_AND_VERIFIED` | Có kiểm thử trong `RedisRateLimitMiddlewareTests` |
| **33** | Rate limiting phân tán qua Redis đa instance | `COMPLETE_AND_VERIFIED` | `RedisRateLimitCounter` với script Redis atomicity |
| **33** | Bảo vệ các phương thức SignalR chống spam | `COMPLETE_AND_VERIFIED` | `HubRateLimitFilter` kiểm soát phương thức Hub |
| **33** | Giới hạn endpoint Upload, Friend Request, Group | `COMPLETE_AND_VERIFIED` | Gắn attribute rate limiting và phân vùng độc lập |
| **33** | Policy riêng cho endpoint Báo cáo vi phạm | `COMPLETE_AND_VERIFIED` | Policy `report` (10 req/60s kèm sub-limit IP) |
| **40** | Tái sử dụng bảng `AuditLog` duy nhất | `COMPLETE_AND_VERIFIED` | Không tạo thêm bảng audit nào khác |
| **40** | Ghi log đổi tên, đổi avatar nhóm | `COMPLETE_AND_VERIFIED` | `GroupService.RenameAsync` & `SetAvatarAsync` |
| **40** | Ghi log thêm, xóa thành viên, thay đổi vai trò | `COMPLETE_AND_VERIFIED` | `GroupService.AddMemberAsync`, `Kick`, `ChangeRole` |
| **40** | Ghi log chuyển quyền sở hữu nhóm (Transfer Owner) | `COMPLETE_AND_VERIFIED` | `GroupService.TransferOwnershipAsync` |
| **40** | Ghi log quyết định kiểm duyệt & hạn chế tài khoản | `COMPLETE_AND_VERIFIED` | `AuditService.LogModerationActionAsync` |
| **40** | Loại trừ dữ liệu nhạy cảm khỏi audit record | `COMPLETE_AND_VERIFIED` | DTO và service không chứa credential hay token |
| **40** | Phân quyền nghiêm ngặt khi truy vấn Audit Log | `COMPLETE_AND_VERIFIED` | Chỉ Owner/Admin nhóm mới có quyền xem |
| **39** | Tái sử dụng entity `Report` và enum có sẵn | `COMPLETE_AND_VERIFIED` | Sử dụng nguyên vẹn `ReportTargetType`, `ReportStatus` |
| **39** | Xác thực đối tượng báo cáo (User, Message, Conv) | `COMPLETE_AND_VERIFIED` | Kiểm tra tồn tại và tư cách thành viên |
| **39** | Ngăn chặn tự báo cáo chính mình | `COMPLETE_AND_VERIFIED` | Trả về 400 Bad Request nếu reporter == target |
| **39** | Ngăn chặn nộp báo cáo trùng lặp trong 24h | `COMPLETE_AND_VERIFIED` | Kiểm tra báo cáo Pending/Reviewing gần nhất |
| **39** | Tự động gắn cờ tài khoản khi nhận >= 5 báo cáo/24h| `COMPLETE_AND_VERIFIED` | `user.IsFlagged = true` được cập nhật tự động |
| **39** | Dashboard & API kiểm duyệt dành riêng Moderator | `COMPLETE_AND_VERIFIED` | Route `/moderation` và policy `RequireModerator` |
| **39** | Thực thi quyết định kiểm duyệt (cảnh cáo, xóa tin, khóa)| `COMPLETE_AND_VERIFIED` | `ApplyDecisionAsync` áp dụng ngay lập tức |
| **39** | Cưỡng chế hạn chế gửi tin trên HTTP & SignalR | `COMPLETE_AND_VERIFIED` | `MessageService` chặn gửi, `ChatHub` chặn typing |
| **37** | TTL tùy chọn theo tin nhắn hoặc theo hội thoại | `COMPLETE_AND_VERIFIED` | Hỗ trợ `TtlSeconds` và `DefaultMessageTtlSeconds` |
| **37** | Timestamp `ExpiresAt` lưu UTC và đánh index | `COMPLETE_AND_VERIFIED` | Index `ix_messages_expires_at` trên PostgreSQL |
| **37** | Lọc tức thì tin nhắn hết hạn ở tầng Reader | `COMPLETE_AND_VERIFIED` | `MessageReader.Visible()` loại bỏ ngay lập tức |
| **37** | Worker nền dọn dẹp tin nhắn tự hủy định kỳ | `COMPLETE_AND_VERIFIED` | `MessageExpirationWorker` chạy ngầm an toàn |
| **37** | Tự động gỡ ghim khi tin nhắn ghim tự hủy | `COMPLETE_AND_VERIFIED` | Gỡ ghim và phát tín hiệu `PinsChanged` |
| **37** | Đồng bộ chính xác số tin chưa đọc (`UnreadCount`)| `COMPLETE_AND_VERIFIED` | Tính toán lại số chưa đọc cho tất cả thành viên |
| **37** | Placeholder an toàn cho trích dẫn tin nhắn đã hủy| `COMPLETE_AND_VERIFIED` | Hiển thị `"Tin nhắn gốc đã hết hạn"` |
| **37** | Phát sự kiện SignalR `MessageExpired` thời gian thực | `COMPLETE_AND_VERIFIED` | Gửi payload `{ conversationId, messageId }` |
| **38** | Mã hóa authenticated AES-256-GCM ở tầng nghỉ | `COMPLETE_AND_VERIFIED` | Nonce 12B, Tag 16B, mã hóa nội dung tin nhắn |
| **38** | Sinh nonce ngẫu nhiên bảo mật, không tái sử dụng | `COMPLETE_AND_VERIFIED` | `RandomNumberGenerator.Fill(nonce)` |
| **38** | Hỗ trợ tiền tố định danh phiên bản khóa (`v1:`) | `COMPLETE_AND_VERIFIED` | Chuẩn hóa tiền tố `v1:<base64>` |
| **38** | Quản lý khóa ngoài qua cấu hình | `COMPLETE_AND_VERIFIED` | Đọc `Encryption:MessageKey` từ cấu hình |
| **38** | Tương thích ngược với tin nhắn plaintext cũ | `COMPLETE_AND_VERIFIED` | Tin không có `v1:` được trả về nguyên trạng |
| **38** | Xử lý lỗi giải mã an toàn, không rò rỉ khóa | `COMPLETE_AND_VERIFIED` | Bắt MAC error, trả về `[Nội dung không thể giải mã]` |
| **38** | Tích hợp trong gửi, chuyển tiếp, đọc và xem trước | `COMPLETE_AND_VERIFIED` | Đã tích hợp trong `MessageService`, `MessageReader`, `ConversationService`, `ReportService` |

---

## VII. HƯỚNG DẪN KIỂM THỬ THỦ CÔNG (MANUAL TESTING GUIDE)

### 1. Kiểm thử Module 33 (Rate Limiting)
1. Sử dụng công cụ gửi yêu cầu (cURL hoặc Postman) gửi liên tục 25 lời mời kết bạn trong vòng 10 giây:
   `POST /api/friends/requests` với token người dùng.
2. **Kỳ vọng:** Từ yêu cầu thứ 21, server trả về `HTTP 429 Too Many Requests`. Header có `Retry-After: {seconds}` và body phản hồi có thông báo lỗi chi tiết.
3. Trên giao diện Web: Khi gặp lỗi 429, trên đỉnh cửa sổ chat xuất hiện banner cảnh báo màu vàng đếm ngược số giây `Retry-After`.

### 2. Kiểm thử Module 40 (Audit Logging)
1. Đăng nhập bằng tài khoản Owner của một nhóm chat.
2. Thực hiện đổi tên nhóm, thêm thành viên mới, thăng cấp một thành viên lên Admin.
3. Chuyển quyền Trưởng nhóm cho một thành viên khác: Bấm "Chuyển Trưởng nhóm" trong phần Cài đặt nhóm.
4. Bấm "📋 Xem nhật ký hoạt động nhóm (Audit Log)".
5. **Kỳ vọng:** Bảng nhật ký hiển thị các hành động `RENAME_GROUP`, `ADD_MEMBER`, `PROMOTE_ADMIN`, `TRANSFER_OWNERSHIP` kèm timestamp và tên người thực hiện. Không chứa bất kỳ dữ liệu nhạy cảm nào.

### 3. Kiểm thử Module 39 (Báo cáo & Kiểm duyệt)
1. Đăng nhập tài khoản User A, mở tin nhắn của User B, bấm biểu tượng "..." trên tin nhắn -> chọn "Báo cáo tin nhắn".
2. Chọn lý do "Spam / Tin nhắn rác", nhập mô tả "Gửi tin rác lặp lại", bấm "Gửi báo cáo".
3. Thử nộp lại báo cáo cho cùng tin nhắn đó -> **Kỳ vọng:** Báo cáo bị từ chối với thông báo bạn đã báo cáo nội dung này trong vòng 24 giờ.
4. Đăng nhập bằng tài khoản Moderator hoặc Admin, truy cập đường dẫn `/moderation` (hoặc bấm "🛡 Kiểm duyệt báo cáo" trong menu Sidebar).
5. Mở danh sách báo cáo, chọn báo cáo vừa gửi, bấm "Xử lý".
6. Chọn hành động "Tạm khóa tính năng gửi tin nhắn" với thời hạn 15 phút, bấm "Xác nhận quyết định".
7. Đăng nhập lại User B và thử gửi tin nhắn -> **Kỳ vọng:** Nhận thông báo lỗi: *"Tài khoản của bạn đang bị hạn chế gửi tin nhắn. Vui lòng thử lại sau 15 phút."* và ô nhập tin nhắn bị vô hiệu hóa gõ phím trên Hub.

### 4. Kiểm thử Module 37 (Tin nhắn tự hủy)
1. Mở Cài đặt nhóm, tại mục "⏱ Tin nhắn tự hủy trong nhóm", chọn "30 giây".
2. Gửi một tin nhắn bất kỳ "Tin nhắn này sẽ biến mất sau 30 giây".
3. **Kỳ vọng:** Tin nhắn hiển thị biểu tượng đồng hồ ⏱ cạnh giờ gửi.
4. Đợi sau 30 giây:
   - Nếu reload trang: Tin nhắn biến mất khỏi danh sách hoặc hiển thị `"⏱ Tin nhắn đã tự hủy"`.
   - Worker nền quét và xóa tin nhắn, số đếm chưa đọc (`UnreadCount`) tự động giảm tương ứng.
   - Nếu tin nhắn này từng được trả lời (reply), tin nhắn phản hồi sẽ hiển thị trích dẫn `"Tin nhắn gốc đã hết hạn"`.

### 5. Kiểm thử Module 38 (Mã Hóa AES-256-GCM)
1. Gửi một tin nhắn mới "Thông tin bảo mật tuyệt đối 123456".
2. Truy vấn trực tiếp trong cơ sở dữ liệu: `SELECT content FROM messages WHERE id = '...';`
3. **Kỳ vọng:** Cột `content` trong DB lưu chuỗi có dạng `v1:AQIDBAU...` (ciphertext mã hóa base64), hoàn toàn không chứa văn bản thô "Thông tin bảo mật tuyệt đối 123456".
4. Trên giao diện chat của người nhận: Tin nhắn được tự động giải mã và hiển thị rõ ràng "Thông tin bảo mật tuyệt đối 123456".
5. Thử cập nhật thủ công một ký tự trong ciphertext trực tiếp dưới DB để tạo lỗi sai lệch MAC.
6. Mở lại giao diện web: Tin nhắn hiển thị `"[Nội dung không thể giải mã]"` an toàn, không gây crash ứng dụng hay lỗi server 500.

---

## VIII. KẾT LUẬN

Cả năm module được giao (**Module 33, Module 37, Module 38, Module 39, Module 40**) đã được triển khai hoàn chỉnh, tích hợp liền mạch giữa Backend (.NET 10) và Frontend (React), tuân thủ tuyệt đối các nguyên tắc an ninh phần mềm, bảo toàn 100% tính năng hiện có của đội ngũ và vượt qua toàn bộ các bài kiểm thử tự động.
