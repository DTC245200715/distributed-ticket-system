# TÀI LIỆU THIẾT KẾ HỆ THỐNG HỖ TRỢ YÊU CẦU KHÁCH HÀNG
**Học phần:** Phát triển ứng dụng phân tán  
**Đề tài 07:** Hệ thống hỗ trợ yêu cầu khách hàng  
**Nền tảng:** .NET 10, ASP.NET Core Minimal API, RabbitMQ, SignalR, PostgreSQL, Docker Compose  

---

## 1. BẢNG TRUY VẾT YÊU CẦU (TRACEABILITY MATRIX)

| STT | Chức năng / Yêu cầu đề tài | Thành phần đảm nhiệm | Giao thức / Cơ chế |
|:---|:---|:---|:---|
| 1 | Tạo yêu cầu hỗ trợ mới | **Support API** (`POST /tickets`), **Client Web** (Form tạo) | HTTP POST / REST |
| 2 | Tra cứu danh sách và chi tiết yêu cầu | **Support API** (`GET /tickets`, `GET /tickets/{id}`, `GET /users/{userId}/tickets`), **Client Web** | HTTP GET / REST |
| 3 | Đưa yêu cầu mới vào queue phân loại | **Support API** (Publisher) -> **RabbitMQ** (`ticket.exchange`) | AMQP (Durable Queue, Persistent Message) |
| 4 | Worker gán nhóm/nhân viên theo quy tắc | **Assignment Worker** (Consumer) | AMQP Manual Ack, Priority + Round-Robin Rule |
| 5 | Cập nhật trạng thái hợp lệ (New -> Assigned -> InProgress -> Resolved) | **Support API** (`PATCH /tickets/{id}/status`), **Assignment Worker** (New -> Assigned) | State Machine Validation (EF Core -> PostgreSQL) |
| 6 | Phát sự kiện khi trạng thái/người xử lý thay đổi | **Support API** & **Assignment Worker** -> **RabbitMQ** | Event Publishing (`ticket.status.changed`) |
| 7 | Gửi thông báo realtime cho client liên quan | **Notification Service** (RabbitMQ Consumer -> SignalR Hub) -> **Client Web** (SignalR JS) | WebSockets / SSE qua SignalR Hub (theo Group `userId`) |
| 8 | Lưu lịch sử thay đổi của yêu cầu | **PostgreSQL** (`TicketHistories`), **Support API**, **Assignment Worker** | EF Core DbContext Transaction |

---

## 2. KIẾN TRÚC HỆ THỐNG TỔNG THỂ

```mermaid
flowchart TB
    subgraph Browser["Trình duyệt Client (Host OS: http://localhost)"]
        ClientApp["Client Web (HTML5/CSS3/Vanilla JS + SignalR Client)"]
    end

    subgraph DockerNet["Mạng Docker nội bộ (app-network)"]
        subgraph WebLayer["Web / Gateway Layer"]
            Nginx["Nginx Static Server\n(Port 3000:80)"]
        end

        subgraph CoreServices["Microservices"]
            SupportApi["Support API (.NET 10 Minimal API)\n(Port 5000:8080)"]
            Worker["Assignment Worker (.NET 10 Background Worker)"]
            NotificationSvc["Notification Service (.NET 10 Minimal API + SignalR)\n(Port 5001:8080)"]
        end

        subgraph Middleware["Hạ tầng trung gian (Middleware)"]
            RabbitMQ[("RabbitMQ Broker\n(Port 5672, Mgmt: 15672)")]
            Postgres[("PostgreSQL Database\n(Port 5432)")]
        end
    end

    ClientApp -->|"1. Tải static file"| Nginx
    ClientApp -->|"2. REST API: GET, POST, PATCH"| SupportApi
    ClientApp <-->|"3. SignalR WebSockets (withCredentials)"| NotificationSvc

    SupportApi -->|"Ghi dữ liệu ticket & history ban đầu"| Postgres
    SupportApi -->|"Publish: ticket.created"| RabbitMQ

    RabbitMQ -->|"Consume: ticket.created (Manual Ack)"| Worker
    Worker -->|"Gán nhóm/nhân viên, đổi sang Assigned, ghi history"| Postgres
    Worker -->|"Publish: ticket.status.changed"| RabbitMQ

    SupportApi -->|"Publish: ticket.status.changed khi user PATCH"| RabbitMQ

    RabbitMQ -->|"Consume: ticket.status.changed"| NotificationSvc
    NotificationSvc -->|"Push realtime theo group userId"| ClientApp

    classDef client fill:#e1f5fe,stroke:#0288d1,stroke-width:2px;
    classDef service fill:#e8f5e9,stroke:#388e3c,stroke-width:2px;
    classDef infra fill:#fff3e0,stroke:#f57c00,stroke-width:2px;
    class ClientApp client;
    class SupportApi,Worker,NotificationSvc,Nginx service;
    class RabbitMQ,Postgres infra;
```

