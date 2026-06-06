using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using shared_models;
using webhook_service.Services;

namespace webhook_service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly KafkaProducerService _kafkaProducerService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(IConfiguration configuration, KafkaProducerService kafkaProducerService, ILogger<WebhookController> logger)
        {
            _configuration = configuration;
            _kafkaProducerService = kafkaProducerService;
            _logger = logger;
        }

        /// <summary>
        /// GET /webhook - Verifies the webhook callback URL.
        /// </summary>
        [HttpGet]
        public IActionResult Verify()
        {
            var mode = Request.Query["hub.mode"];
            var token = Request.Query["hub.verify_token"];
            var challenge = Request.Query["hub.challenge"];

            var verifyToken = _configuration["Facebook:VerifyToken"];

            if (mode == "subscribe" && token == verifyToken)
            {
                _logger.LogInformation("WEBHOOK_VERIFIED");
                return Content(challenge.ToString(), "text/plain");
            }

            return Forbid();
        }

        /// <summary>
        /// POST /webhook - Receives Facebook events, normalizes them, and publishes them to Kafka.
        /// Returns 200 OK as quickly as possible to avoid Facebook retries.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ReceiveEvent()
        {
            // 1. Validate the HMAC-SHA256 signature.
            var appSecret = _configuration["Facebook:AppSecret"];
            if (string.IsNullOrEmpty(appSecret))
            {
                _logger.LogWarning("Facebook:AppSecret chưa được cấu hình.");
                return StatusCode(500, "Server misconfiguration");
            }

            var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256="))
            {
                _logger.LogWarning("Thiếu hoặc sai header X-Hub-Signature-256.");
                return BadRequest("Invalid signature");
            }

            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var expectedSignature = "sha256=" + CalculateSignature(payload, appSecret);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signatureHeader),
                Encoding.UTF8.GetBytes(expectedSignature)))
            {
                _logger.LogWarning("Xác thực chữ ký thất bại.");
                return BadRequest("Invalid signature");
            }

            _logger.LogInformation("=== WEBHOOK NHẬN SỰ KIỆN THẬT ===");
            _logger.LogInformation("Payload: {Payload}", payload);

            // 2. Parse, normalize, and publish to Kafka without calling the Facebook API here.
            try
            {
                using var jsonDoc = JsonDocument.Parse(payload);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("object", out var objProperty) && objProperty.GetString() == "page")
                {
                    if (root.TryGetProperty("entry", out var entries))
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            var pageId = entry.GetProperty("id").GetString() ?? "";
                            var time = entry.GetProperty("time").GetInt64();

                            // ===== HANDLE MESSENGER EVENTS =====
                            if (entry.TryGetProperty("messaging", out var messagingEvents))
                            {
                                foreach (var messagingEvent in messagingEvents.EnumerateArray())
                                {
                                    var senderId = GetNestedString(messagingEvent, "sender", "id") ?? "unknown";

                                    // Plain text message
                                    if (messagingEvent.TryGetProperty("message", out var messageObj))
                                    {
                                        var mid = messageObj.TryGetProperty("mid", out var mId) ? mId.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                                        var text = messageObj.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";

                                        _logger.LogInformation("📩 TIN NHẮN từ {SenderId}: \"{Text}\"", senderId, text);

                                        await PublishNormalizedEvent(mid, pageId, senderId, time, "message", text, messagingEvent.GetRawText());
                                    }
                                    // Read receipt
                                    else if (messagingEvent.TryGetProperty("read", out _))
                                    {
                                        _logger.LogInformation("👁️ Tin nhắn đã được đọc bởi {SenderId}", senderId);
                                        await PublishNormalizedEvent(Guid.NewGuid().ToString(), pageId, senderId, time, "message_read", "", messagingEvent.GetRawText());
                                    }
                                    // Delivery receipt
                                    else if (messagingEvent.TryGetProperty("delivery", out _))
                                    {
                                        _logger.LogInformation("📬 Tin nhắn đã được giao tới {SenderId}", senderId);
                                        await PublishNormalizedEvent(Guid.NewGuid().ToString(), pageId, senderId, time, "message_delivery", "", messagingEvent.GetRawText());
                                    }
                                    // Postback triggered by a user action
                                    else if (messagingEvent.TryGetProperty("postback", out var postback))
                                    {
                                        var postbackTitle = postback.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                                        _logger.LogInformation("🔘 POSTBACK từ {SenderId}: \"{Title}\"", senderId, postbackTitle);
                                        await PublishNormalizedEvent(Guid.NewGuid().ToString(), pageId, senderId, time, "postback", postbackTitle, messagingEvent.GetRawText());
                                    }
                                }
                            }

                            // ===== HANDLE FEED EVENTS (POSTS, COMMENTS) =====
                            if (entry.TryGetProperty("changes", out var changes))
                            {
                                foreach (var change in changes.EnumerateArray())
                                {
                                    var fieldName = change.TryGetProperty("field", out var field) ? field.GetString() ?? "unknown" : "unknown";

                                    if (fieldName == "feed" && change.TryGetProperty("value", out var value))
                                    {
                                        var item = value.TryGetProperty("item", out var itemProp) ? itemProp.GetString() ?? "" : "";
                                        var verb = value.TryGetProperty("verb", out var verbProp) ? verbProp.GetString() ?? "" : "";

                                        // Comment
                                        if (item == "comment")
                                        {
                                            var commentId = value.TryGetProperty("comment_id", out var cId) ? cId.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                                            var senderId = GetNestedString(value, "from", "id") ?? "unknown";
                                            var senderName = GetNestedString(value, "from", "name") ?? "unknown";
                                            var message = value.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                                            if (senderId == pageId)
                                            {
                                                _logger.LogInformation("Bo qua binh luan do chinh Page tao ra: {CommentId}", commentId);
                                                continue;
                                            }

                                            _logger.LogInformation("💬 BÌNH LUẬN từ {SenderName} ({SenderId}): \"{Message}\" [Hành động: {Verb}]", senderName, senderId, message, verb);
                                            await PublishNormalizedEvent(commentId, pageId, senderId, time, "comment", message, change.GetRawText());
                                        }
                                        // New post / share / photo / video
                                        else if (item == "status" || item == "photo" || item == "video" || item == "share")
                                        {
                                            var postId = value.TryGetProperty("post_id", out var pId) ? pId.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                                            var senderId = GetNestedString(value, "from", "id") ?? "unknown";
                                            var senderName = GetNestedString(value, "from", "name") ?? "unknown";
                                            var message = value.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                                            if (senderId == pageId)
                                            {
                                                _logger.LogInformation("Bo qua feed event do chinh Page tao ra: {PostId}", postId);
                                                continue;
                                            }

                                            _logger.LogInformation("📝 BÀI VIẾT ({Item}) từ {SenderName}: \"{Message}\" [Hành động: {Verb}]", item, senderName, message, verb);
                                            await PublishNormalizedEvent(postId, pageId, senderId, time, $"post_{item}", message, change.GetRawText());
                                        }
                                        // Reaction / like
                                        else if (item == "reaction" || item == "like")
                                        {
                                            var reactionType = value.TryGetProperty("reaction_type", out var rType) ? rType.GetString() ?? "like" : "like";
                                            var senderId = GetNestedString(value, "from", "id") ?? "unknown";
                                            var senderName = GetNestedString(value, "from", "name") ?? "unknown";
                                            if (senderId == pageId)
                                            {
                                                _logger.LogInformation("Bo qua reaction do chinh Page tao ra.");
                                                continue;
                                            }

                                            _logger.LogInformation("❤️ REACTION ({ReactionType}) từ {SenderName}", reactionType, senderName);
                                            await PublishNormalizedEvent(Guid.NewGuid().ToString(), pageId, senderId, time, "reaction", reactionType, change.GetRawText());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return Ok("EVENT_RECEIVED");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý webhook payload");
                return Ok("EVENT_RECEIVED");
            }
        }

        private async Task PublishNormalizedEvent(string eventId, string pageId, string senderId, long timestamp, string eventType, string content, string rawData)
        {
            var normalizedEvent = new NormalizedEvent
            {
                EventId = eventId,
                PageId = pageId,
                SenderId = senderId,
                Timestamp = timestamp,
                EventType = eventType,
                Content = content,
                RawData = rawData
            };

            await _kafkaProducerService.PublishAsync("raw_events", normalizedEvent);
            _logger.LogInformation("✅ Đã đẩy sự kiện {EventType} vào Kafka topic 'raw_events'", eventType);
        }

        /// <summary>
        /// POST /Webhook/test-mock - Developer-only mock endpoint for Swagger/Postman testing with fake IDs.
        /// Does not require HMAC-SHA256 validation.
        /// </summary>
        [HttpPost("test-mock")]
        public async Task<IActionResult> ReceiveMockEvent([FromBody] JsonElement mockPayload)
        {
            _logger.LogInformation("=== WEBHOOK NHẬN SỰ KIỆN MOCK (DEVELOPER) ===");
            var payloadString = mockPayload.GetRawText();
            _logger.LogInformation("Mock Payload: {Payload}", payloadString);

            try
            {
                using var jsonDoc = JsonDocument.Parse(payloadString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("object", out var objProperty) && objProperty.GetString() == "page")
                {
                    if (root.TryGetProperty("entry", out var entries))
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            var pageId = entry.GetProperty("id").GetString() ?? "";
                            var time = entry.GetProperty("time").GetInt64();

                            if (entry.TryGetProperty("messaging", out var messagingEvents))
                            {
                                foreach (var messagingEvent in messagingEvents.EnumerateArray())
                                {
                                    var senderId = GetNestedString(messagingEvent, "sender", "id") ?? "unknown";
                                    if (messagingEvent.TryGetProperty("message", out var messageObj))
                                    {
                                        var mid = messageObj.TryGetProperty("mid", out var mId) ? mId.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                                        var text = messageObj.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                                        await PublishNormalizedEvent(mid, pageId, senderId, time, "message", text, messagingEvent.GetRawText());
                                    }
                                }
                            }

                            if (entry.TryGetProperty("changes", out var changes))
                            {
                                foreach (var change in changes.EnumerateArray())
                                {
                                    var fieldName = change.TryGetProperty("field", out var field) ? field.GetString() ?? "unknown" : "unknown";
                                    if (fieldName == "feed" && change.TryGetProperty("value", out var value))
                                    {
                                        var item = value.TryGetProperty("item", out var itemProp) ? itemProp.GetString() ?? "" : "";
                                        var verb = value.TryGetProperty("verb", out var verbProp) ? verbProp.GetString() ?? "" : "";

                                        if (item == "comment")
                                        {
                                            var commentId = value.TryGetProperty("comment_id", out var cId) ? cId.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                                            var senderId = GetNestedString(value, "from", "id") ?? "unknown";
                                            var senderName = GetNestedString(value, "from", "name") ?? "unknown";
                                            var message = value.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";

                                            await PublishNormalizedEvent(commentId, pageId, senderId, time, "comment", message, change.GetRawText());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                return Ok("MOCK_EVENT_RECEIVED");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý mock webhook payload");
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Computes the HMAC SHA256 signature.
        /// </summary>
        private string CalculateSignature(string payload, string appSecret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Safely reads a nested string value such as sender.id or from.name.
        /// </summary>
        private string? GetNestedString(JsonElement element, string parent, string child)
        {
            if (element.TryGetProperty(parent, out var parentElement) &&
                parentElement.TryGetProperty(child, out var childElement))
            {
                return childElement.GetString();
            }
            return null;
        }
    }
}
