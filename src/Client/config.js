// Cấu hình địa chỉ các Microservice cho Web Client.
// Khi triển khai thực tế hoặc qua Reverse Proxy, chỉ cần cập nhật file này mà không sửa app.js.
window.APP_CONFIG = {
    SUPPORT_API_URL: "http://localhost:5000",
    NOTIFICATION_HUB_URL: "http://localhost:5001/hubs/notifications"
};