---

## 3. THIẾT KẾ MESSAGE VÀ SỰ KIỆN (RABBITMQ)

### 3.1. Topology RabbitMQ
- **Exchange Name:** `ticket.exchange`
- **Exchange Type:** `topic`
- **Tính bền vững:** `durable = true`

| Queue Name | Routing Key Binding | Mục đích | Consumer | Cấu hình độ bền |
|:---|:---|:---|:---|:---|
| `ticket.creation.queue` | `ticket.created` | Hàng đợi tiếp nhận ticket mới để phân công | **Assignment Worker** | `durable = true`, Dead-letter sẵn sàng |
| `ticket.notification.queue` | `ticket.status.changed` | Hàng đợi tiếp nhận sự kiện đổi trạng thái/phân công để phát thông báo | **Notification Service** | `durable = true` |

### 3.2. Cấu trúc Message Payload (Contracts)

#### a) `TicketCreatedEvent` (routing key: `ticket.created`)
```json
{
  "eventId": "b6a8df80-a614-4dc4-8da0-cf8a0026e632",
  "ticketId": "6c2017fc-ff6b-4e1b-b467-f3165b4033b0",
  "userId": "customer_01",
  "title": "Không thể thanh toán đơn hàng",
  "description": "Lỗi 500 khi nhấn nút thanh toán tại bước kiểm tra thẻ tín dụng",
  "priority": "Urgent",
  "createdAt": "2026-11-13T10:30:00Z",
  "correlationId": "corr-1731493800-001"
}
```

#### b) `TicketStatusChangedEvent` (routing key: `ticket.status.changed`)
```json
{
  "eventId": "f781a7b4-3652-45e0-94a2-15f77864ce1a",
  "ticketId": "6c2017fc-ff6b-4e1b-b467-f3165b4033b0",
  "userId": "customer_01",
  "oldStatus": "New",
  "newStatus": "Assigned",
  "assignedTo": "agent_hoang",
  "assignedGroup": "L3_Critical_Support",
  "reason": "Tự động phân công theo quy tắc Priority + Round-Robin",
  "changedBy": "AssignmentWorker",
  "changedAt": "2026-11-13T10:30:02Z",
  "correlationId": "corr-1731493800-001"
}
```

---

## 4. THIẾT KẾ CƠ SỞ DỮ LIỆU (POSTGRESQL)

```mermaid
erDiagram
    TICKETS ||--o{ TICKET_HISTORIES : "has histories"
    TICKETS {
        uuid Id PK
        string UserId "Khách hàng gửi yêu cầu"
        string Title "Tiêu đề yêu cầu"
        string Description "Mô tả chi tiết"
        string Priority "Low | Medium | High | Urgent"
        string Status "New | Assigned | InProgress | Resolved"
        string AssignedTo "Nhân viên xử lý (nullable)"
        string AssignedGroup "Nhóm xử lý (nullable)"
        timestamptz CreatedAt "Thời điểm tạo"
        timestamptz UpdatedAt "Thời điểm cập nhật cuối"
        bytea RowVersion "Khóa lạc quan (Concurrency check)"
    }
    TICKET_HISTORIES {
        uuid Id PK
        uuid TicketId FK "Khóa ngoại tham chiếu Tickets"
        string PreviousStatus "Trạng thái trước đó (nullable)"
        string NewStatus "Trạng thái mới"
        string AssignedTo "Nhân viên xử lý tại thời điểm ghi"
        string AssignedGroup "Nhóm xử lý tại thời điểm ghi"
        string ChangedBy "Tác nhân thay đổi (User hoặc Worker)"
        string Reason "Lý do thay đổi"
        timestamptz ChangedAt "Thời điểm thay đổi"
    }
```

