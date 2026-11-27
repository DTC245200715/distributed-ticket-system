# Hệ Thống Hỗ Trợ Yêu Cầu Khách Hàng (Customer Support Ticket System)

> **Học phần:** Phát triển ứng dụng phân tán  
> **Đề tài số 07:** Hệ thống hỗ trợ yêu cầu khách hàng  
> **Nền tảng:** .NET 10 (ASP.NET Core Minimal API, Background Worker), RabbitMQ, SignalR, PostgreSQL, Nginx, Docker Compose.

---

## 1. Giới thiệu tổng quan

Hệ thống tiếp nhận và xử lý yêu cầu hỗ trợ khách hàng theo kiến trúc hướng sự kiện (Event-Driven Microservices):
- **Support API:** Tiếp nhận yêu cầu (REST API), lưu trữ cơ sở dữ liệu PostgreSQL và đẩy sự kiện vào RabbitMQ Message Broker.
- **Assignment Worker:** Lắng nghe hàng đợi `ticket.creation.queue`, tự động gán nhóm và nhân viên xử lý theo quy tắc ưu tiên (Priority) kết hợp vòng tròn (Round-Robin), cập nhật trạng thái `Assigned` trong Transaction (kèm kiểm soát Idempotency & Optimistic Concurrency) và phát sự kiện `ticket.status.changed`.
- **Notification Service:** *(Đang phát triển)* Tiêu thụ sự kiện thay đổi trạng thái từ RabbitMQ và đẩy thông báo tức thời (realtime) đến trình duyệt khách hàng thông qua **SignalR Hub** theo nhóm người dùng (`userId`).
- **Web Client:** *(Đang phát triển)* Ứng dụng web thuần (HTML5/CSS3/Vanilla JS) kết nối REST API và nhận thông báo thời gian thực qua SignalR JS client.

---

## 2. Kiến trúc luồng dữ liệu & RabbitMQ Topology

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

### RabbitMQ Topology
- **Exchange chính:** `ticket.exchange` (kiểu `topic`, `durable = true`).
- **Hàng đợi xử lý tạo ticket:** `ticket.creation.queue` (bind routing key `ticket.created`, `durable = true`).
- **Hàng đợi cách ly lỗi (DLQ):** `ticket.creation.dlq` bind vào Dead-Letter Exchange `ticket.dlx` (lưu các message hỏng hoặc thất bại sau 3 lần retry).
- **Hàng đợi thông báo:** `ticket.notification.queue` (bind routing key `ticket.status.changed`, `durable = true`).

---

## 3. Cấu trúc thư mục

- `/docs`: Tài liệu thiết kế hệ thống (`design.md`) và bộ câu hỏi bảo vệ (`defense-qa.md`).
- `/src/Contracts`: Định nghĩa các sự kiện (Event), Enum và hằng số dùng chung.
- `/src/SupportApi`: Service Web Minimal API tiếp nhận và quản lý ticket.
- `/src/AssignmentWorker`: Background Service tự động phân loại và phân công ticket.
- `/src/NotificationService`: Service nhận sự kiện và phân phối thông báo realtime qua SignalR.
- `/src/Client`: Giao diện Web tĩnh phục vụ bởi Nginx.
- `/tests/DistributedTicketSystem.AssignmentWorker.Tests`: Dự án Unit Test & Integration Test bằng xUnit.
- `docker-compose.yml`: Script khởi chạy hạ tầng PostgreSQL và RabbitMQ.
- `.env.example`: Biến môi trường mẫu.

---

## 4. Hướng dẫn cài đặt, chạy thử và kiểm thử

### 4.1. Yêu cầu môi trường
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (để chạy PostgreSQL & RabbitMQ)

### 4.2. Khởi chạy hạ tầng (PostgreSQL & RabbitMQ)
```powershell
docker compose up -d postgres rabbitmq
```
- **RabbitMQ Management UI:** Truy cập [http://localhost:15672](http://localhost:15672) (Tài khoản: `guest` / Mật khẩu: `guest`).
- **PostgreSQL:** Cổng `5432`, Database `ticket_support_db`.

### 4.3. Chạy các Microservices (.NET 10)
Mở 2 cửa sổ Terminal riêng biệt để chạy độc lập từng service:

**Terminal 1 — Support API (Cổng `http://localhost:5000`):**
```powershell
dotnet run --project src/SupportApi/DistributedTicketSystem.SupportApi.csproj
```

**Terminal 2 — Assignment Worker:**
```powershell
dotnet run --project src/AssignmentWorker/DistributedTicketSystem.AssignmentWorker.csproj
```

### 4.4. Kiểm thử tự động (Unit Test & Integration Test)
Chạy bộ kiểm thử thuật toán Priority + Round-Robin, kiểm thử đa luồng và luồng xử lý DB:
```powershell
dotnet test tests/DistributedTicketSystem.AssignmentWorker.Tests/DistributedTicketSystem.AssignmentWorker.Tests.csproj
```

### 4.5. Kiểm chứng End-to-End nhanh bằng cURL
Gửi một yêu cầu mức `Urgent` để quan sát `SupportApi` lưu DB (`New`), đẩy lên RabbitMQ và `AssignmentWorker` tự động gán cho nhân viên nhóm `L3_Critical_Support` (`Assigned`):
```powershell
curl -X POST http://localhost:5000/tickets `
  -H "Content-Type: application/json" `
  -d '{\"userId\":\"customer_01\",\"title\":\"Lỗi thanh toán\",\"description\":\"Lỗi 500 khi xác nhận thẻ\",\"priority\":\"Urgent\"}'
```
