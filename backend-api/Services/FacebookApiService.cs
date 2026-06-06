using System.Net.Http.Json;

namespace backend_api.Services;

public sealed class FacebookApiService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v20.0";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FacebookApiService> _logger;

    public FacebookApiService(HttpClient httpClient, IConfiguration configuration, ILogger<FacebookApiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendMessageAsync(string recipientId, string messageText)
    {
        var payload = new
        {
            messaging_type = "RESPONSE",
            recipient = new { id = recipientId },
            message = new { text = messageText }
        };

        return PostGraphAsync("me/messages", payload);
    }

    public Task ReplyToCommentAsync(string commentId, string messageText)
    {
        var payload = new
        {
            message = messageText
        };

        return PostGraphAsync($"{commentId}/comments", payload);
    }

    public Task HideCommentAsync(string commentId)
    {
        var payload = new
        {
            is_hidden = true
        };

        return PostGraphAsync(commentId, payload);
    }

    private async Task PostGraphAsync(string path, object payload)
    {
        var accessToken = _configuration["Facebook:PageAccessToken"];
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken == "YOUR_PAGE_ACCESS_TOKEN")
        {
            throw new InvalidOperationException("Facebook:PageAccessToken is not configured.");
        }

        var url = $"{GraphApiBaseUrl}/{path}?access_token={Uri.EscapeDataString(accessToken)}";
        using var response = await _httpClient.PostAsJsonAsync(url, payload);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync();
        _logger.LogError("Facebook API failed. Status: {StatusCode}. Body: {Body}", (int)response.StatusCode, errorBody);
        response.EnsureSuccessStatusCode();
    }
}
