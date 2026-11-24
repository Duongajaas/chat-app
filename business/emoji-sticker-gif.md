**Emoji, Sticker và GIF — báo cáo triển khai ngày 16/11/2026.**

Triển khai trên React 18/TypeScript, ASP.NET Core 10, PostgreSQL/EF Core và SignalR hiện có. Emoji dùng chung tin văn bản Unicode; Sticker và GIF là tin riêng có loại `Sticker`/`Gif` và trường `expression`. Các luồng gửi tiếp tục dùng HTTP, hàng đợi retry, `clientMessageId`, đối soát và SignalR outbox của dự án.

**Các chức năng trong code.**

- Bảng chọn thống nhất ba tab, hỗ trợ bàn phím, Escape, click ngoài, phục hồi focus và chế độ sáng/tối. Popover tự đo chiều cao composer; trên mobile giữ ô nhập và nút gửi nằm ngoài bảng chọn.
- Emoji Unicode từ `emoji-picker-react`, phân nhóm, tìm kiếm, chọn màu da, chèn tại con trỏ hoặc thay vùng đang chọn. Chèn nhiều lần giữ nội dung nháp và tuân thủ giới hạn 4.000 đơn vị UTF-16 của backend. Lịch sử gần đây/thường dùng lưu riêng cho tài khoản trong trình duyệt.
- Ba bộ sticker gốc: Mèo đáng yêu, Gửi yêu thương, Cùng ăn mừng; tổng cộng sáu sticker, trong đó hai sticker có bản động. Asset SVG tĩnh/động đi kèm source; catalog hỗ trợ thêm PNG/WebP/GIF. Có tìm kiếm, xem trước, gửi một lần nhấn, gần đây và yêu thích theo tài khoản/trình duyệt.
- GIF gọi API GIPHY thật bằng Fetch SDK: trending, tìm kiếm debounce 350 ms, phân trang/cuộn tải thêm, thumbnail tĩnh, xem trước, tải lỗi/thử lại và ghi nguồn. Không có dữ liệu GIF mẫu trong chức năng runtime.
- Nội dung biểu cảm giữ tỷ lệ, tải khi gần vùng nhìn thấy, hỗ trợ giảm chuyển động và phát lại. Mỗi media có fallback khi lỗi; lỗi tải một media không làm hỏng khung chat.
- Lịch sử, reply, forward, thứ tự sequence, nhận realtime, nhiều phiên đăng nhập, retry không trùng, xóa phía mình và thu hồi tiếp tục dùng cơ chế sẵn có. Preview danh sách hội thoại hiển thị Sticker hoặc GIF tương ứng.

**Danh sách hàm và chức năng.**

