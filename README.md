# Hệ Thống Xử Lý Sự Kiện Facebook Page

Dự án này mô phỏng một hệ thống microservices dùng để nhận sự kiện từ Facebook Page, đẩy dữ liệu qua Kafka, phân tích nội dung và thực hiện các hành động tự động như trả lời bình luận hoặc ẩn bình luận spam.

## 1. Tổng quan

Hệ thống được chia thành 4 service chính:

- `webhook-service`: nhận webhook từ Facebook và publish dữ liệu chuẩn hóa vào Kafka.
- `core-service`: consume dữ liệu thô, phân tích nội dung và sinh command xử lý.
- `backend-api`: consume command, gọi Facebook Graph API và lưu trạng thái vào database.
- `retry-service`: xử lý retry các command lỗi và chuyển sang `dead_letter` nếu vượt quá số lần thử lại.

## 2. Kiến trúc xử lý

```text
Facebook Page
    -> webhook-service
    -> Kafka: raw_events
    -> core-service
    -> Kafka: reply_commands / send_failed
    -> backend-api
    -> retry-service
    -> Kafka: send_retry / dead_letter
```

## 3. Mô tả từng service

### `webhook-service` (`http://localhost:3001`)

Chức năng:

- xác thực webhook callback với Facebook
- kiểm tra chữ ký request thật từ Facebook
- chuẩn hóa event comment, feed, message, reaction
- publish dữ liệu vào topic `raw_events`

Endpoint chính:

- `GET /health`
- `GET /webhook`
- `POST /webhook`
- `POST /webhook/test-mock`

### `core-service` (`http://localhost:3002`)

Chức năng:

- consume `raw_events`
- phát hiện spam, nội dung lặp, toxic comment và blacklist nội bộ
- phân loại nội dung bình thường
- tạo command và publish sang `reply_commands`

Endpoint chính:

- `GET /health`

### `backend-api` (`http://localhost:3000`)

Chức năng:

- consume `reply_commands` và `send_retry`
- gọi Facebook Graph API để reply hoặc hide comment
- lưu `IdempotencyKeys`, `EventTrackings`, `FailedCommands` vào SQL Server
- publish command lỗi sang `send_failed`

Endpoint chính:

- `GET /health`

### `retry-service` (`http://localhost:3003`)

Chức năng:

- consume `send_failed`
- retry command theo exponential backoff
- republish sang `send_retry`
- đưa message sang `dead_letter` nếu lỗi quá số lần cho phép

Endpoint chính:

- `GET /health`
- `GET /status`

## 4. Shared Models

Project `shared-models` chứa các model dùng chung giữa các service:

- `NormalizedEvent`
- `CommandEvent`
- `AnalysisResult`
- `ApiResponse`
- `EventState`

## 5. Các Kafka topic sử dụng

- `raw_events`
- `reply_commands`
- `send_failed`
- `send_retry`
- `dead_letter`

## 6. Hạ tầng local

`docker-compose.yml` đang cấu hình các thành phần sau:

- Zookeeper
- Kafka
- SQL Server
- Kafka UI
- Prometheus
- Alertmanager
- Kafka exporter

Port quan trọng:

- Kafka UI: `http://localhost:8085`
- Kafka broker: `localhost:9092`
- SQL Server: `localhost,1435`
- Prometheus: `http://localhost:9090`
- Alertmanager: `http://localhost:9093`

## 7. Cấu hình cần thay trước khi chạy

### `webhook-service/appsettings.json`

```json
{
  "Facebook": {
    "AppSecret": "YOUR_FACEBOOK_APP_SECRET",
    "VerifyToken": "fb_webhook_verify_2026_6c2b9d41a8f7",
    "PageAccessToken": "YOUR_FACEBOOK_PAGE_ACCESS_TOKEN"
  }
}
```

