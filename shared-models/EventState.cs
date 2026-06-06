namespace shared_models
{
    public enum EventState
    {
        Received,
        Processing,
        Processed,
        Replied,
        Failed,
        Blacklisted,
        PendingReview,
        RateLimited
    }
}
