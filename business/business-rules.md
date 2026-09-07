# Chat Web App — Toàn bộ Business Rules & Bài toán kỹ thuật

## 1. Authentication  (ok)

Rule

Một tài khoản đăng nhập được nhiều thiết bị (PC, Laptop, Phone) → cho phép.

DB: `refresh_tokens`, `device_tokens`.

Logout chỉ xóa refresh token của thiết bị hiện tại, không xóa toàn bộ.

Đổi password → thường logout toàn bộ thiết bị (xóa hết refresh token).

JWT hết hạn → Client dùng Refresh Token để lấy JWT mới.

## 2. Session & Device Management (ok)

Lưu `device_id`, `device_name`, `last_active`, `ip`, `location(optional)`.

User xem được danh sách thiết bị đang đăng nhập, có thể remote-logout thiết bị khác (chỉ xóa token của thiết bị đó).

## 3. User Presence

Một user mở nhiều kết nối (Chrome, Edge, Phone) → 3 socket.

Redis lưu `user:1 → [socket1, socket2, socket3]`.

Disconnect 1 socket → chỉ remove socket đó, còn socket khác → user vẫn online.

Chỉ khi socket count = 0 → offline.

Heartbeat/ping-pong: client gửi ping định kỳ, server timeout sau N giây không nhận ping → coi như disconnect.

## 4. Online Status & Last Seen

Hiển thị "Online" hay "Last seen"? Tùy business.

Privacy setting → cho phép ẩn Last Seen.

Nếu bị block → không thấy online status của nhau.

## 5. Friend (ok)

Không gửi request cho chính mình.

A → B đang Pending → không tạo request mới.

A block B → không gửi request được.

Đã là bạn → không gửi request lại.

## 6. Block

A block B → không chat, không gọi, không thấy online, không gửi request, không push notification.

Invite group khi bị block? Tùy business.

Conversation cũ có xóa không? → Không, chỉ không gửi thêm message được (giống Messenger).

## 7. Conversation

**Direct conversation**: chỉ tồn tại 1 conversation duy nhất giữa 2 user (A chat B trước, B chat A sau vẫn dùng chung 1 conversation, không tạo mới).

**Delete conversation**: không xóa DB, chỉ hidden với user đó.

**Leave group**: member rời → không nhận message mới, nhưng vẫn xem được history cũ.

**Owner leave group**: owner mới = admin đầu tiên hoặc member join sớm nhất (tùy rule).

**Group invite link**: có thời hạn, giới hạn số lượt dùng, revoke được; join có cần admin approve không → tùy business.

## 8. Group Permission & Role

Member không đổi được tên nhóm, admin đổi được.

Member không kick được admin. Owner kick được admin.

Admin không kick được owner.

Nên định nghĩa rõ ma trận quyền: ai đổi tên, ai ghim tin, ai xóa tin người khác, ai thêm/xóa admin — tránh if-else rải rác trong code.

## 9. Message — Gửi & Lưu

User gửi → mất mạng → có lưu DB không?

Flow: Client hiển thị "Pending" → server save DB thành công → gửi ACK → client đổi "Sending" → "Sent". Nếu timeout → client retry.

## 10. Duplicate Message

Network lag khiến client gửi 2 lần → server insert 2 record là sai.

Rule: client gửi kèm `client_message_id` (UUID); server nếu đã tồn tại ID này thì không insert lại (idempotency).

Áp dụng idempotency tương tự cho các API quan trọng khác (kết bạn, tạo group, upload) — không chỉ riêng message.

## 11. Edit Message

Cho edit trong khoảng thời gian giới hạn (vd 15 phút), sau đó không sửa được.

Message đã bị xóa (deleted) thì không edit được.

## 12. Delete Message

2 loại: Delete for me / Delete for everyone.

Delete for everyone có giới hạn thời gian (5 phút / 15 phút / 24h — tùy business).

Receiver đã đọc rồi vẫn cho xóa (giống Messenger).

## 13. Reply Message

Nếu message gốc bị xóa → reply hiển thị "Original message unavailable".

## 14. Forward Message

Forward tạo message mới độc lập, có field `forwarded_from_id`, không copy nội dung gắn chặt vào bản gốc — nếu bản gốc bị xóa sau đó, message forward vẫn còn.

Không forward được tin nhắn từ conversation đã bị block.

## 15. Reaction (Like/Emoji)

Mỗi user chỉ 1 reaction/message. Đổi reaction → update record, không tạo mới. Bỏ reaction → xóa record, không lưu lịch sử.

## 16. Mention (@tag)

Mention trong group → notification riêng, ưu tiên cao hơn tin nhắn thường.

User bị mention nhưng đã rời group → không gửi notification.

## 17. Pin Message

Giới hạn số lượng pin/conversation (vd tối đa 3–5).

Ai được pin: admin/owner trong group; cả 2 bên trong direct chat (tùy business).

## 18. Attachment

Ảnh → preview. File → download. Video → thumbnail.

Upload lỗi → không cho gửi message kèm file lỗi.

## 19. Upload Flow

Không upload file qua WebSocket/SignalR.

Flow đúng: Upload → Storage (S3/Blob) → lấy URL → gửi kèm Message.

