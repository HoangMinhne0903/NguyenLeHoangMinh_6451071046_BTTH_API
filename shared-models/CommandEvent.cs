namespace shared_models
{
    /// <summary>
    /// Represents an action command published by Core Service for Backend API to execute.
    /// </summary>
    public class CommandEvent
    {
        public string CommandId { get; set; } = Guid.NewGuid().ToString();
        public string EventId { get; set; } = string.Empty;
        public string PageId { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;

        /// <summary>
        /// "reply_message", "reply_comment", "hide_comment", "block_user"
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Target ID: recipientId for messages, commentId for comments
        /// </summary>
        public string TargetId { get; set; } = string.Empty;

        /// <summary>
        /// The message text to send (for reply actions)
        /// </summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// AI analysis results
        /// </summary>
        public string Intent { get; set; } = string.Empty;
        public string Sentiment { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public bool NeedsHumanReview { get; set; }

        /// <summary>
        /// Retry tracking
        /// </summary>
        public int RetryCount { get; set; } = 0;
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