---

## 5. MÔ HÌNH CHUYỂN TRẠNG THÁI (STATE MACHINE)

```mermaid
stateDiagram-v2
    [*] --> New : POST /tickets (API lưu DB & bắn event)
    New --> Assigned : Assignment Worker tự động gán
    Assigned --> InProgress : Nhân viên bắt đầu xử lý (PATCH /status)
    InProgress --> Resolved : Hoàn thành xử lý (PATCH /status)
    Resolved --> [*]

    note right of New
      Quy tắc hợp lệ:
      - Không được nhảy từ New sang InProgress hoặc Resolved
      - Không được nhảy từ Assigned sang Resolved
      - Trạng thái Resolved là trạng thái kết thúc
    end note
```

---

## 6. QUY TẮC PHÂN CÔNG CỦA ASSIGNMENT WORKER

Để hệ thống hoạt động ổn định, dự đoán được và dễ bảo vệ trước hội đồng chấm thi, Worker áp dụng quy tắc kết hợp: **Ưu tiên (Priority-based) + Cân bằng tải vòng tròn (Round-Robin)**:

1. **Phân nhóm theo mức độ ưu tiên (`Priority`)**:
   - `Urgent`: Nhóm `L3_Critical_Support` (đội ngũ giải quyết sự cố khẩn cấp).
   - `High`: Nhóm `L2_Senior_Support` (chuyên viên cao cấp).
   - `Medium` & `Low`: Nhóm `L1_Helpdesk_Support` (bộ phận tiếp nhận & giải đáp cơ bản).

2. **Gán nhân viên trong nhóm theo thuật toán Round-Robin**:
   - Nhóm `L3_Critical_Support`: `agent_hoang`, `agent_dung`
   - Nhóm `L2_Senior_Support`: `agent_lan`, `agent_minh`
   - Nhóm `L1_Helpdesk_Support`: `agent_an`, `agent_binh`, `agent_chi`

3. **Luồng thực thi trong Worker**:
   - Nhận message `TicketCreatedEvent` từ `ticket.creation.queue`.
   - Tính toán `assignedGroup` và `assignedTo` theo thuật toán trên.
   - Mở Transaction cập nhật vào PostgreSQL:
     - Đổi trạng thái ticket từ `New` sang `Assigned`.
     - Điền `AssignedTo` và `AssignedGroup`.
     - Thêm 1 bản ghi vào bảng `TicketHistories`.
   - Chỉ khi DB commit thành công, gọi `channel.BasicAck`.
   - Publish sự kiện `TicketStatusChangedEvent` vào exchange `ticket.exchange` (routing key `ticket.status.changed`).

---

## 7. CÁC BIỂU ĐỒ TRÌNH TỰ (SEQUENCE DIAGRAMS)

### 7.1. Trình tự Tạo Ticket & Tự động Phân công

```mermaid
sequenceDiagram
    autonumber
    actor Customer as Khách hàng (Client Web)
    participant API as Support API
    participant DB as PostgreSQL
    participant Broker as RabbitMQ (ticket.exchange)
    participant Worker as Assignment Worker
    participant Notif as Notification Service
    participant Hub as SignalR (NotificationHub)

    Customer->>API: POST /tickets { userId, title, description, priority }
    Note over API: Kiểm tra hợp lệ dữ liệu (Validation)
    API->>DB: INSERT Ticket (Status = 'New') & INSERT TicketHistory
    API->>Broker: Publish 'ticket.created' message (Persistent)
    API-->>Customer: 201 Created { ticketId, status: 'New', ... }

    Broker->>Worker: Deliver 'ticket.created'
    Note over Worker: Áp dụng quy tắc Priority + Round-Robin
    Worker->>DB: UPDATE Ticket (Status='Assigned', AssignedTo, AssignedGroup) & INSERT TicketHistory
    Note over Worker: DB commit thành công
    Worker->>Broker: BasicAck (Xác nhận đã xử lý xong)
    Worker->>Broker: Publish 'ticket.status.changed'

    Broker->>Notif: Deliver 'ticket.status.changed'
    Notif->>Hub: Clients.Group(userId).SendAsync("ReceiveTicketNotification", event)
    Hub-->>Customer: Realtime Toast: "Ticket của bạn đã được gán cho agent_hoang"
```