### `backend-api/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1435;Database=ApiFacebookDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
  },
  "Facebook": {
    "AppSecret": "YOUR_FACEBOOK_APP_SECRET",
    "VerifyToken": "fb_webhook_verify_2026_6c2b9d41a8f7",
    "PageAccessToken": "YOUR_FACEBOOK_PAGE_ACCESS_TOKEN"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092"
  }
}
```

## 8. Cách chạy local

### Bước 1: chạy hạ tầng Docker

```powershell
cd D:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook
docker-compose up -d zookeeper kafka sqlserver kafka-ui
```

### Bước 2: chạy các service

Mở 4 terminal riêng:

#### Terminal 1

```powershell
cd D:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook\webhook-service
dotnet run
```

#### Terminal 2

```powershell
cd D:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook\core-service
dotnet run
```

#### Terminal 3

```powershell
cd D:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook\backend-api
dotnet run
```

#### Terminal 4

```powershell
cd D:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook\retry-service
dotnet run
```

### Bước 3: chạy ngrok

```powershell
ngrok http 3001
```

Dùng URL HTTPS do ngrok cấp để khai báo trên Meta for Developers:

- Callback URL: `https://<ngrok-domain>/webhook`
- Verify Token: `fb_webhook_verify_2026_6c2b9d41a8f7`

## 9. Link kiểm tra nhanh

- `http://localhost:3001/health`
- `http://localhost:3002/health`
- `http://localhost:3000/health`
- `http://localhost:3003/health`
- `http://localhost:8085`

## 10. Lưu ý về webhook Facebook

- Route thật của project là `/webhook`, không phải `/api/webhook`.
- `VerifyToken` là chuỗi tự đặt, phải trùng giữa Facebook Developer và file config.
- `PageAccessToken` phải còn hạn. Nếu token hết hạn thì `backend-api` sẽ không reply được và command sẽ đi vào luồng retry.

## 11. Cách test

### Test verify webhook local

Mở link:

```text
http://localhost:3001/webhook?hub.mode=subscribe&hub.verify_token=fb_webhook_verify_2026_6c2b9d41a8f7&hub.challenge=12345
```

Nếu đúng, service sẽ trả:

```text
12345
```

### Test mock event

Có thể gửi mock event vào:

```text
POST http://localhost:3001/webhook/test-mock
```

Mục đích là kiểm tra pipeline Kafka trước khi test với Facebook thật.

### Test retry flow

Có thể produce một message giả vào topic `send_failed` trên Kafka UI để kiểm tra `retry-service` và `dead_letter`.

## 12. Database

Các bảng chính được `backend-api` sử dụng:

- `EventTrackings`
- `FailedCommands`
- `IdempotencyKeys`

Ví dụ câu query xem lỗi:

```sql
SELECT TOP 20
    CommandId,
    EventId,
    Action,
    TargetId,
    ErrorMessage,
    FailedAt
FROM FailedCommands
ORDER BY FailedAt DESC;
```

## 13. Một số lỗi thường gặp

### Webhook verify thất bại

Kiểm tra:

- `webhook-service` có đang chạy ở port `3001` không
- ngrok có forward đúng `3001` không
- callback URL có đúng `/webhook` không
- Verify Token có khớp config không

### Kafka không kết nối được

Kiểm tra:

- `docker ps`
- Kafka đã lên ở `localhost:9092` chưa
- service .NET có chạy sau khi Kafka sẵn sàng hay không

### Facebook không reply

Kiểm tra:

- `Facebook:PageAccessToken` còn hạn không
- app đã subscribe vào page chưa
- page đã bật field `feed` chưa

### `send_failed` không thấy message

Trường hợp này có thể xảy ra nếu `retry-service` consume message quá nhanh.  
Muốn chụp topic `send_failed`, hãy tắt `retry-service` trước rồi gây lỗi lại.

## 14. File báo cáo trong repo

Repo hiện có sẵn các file hỗ trợ làm báo cáo:

- `Bao_cao_da_dien_noi_dung_Facebook_Page_API.docx`
- `scripts/create_filled_report.py`
- `scripts/create_report_template.py`
- `scripts/create_simple_report_template.py`

Các file này dùng để tạo mẫu báo cáo Word, chèn ảnh minh chứng và mô tả quá trình làm bài.