| Hàm/component | File | Chức năng |
|---|---|---|
| `ExpressionPicker` | `chat-app-web/src/components/ExpressionPicker.tsx` | Quản lý ba tab, focus, phím điều hướng, đóng bảng và lịch sử theo tài khoản. |
| `EmojiPanel` | `chat-app-web/src/components/EmojiPanel.tsx` | Hiển thị bộ chọn Unicode, tìm kiếm, phân nhóm, gần đây/thường dùng. |
| `StickerPicker` | `chat-app-web/src/components/StickerPicker.tsx` | Tải catalog, lọc pack, tìm kiếm, xem trước, gửi và chọn yêu thích. |
| `GifPicker` | `chat-app-web/src/components/GifPicker.tsx` | Tìm kiếm/trending GIPHY và giao diện lựa chọn GIF. |
| `loadPage` | `GifPicker.tsx` | Tải từng trang, kiểm soát request đang chạy, loại kết quả cũ và xử lý retry. |
| `send` | `GifPicker.tsx` | Kiểm tra URL media hiển thị, gửi lựa chọn và sự kiện tương tác GIPHY. |
| `analyticsUrl`, `trackGiphyAction` | `GifPicker.tsx` | Đọc URL analytics và chỉ gọi endpoint analytics GIPHY được cho phép. |
| `GifMessage` | `chat-app-web/src/components/GifMessage.tsx` | Render Sticker/GIF; tra metadata GIF theo ID khi xuất hiện, phát lại và fallback. |
| `useReducedMotion` | `chat-app-web/src/hooks/useReducedMotion.ts` | Theo dõi tùy chọn giảm chuyển động của hệ điều hành. |
| `handleEmojiSelect` | `chat-app-web/src/components/ChatWindow.tsx` | Chèn emoji vào bản nháp và phục hồi con trỏ. |
| `rememberComposerSelection` | `ChatWindow.tsx` | Ghi lại vùng chọn trước khi focus chuyển sang bảng biểu cảm. |
| `handleSendExpression` | `ChatWindow.tsx` | Gửi Sticker/GIF riêng, giữ nháp văn bản và đính kèm reply nếu đang chọn. |
| `closeExpressionPicker` | `ChatWindow.tsx` | Đóng bảng và trả focus về nút đã mở bảng khi phù hợp. |
| `insertEmojiAtSelection` | `chat-app-web/src/utils/expressions.ts` | Chèn/thay thế Unicode theo vùng chọn; từ chối nếu vượt giới hạn. |
| `readExpressionHistory`, `saveExpressionHistory` | `expressions.ts` | Đọc/ghi lịch sử theo tài khoản, xử lý dữ liệu hỏng hoặc storage không khả dụng. |
| `rememberEmoji` | `expressions.ts` | Cập nhật số lần dùng và thời điểm dùng emoji. |
| `rememberSticker` | `expressions.ts` | Cập nhật danh sách sticker gần đây, không trùng. |
| `toggleFavoriteSticker` | `expressions.ts` | Thêm hoặc bỏ sticker yêu thích. |
| `isSafeExpressionUrl` | `expressions.ts` | Cho phép asset sticker nội bộ và HTTPS media trên domain GIPHY hợp lệ. |
| `expressionsApi.stickerPacks` | `chat-app-web/src/api/expressions.ts` | Lấy catalog qua API xác thực; hỗ trợ hủy request. |
| `handleSend` | `chat-app-web/src/pages/ChatPage.tsx` | Tạo tin optimistic và đưa ID biểu cảm vào hàng đợi gửi/retry hiện có. |
| `GetStickerPacks` | `ChatApp/Controllers/ExpressionsController.cs` | Trả catalog sticker cho người đã đăng nhập, có rate limit. |
| `ExpressionCatalog.IsValid` | `ChatApp/Media/ExpressionCatalog.cs` | Kiểm tra cấu hình pack, ID không trùng, URL nội bộ và kích thước trước khi chạy. |
| `ExpressionCatalog.Normalize` | `ExpressionCatalog.cs` | Đối chiếu Sticker ID/pack với catalog; kiểm tra định dạng GIF ID và chuẩn hóa provider. |
| `ExpressionCatalog.Serialize`, `Read` | `ExpressionCatalog.cs` | Chuyển metadata biểu cảm sang/từ JSON lưu trong database. |
| `SendMessageAsync`, `CreateAsync` | `ChatApp/Services/MessageService.cs` | Kiểm tra quyền, block, payload; lưu tin, chống trùng và phát sự kiện hiện có. |
| `ForwardAsync` | `MessageService.cs` | Tạo tin chuyển tiếp độc lập, giữ metadata biểu cảm. |
| `ToResponse` | `MessageService.cs` | Trả metadata khi tin còn khả dụng; bỏ metadata khi thu hồi. |
| `Hash`, `ReconcileAsync` | `ChatApp/Services/MessageReconciliation.cs` | Đưa loại/ID/pack vào định danh retry; giữ hash văn bản cũ tương thích. |
| `BuildAsync` | `ChatApp/Services/MessageReader.cs` | Trả lịch sử/preview phù hợp quyền đọc của từng người. |
| `SummariesAsync` | `ChatApp/Services/ConversationService.cs` | Tạo preview hội thoại cho Sticker/GIF. |
| `ExpressionMessages.Up` | `ChatApp/Data/ExpressionMessagesMigration.cs` | Thêm cột `messages.expression_json` kiểu JSONB, nullable. |
| `EnsureRecentChatSchema.Up` | `ChatApp/Data/EnsureRecentChatSchemaMigration.cs` | Bổ sung schema/index còn thiếu của các model hiện có để database mới chạy được luồng chat. |

**File đã thêm hoặc sửa trong phần tích hợp.**