### 7.2. Trình tự Cập nhật Trạng thái (PATCH /tickets/{id}/status)

```mermaid
sequenceDiagram
    autonumber
    actor Staff as Nhân viên / Quản trị (Client Web)
    participant API as Support API
    participant DB as PostgreSQL
    participant Broker as RabbitMQ (ticket.exchange)
    participant Notif as Notification Service
    participant Hub as SignalR (NotificationHub)
    actor Customer as Khách hàng (Client Web)

    Staff->>API: PATCH /tickets/{id}/status { newStatus: 'InProgress', changedBy: 'agent_hoang', reason: '...' }
    Note over API: Kiểm tra tính hợp lệ của luồng chuyển trạng thái
    API->>DB: UPDATE Ticket Status & INSERT TicketHistory
    API->>Broker: Publish 'ticket.status.changed'
    API-->>Staff: 200 OK { ticketId, status: 'InProgress', ... }

    Broker->>Notif: Deliver 'ticket.status.changed'
    Notif->>Hub: Clients.Group(userId).SendAsync("ReceiveTicketNotification", event)
    Hub-->>Customer: Realtime Toast: "Ticket #123 chuyển sang trạng thái InProgress"
```

---

## 8. DANH SÁCH API ENDPOINTS (SUPPORT API)

1. `POST /tickets`
   - Body: `{ "userId": "string", "title": "string", "description": "string", "priority": "Low|Medium|High|Urgent" }`
   - Response: `201 Created`
2. `GET /tickets`
   - Query: `?status=...&priority=...&page=1&pageSize=20`
   - Response: `200 OK [ { ticket... }, ... ]`
3. `GET /tickets/{id}`
   - Path: `id` (GUID)
   - Response: `200 OK { ticket, histories: [ ... ] }` hoặc `404 Not Found`
4. `PATCH /tickets/{id}/status`
   - Path: `id` (GUID)
   - Body: `{ "newStatus": "Assigned|InProgress|Resolved", "changedBy": "string", "reason": "string" }`
   - Response: `200 OK` hoặc `400 Bad Request` (nếu sai luồng trạng thái)
5. `GET /users/{userId}/tickets`
   - Path: `userId` (string)
   - Response: `200 OK [ { ticket... }, ... ]`

---

## 9. THIẾT KẾ TỔ CHỨC DỰ ÁN (SOLUTION STRUCTURE)

```text
/
├── docs/
│   ├── design.md                      # Tài liệu phân tích và thiết kế hệ thống
│   ├── test-report.md                 # Báo cáo kết quả kiểm thử và khả năng chịu lỗi
│   └── defense-qa.md                  # 20+ câu hỏi & trả lời bảo vệ đồ án
├── src/
│   ├── DistributedTicketSystem.sln    # Solution tổng .NET 10
│   ├── Contracts/                     # Class thư viện chứa Message & Event dùng chung
│   │   └── DistributedTicketSystem.Contracts.csproj
│   ├── SupportApi/                    # ASP.NET Core Minimal API
│   │   ├── DistributedTicketSystem.SupportApi.csproj
│   │   └── Dockerfile
│   ├── AssignmentWorker/              # Background Worker Service
│   │   ├── DistributedTicketSystem.AssignmentWorker.csproj
│   │   └── Dockerfile
│   ├── NotificationService/           # ASP.NET Core Minimal API + SignalR Hub
│   │   ├── DistributedTicketSystem.NotificationService.csproj
│   │   └── Dockerfile
│   └── Client/                        # Web App tĩnh (HTML5, CSS3, Vanilla JS, Nginx)
│       ├── index.html
│       ├── style.css
│       ├── app.js
│       ├── config.js
│       ├── nginx.conf
│       └── Dockerfile
├── docker-compose.yml                 # Khởi chạy toàn bộ hệ thống bằng 1 lệnh duy nhất
├── .env.example                       # Mẫu biến môi trường cấu hình cổng, connection strings
├── .gitignore                         # Chuẩn .NET, IDE, temporary files
├── .editorconfig                      # Quy ước code style C#
└── README.md                          # Hướng dẫn chi tiết thiết lập, khởi chạy và demo
```
