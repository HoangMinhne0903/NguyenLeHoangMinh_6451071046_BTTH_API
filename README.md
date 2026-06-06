# Facebook Page Event Pipeline

Microservices demo for receiving Facebook Page events, pushing them through Kafka, analyzing content, and executing automated actions such as replying to comments or hiding spam.

## Overview

This project is organized as an event-driven pipeline with four .NET services:

- `webhook-service`: receives Facebook webhook events and publishes normalized data to Kafka.
- `core-service`: consumes raw events, applies moderation and classification rules, then emits action commands.
- `backend-api`: consumes commands, calls Facebook Graph API, and persists processing state.
- `retry-service`: retries failed commands and moves unrecoverable messages to `dead_letter`.

## Architecture

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

## Services

### `webhook-service` (`http://localhost:3001`)

Responsibilities:

- verify the Facebook webhook callback
- validate incoming signatures for real Facebook requests
- normalize comment, feed, message, and reaction events
- publish normalized payloads to Kafka topic `raw_events`

Useful endpoints:

- `GET /health`
- `GET /webhook`
- `POST /webhook`
- `POST /webhook/test-mock`

### `core-service` (`http://localhost:3002`)

Responsibilities:

- consume `raw_events`
- detect spam, duplicate content, severe toxic content, and blacklist cases
- classify normal content and create action commands
- publish commands to `reply_commands`

Useful endpoint:

- `GET /health`

### `backend-api` (`http://localhost:3000`)

Responsibilities:

- consume `reply_commands` and `send_retry`
- call Facebook Graph API to reply or hide comments
- store idempotency keys, event tracking, and failed commands in SQL Server
- publish failed commands to `send_failed`

Useful endpoint:

- `GET /health`

### `retry-service` (`http://localhost:3003`)

Responsibilities:

- consume `send_failed`
- retry commands with exponential backoff
- republish retry attempts to `send_retry`
- move exhausted commands to `dead_letter`

Useful endpoints:

- `GET /health`
- `GET /status`

## Shared Models

The `shared-models` project contains the DTOs exchanged between services, including:

- `NormalizedEvent`
- `CommandEvent`
- `AnalysisResult`
- `ApiResponse`
- `EventState`

## Kafka Topics

The main topics used in the pipeline are:

- `raw_events`
- `reply_commands`
- `send_failed`
- `send_retry`
- `dead_letter`

## Local Stack

Docker Compose provisions:

- Zookeeper
- Kafka
- SQL Server
- Kafka UI
- Prometheus
- Alertmanager
- Kafka exporter

Important local ports:

- Kafka UI: `http://localhost:8085`
- SQL Server: `localhost,1435`
- Kafka broker: `localhost:9092`
- Prometheus: `http://localhost:9090`
- Alertmanager: `http://localhost:9093`

## Configuration

Before running the system, update these placeholders:

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

## Run Locally

### 1. Start infrastructure

```powershell
cd D:\code\nam3\Ki_II\API\Thuc_hanh\Api_facebook
docker-compose up -d zookeeper kafka sqlserver kafka-ui
```

### 2. Start services

Open four terminals and run:

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

### 3. Expose webhook with ngrok

```powershell
ngrok http 3001
```

Use the generated HTTPS URL in Facebook Developer:

- Callback URL: `https://<ngrok-domain>/webhook`
- Verify Token: `fb_webhook_verify_2026_6c2b9d41a8f7`

## Health Checks

- `http://localhost:3001/health`
- `http://localhost:3002/health`
- `http://localhost:3000/health`
- `http://localhost:3003/health`

## Facebook Webhook Notes

- The real route is `/webhook`, not `/api/webhook`.
- `VerifyToken` is user-defined and must match the value entered in Meta for Developers.
- `PageAccessToken` must be valid. If it expires, `backend-api` will fail when calling Graph API and commands will move into the retry flow.

## Testing

### Test webhook verification locally

Open:

```text
http://localhost:3001/webhook?hub.mode=subscribe&hub.verify_token=fb_webhook_verify_2026_6c2b9d41a8f7&hub.challenge=12345
```

Expected response:

```text
12345
```

### Test with a mock event

You can post a mock event to:

```text
POST http://localhost:3001/webhook/test-mock
```

This is useful for validating the Kafka pipeline before sending real Facebook events.

### Test retry flow manually

Produce a fake command into `send_failed` with Kafka UI to validate retry and dead-letter behavior.

## Database Tables

The main tables used by `backend-api` are:

- `EventTrackings`
- `FailedCommands`
- `IdempotencyKeys`

Example query:

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

## Common Problems

### Webhook verification fails

Check:

- `webhook-service` is running on port `3001`
- ngrok forwards to `3001`
- the callback URL ends with `/webhook`
- the Verify Token matches your config

### Kafka connection errors

Check:

- `docker ps`
- Kafka is up on `localhost:9092`
- services were started after Kafka became healthy

### Facebook replies fail

Check:

- `Facebook:PageAccessToken` is still valid
- the page is subscribed to the app
- the app has the required Page permissions

### `send_failed` appears empty

This can happen if `retry-service` consumes the message immediately.  
To capture it in Kafka UI, stop `retry-service` first, then trigger a new failure.

## Report Files

The repository includes Word report artifacts and generation scripts:

- `Bao_cao_da_dien_noi_dung_Facebook_Page_API.docx`
- `scripts/create_filled_report.py`
- `scripts/create_report_template.py`
- `scripts/create_simple_report_template.py`

These are intended to help prepare a submission report with screenshots and implementation notes.
