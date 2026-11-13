# Đề tài 07 - Hệ thống hỗ trợ yêu cầu khách hàng

## 1. Mô tả đề tài

Xây dựng hệ thống tiếp nhận và xử lý các yêu cầu hỗ trợ của khách hàng. Yêu cầu mới được phân phối đến Worker để phân loại hoặc gán nhóm xử lý, đồng thời các thay đổi trạng thái được gửi đến người dùng theo thời gian thực.

Kiến trúc tối thiểu của hệ thống:

```text
Client -> Support API -> RabbitMQ -> Assignment Worker -> Database
             ^                              |
             | SignalR                      | Event
             +------ Notification Service <-+
```

## 2. Chức năng chính

1. Tạo yêu cầu hỗ trợ mới.
2. Tra cứu danh sách và chi tiết yêu cầu.
3. Đưa yêu cầu mới vào queue để xử lý phân loại.
4. Worker gán nhóm hoặc nhân viên xử lý theo quy tắc đơn giản.
5. Cập nhật trạng thái `New`, `Assigned`, `InProgress`, `Resolved`.
6. Phát sự kiện khi trạng thái hoặc người xử lý thay đổi.
7. Gửi thông báo realtime cho client liên quan.
8. Lưu lịch sử thay đổi của yêu cầu.

## 3. API tối thiểu

```text
POST  /tickets
GET   /tickets
GET   /tickets/{id}
PATCH /tickets/{id}/status
GET   /users/{userId}/tickets
```

Dữ liệu tối thiểu gồm người gửi, nội dung yêu cầu, mức ưu tiên, người hoặc nhóm xử lý và trạng thái.
