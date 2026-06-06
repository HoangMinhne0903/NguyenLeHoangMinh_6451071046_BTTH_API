using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace core_service.Services
{
    public class SpamDetectionService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<SpamDetectionService> _logger;

        public SpamDetectionService(IMemoryCache cache, ILogger<SpamDetectionService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        private static readonly string[] SpamKeywords =
        {
            "mua ngay", "giam gia soc", "click here", "free money", "casino",
            "bet88", "188bet", "w88", "loan shark", "vay nong", "keo baccarat"
        };

        private static readonly string[] SevereToxicKeywords =
        {
            "nhu cuc", "nhu cut", "oc cho", "do ngu", "suc vat",
            "dit me", "con cac", "clm", "vcl", "mat day"
        };

        private static readonly Regex UrlRegex = new(
            @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool IsSpam(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (UrlRegex.IsMatch(text))
            {
                _logger.LogInformation("Spam detected: contains URL.");
                return true;
            }

            var normalizedText = NormalizeText(text);
            foreach (var keyword in SpamKeywords)
            {
                if (normalizedText.Contains(keyword, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Spam detected: contains keyword '{Keyword}'.", keyword);
                    return true;
                }
            }

            return false;
        }

        public bool IsSevereToxic(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var normalizedText = NormalizeText(text);
            foreach (var keyword in SevereToxicKeywords)
            {
                if (normalizedText.Contains(keyword, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Severe toxic content detected: contains keyword '{Keyword}'.", keyword);
                    return true;
                }
            }

            return false;
        }

        public bool IsDuplicateContent(string senderId, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            var normalizedContent = NormalizeText(content);
            var cacheKey = $"content_{senderId}_{normalizedContent.GetHashCode()}";
            if (_cache.TryGetValue(cacheKey, out int count))
            {
                count++;
                _cache.Set(cacheKey, count, TimeSpan.FromHours(24));
                if (count >= 2)
                {
                    _logger.LogInformation("Duplicate content detected from {SenderId}: sent {Count} times.", senderId, count);
                    return true;
                }
            }
            else
            {
                _cache.Set(cacheKey, 1, TimeSpan.FromHours(24));
            }

            return false;
        }

        public bool IsBlacklisted(string senderId)
        {
            var cacheKey = $"spam_count_{senderId}";
            return _cache.TryGetValue(cacheKey, out int spamCount) && spamCount >= 3;
        }

        public bool RecordSpamStrikeAndCheckBlacklist(string senderId)
        {
            var cacheKey = $"spam_count_{senderId}";
            var spamCount = 1;

            if (_cache.TryGetValue(cacheKey, out int currentCount))
            {
                spamCount = currentCount + 1;
            }

            _cache.Set(cacheKey, spamCount, TimeSpan.FromHours(24));

            if (spamCount >= 3)
            {
                _logger.LogWarning("User {SenderId} entered internal blacklist after {Count} spam strikes in 24h.", senderId, spamCount);
                return true;
            }

            _logger.LogInformation("Recorded spam strike {Count}/3 for {SenderId}.", spamCount, senderId);
            return false;
        }

        public bool IsRateLimited(string senderId)
        {
            var cacheKey = $"rate_{senderId}";
            if (_cache.TryGetValue(cacheKey, out int messageCount))
            {
                messageCount++;
                _cache.Set(cacheKey, messageCount, TimeSpan.FromMinutes(1));

                if (messageCount > 20)
                {
                    _logger.LogWarning("Rate limit exceeded for {SenderId}: {Count} messages in 1 minute.", senderId, messageCount);
                    return true;
                }
            }
            else
            {
                _cache.Set(cacheKey, 1, TimeSpan.FromMinutes(1));
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
