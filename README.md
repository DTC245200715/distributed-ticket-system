# Hệ Thống Hỗ Trợ Yêu Cầu Khách Hàng (Customer Support Ticket System)

> **Học phần:** Phát triển ứng dụng phân tán  
> **Đề tài số 07:** Hệ thống hỗ trợ yêu cầu khách hàng  
> **Nền tảng:** .NET 10 (ASP.NET Core Minimal API, Background Worker), RabbitMQ, SignalR, PostgreSQL, Nginx, Docker Compose.

---

## 1. Giới thiệu tổng quan

Hệ thống tiếp nhận và xử lý yêu cầu hỗ trợ khách hàng theo kiến trúc hướng sự kiện (Event-Driven Microservices):
- **Support API:** Tiếp nhận yêu cầu (REST API), lưu trữ cơ sở dữ liệu và đẩy sự kiện vào RabbitMQ Message Broker.
- **Assignment Worker:** Lắng nghe hàng đợi `ticket.creation.queue`, tự động gán nhóm và nhân viên xử lý theo quy tắc ưu tiên (Priority) kết hợp vòng tròn (Round-Robin), cập nhật trạng thái `Assigned` và phát sự kiện `ticket.status.changed`.
- **Notification Service:** Tiêu thụ sự kiện thay đổi trạng thái từ RabbitMQ và đẩy thông báo tức thời (realtime) đến trình duyệt khách hàng thông qua **SignalR Hub** theo nhóm người dùng (`userId`).
- **Web Client:** Ứng dụng web thuần (HTML5/CSS3/Vanilla JS) kết nối REST API và nhận thông báo thời gian thực qua SignalR JS client.

---

## 2. Kiến trúc luồng dữ liệu

```text
Client Web (HTML/JS)
  |-- (1) REST API (POST /tickets) ---------> Support API
  |                                                |-- (2) Ghi DB (Status: New) -> PostgreSQL
  |                                                |-- (3) Publish (ticket.created) -> RabbitMQ
  |                                                                                      |
  |                                                        Assignment Worker <-----------+
  |                                                            |-- (4) Gán nhân viên, đổi Status: Assigned -> PostgreSQL
  |                                                            |-- (5) Publish (ticket.status.changed) -> RabbitMQ
  |                                                                                                          |
  |                                                        Notification Service <----------------------------+
  |<-- (6) Push Realtime (SignalR WebSocket) ------------------+
```

---

## 3. Cấu trúc thư mục

- `/docs`: Tài liệu thiết kế hệ thống, sơ đồ kiến trúc, báo cáo kiểm thử và bộ câu hỏi bảo vệ.
- `/src/Contracts`: Định nghĩa các sự kiện (Event) và giao ước dữ liệu dùng chung.
- `/src/SupportApi`: Service Web Minimal API tiếp nhận và quản lý ticket.
- `/src/AssignmentWorker`: Background Service tự động phân loại và phân công ticket.
- `/src/NotificationService`: Service nhận sự kiện và phân phối thông báo realtime qua SignalR.
- `/src/Client`: Giao diện Web tĩnh phục vụ bởi Nginx.
- `docker-compose.yml`: Script khởi chạy toàn bộ hệ thống bằng 1 lệnh duy nhất.
- `.env.example`: Biến môi trường mẫu.

---

## 4. Hướng dẫn khởi chạy nhanh (Xem chi tiết tại docs/design.md)

*(Nội dung sẽ được cập nhật hoàn chỉnh trong các giai đoạn tiếp theo)*
