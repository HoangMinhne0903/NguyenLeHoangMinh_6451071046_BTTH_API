namespace shared_models
{
    public class NormalizedEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string PageId { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public long Timestamp { get; set; }

        /// <summary>
        /// "comment", "message", "reaction", "postback", "post_status", etc.
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Raw payload for reference or fallback
        /// </summary>
        public string RawData { get; set; } = string.Empty;
    }
}