| Nhóm | File |
|---|---|
| Component và media frontend | `ExpressionPicker.tsx`, `EmojiPanel.tsx`, `StickerPicker.tsx`, `GifPicker.tsx`, `GifMessage.tsx`, `public/stickers/*.svg` |
| Tích hợp frontend | `ChatWindow.tsx`, `ConversationList.tsx`, `MessageTools.tsx`, `ChatPage.tsx`, `types.ts`, `sendQueue.ts`, `chatStore.ts`, `index.css` |
| Tiện ích frontend | `api/expressions.ts`, `utils/expressions.ts`, `hooks/useReducedMotion.ts`; giữ tương thích tiện ích `utils/giphy.ts` của phần GIF cũ |
| Backend mới | `Controllers/ExpressionsController.cs`, `DTOs/ExpressionDtos.cs`, `Media/ExpressionCatalog.cs`, hai migration trong `Data/` |
| Backend mở rộng | `Models/Message.cs`, `Data/AppDbContext.cs`, `Migrations/AppDbContextModelSnapshot.cs`, `DTOs/ConversationDtos.cs`, `DTOs/ReconcileDtos.cs`, `Controllers/MessagesController.cs`, `Program.cs`, bốn service Message/Reader/Reconciliation/Conversation |
| Test | `tests/expressions.test.ts`, `e2e/chat.spec.ts`, `ExpressionContractTests.cs`, `ExpressionMigrationTests.cs`, `RealtimeIntegrationTests.cs` |
| Cấu hình/dependency | `package.json`, `package-lock.json`, `.env.example`, `security-headers.conf`, `Docker-compose.yml`, `ChatApp/.gitignore` |

`ChatApp/.gitignore` đã bỏ quy tắc ẩn toàn bộ thư mục Migrations. Hai migration nền `20261015135725_SyncCurrentModel` và `20261101130000_RepairRefreshTokenSchema` cùng file Designer đã có trên máy nhưng trước đó bị ignore; cần đưa chúng vào cùng thay đổi để bản clone có đúng bộ migration đã được kiểm thử. Nội dung hai migration nền được giữ như trên máy.

Dependency mới: `emoji-picker-react` 5.1.4. Tiếp tục dùng `@giphy/js-fetch-api`; bỏ `@giphy/react-components` vì giao diện grid mới cần kiểm soát thumbnail và giảm chuyển động. Axios và tiện ích GIPHY đã được cập nhật trong khoảng phiên bản tương thích để xử lý cảnh báo audit production.

**API và dữ liệu.**

API mới: `GET /api/expressions/sticker-packs`, yêu cầu JWT/session hợp lệ và dùng chính sách `media-read` hiện có. Gửi qua endpoint cũ:

```json
{
  "clientMessageId": "UUID-do-client-sinh",
  "content": "",
  "expression": { "kind": "sticker", "id": "cat-happy", "packId": "cats" }
}
```

Với GIF, gửi `expression: { "kind": "gif", "id": "ID-do-GIPHY-tra-ve" }`. Client không gửi URL để backend lưu. Sticker lấy metadata/URL từ catalog server. GIF lưu provider và ID; trình duyệt lấy metadata hiện tại qua API chính thức khi hiển thị. Backend kiểm tra loại và định dạng GIF ID; tình trạng còn tồn tại được kiểm tra khi client resolve ID. Nếu provider xóa GIF hoặc mạng/API lỗi, UI có thông báo và nút tải lại.

