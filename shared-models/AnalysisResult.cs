namespace shared_models
{
    public class AnalysisResult
    {
        public string Intent { get; set; } = string.Empty;
        public string Sentiment { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string ReplyMessage { get; set; } = string.Empty;
        public bool NeedsHumanReview { get; set; }
    }
}