## 20. Voice Message / Sticker / GIF

Voice message giới hạn thời lượng, hiển thị waveform, không auto-download để tiết kiệm băng thông.

Sticker/GIF không lưu file trong DB, chỉ lưu reference/URL.

## 21. Schedule Message & Draft

Schedule message: lưu trạng thái `scheduled`, có job quét gửi đúng giờ, cho phép hủy trước giờ gửi.

Draft: lưu theo (`user_id`, `conversation_id`), không đồng bộ real-time cho người khác thấy.

## 22. Read Receipt

A gửi, B offline → chưa tính là đã đọc.

B online, mở conversation → mới tính là "Read", server update trạng thái.

Nhận qua notification (chưa mở app) → không tính là đã đọc.

## 23. Typing Indicator

Không lưu DB. Debounce (vd 3 giây không gõ tiếp → gửi "Typing stop").

## 24. Search

Theo conversation, toàn hệ thống, theo file, theo người gửi.

Nếu có end-to-end encryption thì search phía server sẽ bị hạn chế (server không đọc được nội dung để index).

## 25. Notification

Receiver online → không push. Receiver offline → push.

Muted conversation → không push. Bị block → không push.

Mention vẫn ưu tiên push kể cả khi mute (tùy business).

## 26. Message Ordering (ok)

Client gửi 1,2,3 nhưng server nhận 2,1,3 (do network) → hiển thị sao?

Rule: sắp xếp theo 'sequence' được thêm vào bảng message bản sau này.

## 27. Message Pagination & History Loading (ok)

Không load toàn bộ lịch sử cùng lúc.

Dùng cursor-based pagination (theo `message_id`/`created_at`), tránh OFFSET vì chậm với bảng lớn.

Load thêm khi scroll lên đầu danh sách (infinite scroll ngược).

## 28. Unread Count (ok)

1000 message → COUNT(*) mỗi lần là rất chậm.

Rule: lưu sẵn `unread_count` trong bảng `conversation_members`, tăng/giảm theo sự kiện thay vì đếm lại.

## 29. Concurrent Editing

PC và Phone cùng login. PC delete message, Phone edit message cùng lúc.

Rule: server luôn là nguồn dữ liệu duy nhất (source of truth), kiểm tra trạng thái mới nhất trước khi xử lý mỗi action.

## 30. Multi-device Sync

Mọi action (read, delete, edit, react) phải broadcast tới **tất cả session của chính user đó**, không chỉ tới người nhận — để PC và Phone luôn đồng bộ trạng thái.

Không lưu state riêng theo device, chỉ lưu theo user + conversation.

## 31. Call (1-1)

A gọi, B đang bận cuộc gọi khác → Busy.

A gọi, B offline → Push Notification.

Không bắt máy sau 30s → Missed call.

Caller cancel → popup ở máy receiver tự biến mất.

Mất mạng giữa cuộc gọi → reconnect. Có cơ chế call timeout.

## 32. Group Call

Ai tạo room? → Server tạo, không phải client.

Host leave → call tiếp tục hay end? Business quyết định.

## 33. Rate Limit

Spam message (vd >1000 msg/s) → block.

Spam login sai nhiều lần → lock account.

Spam upload → giới hạn dung lượng/số lượng theo thời gian.

## 34. Realtime Scaling (WebSocket/SignalR)

Nhiều server instance: User A connect Server 1, User B connect Server 2 → Server 1 làm sao gửi message cho B?

Rule: dùng Redis Pub/Sub hoặc backplane để broadcast giữa các node.

Sticky session không bắt buộc nếu có backplane, nhưng cần thiết nếu server giữ state in-memory.

## 35. Database Sharding / Partitioning cho Message

Bảng message tăng cực nhanh (hàng tỷ record).

Rule: partition theo `conversation_id` hoặc theo thời gian (`created_at` theo tháng).

Message cũ (> N tháng) chuyển sang cold storage/table riêng (archive).

## 36. Data Retention & Xóa tài khoản

User xóa tài khoản → message đã gửi vẫn giữ, hiển thị "Người dùng đã bị xóa" thay vì xóa cứng (tránh vỡ history của người khác).

Có yêu cầu pháp lý (kiểu GDPR) đòi xóa vĩnh viễn → cần API riêng xử lý theo yêu cầu.

## 37. Disappearing / Self-destruct Message

Set TTL cho message hoặc cả conversation.

Background job quét và xóa message hết hạn (không cần realtime đúng khoảnh khắc hết hạn).

## 38. Encryption

Data at rest: encrypt các field nhạy cảm (nội dung tin nhắn, số điện thoại...).

End-to-end encryption (nếu cần, kiểu Secret Conversation): server không đọc được nội dung → ảnh hưởng tới search (mục 24).

## 39. Report / Spam User

Report → không tự động block, chỉ tạo ticket cho moderation team xử lý.

Nhiều report trong thời gian ngắn → tự động flag account, hạn chế tính năng (rate limit gửi tin, tạm khóa).

## 40. Audit Log

Lưu lại: ai đổi tên nhóm, ai kick member, ai thêm member, ai đổi avatar, ai chuyển owner.

Không chỉ để debug mà còn hỗ trợ truy vết khi có khiếu nại.