Theo [GIPHY Best Practices](https://developers.giphy.com/docs/api/#best-practices), API và media được gọi trực tiếp từ client, không proxy và không lưu lâu dài URL/asset của nhà cung cấp. Bảng chọn hiển thị Powered by GIPHY và giữ nguyên URL nhận từ provider. Người dùng đã chấp thuận dùng khóa client công khai qua `VITE_GIPHY_API_KEY`. Các khóa backend khác không được đưa vào biến `VITE_*`.

**Migration và tác động triển khai.**

- `20261115090000_EnsureRecentChatSchema`: sửa chênh lệch có sẵn giữa snapshot và các migration thực tế. Thêm bảng/cột/index còn thiếu, bổ sung default cho cột `file_url` legacy để upload riêng tư tiếp tục hoạt động. Nếu chưa có sequence, backfill theo `created_at, id` rồi tạo identity cho tin mới. Kiểm thử chạy lại migration hai lần giữ nguyên ID, sequence và nội dung tin.
- `20261116090000_ExpressionMessages`: thêm `expression_json` nullable JSONB. Tin cũ không cần chuyển đổi dữ liệu biểu cảm. GIF dạng URL văn bản của bản cũ vẫn có thể hiển thị qua bộ kiểm tra URL cũ.
- Hai migration mới không xóa bảng hay cột trong `Down`, vì các schema này có thể đã tồn tại và chứa dữ liệu. Với database lớn chưa có sequence, bước backfill có thể khóa bảng trong thời gian chạy; cần xem SQL trước triển khai.
- Chưa áp dụng migration vào database thật của người dùng. Kiểm thử sử dụng PostgreSQL 17 riêng trên localhost, với database test tạo/xóa tự động.

**Cấu hình và chạy.**

Để dùng GIF thật khi phát triển, thêm khóa client hợp lệ vào `chat-app-web/.env` rồi khởi động lại Vite:

```dotenv
VITE_GIPHY_API_KEY=YOUR_PUBLIC_GIPHY_CLIENT_KEY
```

Với Docker, điền `GIPHY_API_KEY` trong `.env.production`; Compose truyền nó vào build arg `VITE_GIPHY_API_KEY`. Cần build lại web khi đổi khóa. Không đưa khóa thật vào file `.example` hoặc commit `.env`.

Backend dùng `ConnectionStrings__DefaultConnection` theo cấu hình sẵn có. Sau khi xem xét migration với database định triển khai, các lệnh chạy từ gốc repo:

```powershell
dotnet ef migrations script --idempotent --project .\ChatApp --output .\ops\expressions-migration.sql
dotnet ef database update --project .\ChatApp
dotnet run --project .\ChatApp
```

Trong terminal frontend:

```powershell
cd chat-app-web
npm.cmd ci
npm.cmd run dev
```

Thêm sticker pack bằng cấu hình `Expressions:StickerPacks` trong cấu hình backend. Mỗi pack có `id`, `name`, `stickers`; mỗi sticker có `kind: sticker`, `provider: local`, `id`, `name`, `packId`, `url`, `width`, `height`, `fileType`, `animated` và `thumbnailUrl` tĩnh nếu có chuyển động. Đặt asset tương ứng vào `chat-app-web/public/stickers/`. Tên asset gồm chữ thường, số, dấu gạch ngang; định dạng SVG/WebP/PNG/GIF. Backend kiểm tra catalog khi khởi động. Không cần sửa logic gửi tin để thêm pack.

**Kết quả kiểm thử và giới hạn.**

| Kiểm tra | Kết quả |
|---|---|
| `npm.cmd run build` | Qua TypeScript strict và build Vite production; bảng emoji và các picker được tách thành chunk tải sau. |
| `npm.cmd test` | 50/50 test qua, gồm Unicode/vùng chọn/giới hạn, lịch sử từng tài khoản, URL an toàn và dedupe/thu hồi. |
| Playwright với Edge headless | 52/52 test qua: desktop/mobile, emoji con trỏ, sticker preview/yêu thích, reduced motion, GIF retry/lịch sử, cùng hồi quy chat/ảnh/file/nhóm/gọi điện. |
| Backend/PostgreSQL | 124/124 test trong bộ lọc được chọn qua, gồm hai tài khoản HTTP + hai kết nối SignalR thật, lịch sử/phân trang/retry/thu hồi và migration chạy lặp. |
| `dotnet ef migrations has-pending-model-changes --project ChatApp --no-build` | Model và snapshot khớp. |
| `npm.cmd audit --omit=dev --audit-level=high` | Không còn lỗ hổng được báo trong dependency production tại thời điểm kiểm tra. |

Lệnh backend đã chạy với `CHATAPP_TEST_POSTGRES` trỏ cluster test riêng:

```powershell
dotnet test ChatApp.Tests/ChatApp.Tests.csproj --no-restore --filter 'FullyQualifiedName!~Redis&FullyQualifiedName!~Group_rejoin_on_second_api&FullyQualifiedName!~Expired_presence&FullyQualifiedName!~GroupMigrationTests'
```

Các test Redis chưa chạy do không có Redis test server. Test upgrade lịch sử `GroupMigrationTests` hiện tham chiếu `20260908155557_ProductionReliability`, một migration không có trong source trên máy; lần chạy rộng ban đầu báo lỗi này. Không coi toàn bộ test backend là đã qua. NuGet vẫn báo advisory cho một số dependency có sẵn như SignalR, MessagePack và PKCS; cần xử lý trong công việc cập nhật dependency backend.

Playwright kiểm tra giao diện với API fixture; bài backend kiểm tra thật việc lưu PostgreSQL và phát SignalR giữa các tài khoản. Máy chưa có khóa GIPHY hợp lệ: GIF đã kiểm tra luồng tích hợp, request, lỗi và render bằng phản hồi API mô phỏng, nhưng chưa nghiệm thu tìm kiếm/trending với GIPHY thật. Tính năng runtime dùng endpoint thật khi được cấu hình; chưa thể tuyên bố hoàn tất toàn bộ tiêu chí nghiệm thu GIF live.

Dự án chưa có script lint riêng; đã chạy TypeScript strict, build, test và kiểm tra dấu conflict/khoảng trắng. Ảnh chụp kiểm thử mobile: `chat-app-web/test-results/expression-picker-mobile.png` và `expression-picker-mobile-dark.png`.
