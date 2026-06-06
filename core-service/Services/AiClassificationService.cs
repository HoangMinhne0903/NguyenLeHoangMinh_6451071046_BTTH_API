using System.Globalization;
using System.Text;
using System.Text.Json;
using shared_models;

namespace core_service.Services
{
    public class AiClassificationService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiClassificationService> _logger;

        public AiClassificationService(HttpClient httpClient, IConfiguration configuration, ILogger<AiClassificationService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AnalysisResult> AnalyzeContentAsync(string content)
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_GEMINI_API_KEY")
            {
                _logger.LogWarning("Gemini ApiKey is not configured. Returning rule-based fallback analysis.");
                return AnalyzeFallback(content);
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var prompt = $@"Analyze the following user message from a Facebook Page. Provide the output strictly in JSON format with these fields:
- Intent: The main goal of the user (e.g., 'inquiry', 'complaint', 'greeting', 'feedback', 'spam', 'price_inquiry', 'support')
- Sentiment: The emotion ('positive', 'neutral', 'negative')
- Confidence: A score between 0.0 and 1.0 indicating confidence in the analysis
- ReplyMessage: A suggested short reply in Vietnamese appropriate for the sentiment and intent.
  For positive sentiment: thank the user warmly.
  For negative sentiment: apologize and offer to help.
  For inquiries: provide a helpful response.
  For spam: leave empty.
- NeedsHumanReview: true only if it is an extremely complex technical issue or severe billing dispute; false for standard greetings, product/price inquiries, and general negative feedback or simple complaints (like waiting too long, slow delivery, or minor product damage) which should receive a polite apology and promise to inspect the issue in ReplyMessage.

Message: ""{content}""
JSON Output:";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, httpContent);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API call failed ({StatusCode}): {Error}", (int)response.StatusCode, errorBody);
                    throw new HttpRequestException($"Gemini API returned {(int)response.StatusCode}");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var textResponse = candidates[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString() ?? string.Empty;

                    textResponse = textResponse.Replace("```json", string.Empty).Replace("```", string.Empty).Trim();

                    var result = JsonSerializer.Deserialize<AnalysisResult>(textResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return result ?? AnalyzeFallback(content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Gemini API error: {Message}. Falling back to rule-based analysis.", ex.Message);
                return AnalyzeFallback(content);
            }

            return AnalyzeFallback(content);
        }

        private static AnalysisResult AnalyzeFallback(string content)
        {
            var normalized = NormalizeText(content);

            if (ContainsAny(normalized, "gia", "bao nhieu", "bao gia", "cost", "price"))
            {
                return new AnalysisResult
                {
                    Intent = "price_inquiry",
                    Sentiment = "neutral",
                    Confidence = 0.85,
                    ReplyMessage = "Chao ban! Ban vui long de lai ten san pham hoac gui tin nhan de chung toi bao gia chi tiet.",
                    NeedsHumanReview = false
                };
            }

            if (ContainsAny(normalized, "cham", "lau", "loi", "hong", "khong nhan", "chua nhan", "that vong", "te", "kem"))
            {
                return new AnalysisResult
                {
                    Intent = "complaint",
                    Sentiment = "negative",
                    Confidence = 0.88,
                    ReplyMessage = "Rat xin loi ban vi trai nghiem chua tot. Ban vui long nhan tin hoac de lai thong tin de chung toi ho tro ngay.",
                    NeedsHumanReview = false
                };
            }

            if (ContainsAny(normalized, "cam on", "tot", "hay", "tuyet", "ok", "dep"))
            {
                return new AnalysisResult
                {
                    Intent = "positive_feedback",
                    Sentiment = "positive",
                    Confidence = 0.82,
                    ReplyMessage = "Cam on ban da ung ho Page. Chung toi rat vui khi nhan duoc phan hoi tich cuc tu ban.",
                    NeedsHumanReview = false
                };
            }

            if (ContainsAny(normalized, "xin chao", "chao", "hello", "hi", "shop oi"))
            {
                return new AnalysisResult
                {
                    Intent = "greeting",
                    Sentiment = "neutral",
                    Confidence = 0.8,
                    ReplyMessage = "Chao ban! Chung toi da nhan duoc tin nhan va se phan hoi som nhat co the.",
                    NeedsHumanReview = false
                };
            }

            return new AnalysisResult
            {
                Intent = "fallback_reply",
                Sentiment = "neutral",
                Confidence = 0.75,
                ReplyMessage = "Chao ban! Chung toi da nhan duoc thong tin va se phan hoi som nhat co the.",
                NeedsHumanReview = false
            };
        }

        private static bool ContainsAny(string normalizedContent, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (normalizedContent.Contains(keyword, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeText(string input)
        {
            var normalized = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd');
        }
    }
}
