namespace WinAI.Models
{
    /// <summary>
    /// Represents an AI model dynamically fetched from an active provider's API.
    /// </summary>
    public class AiModelItem
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string ProviderName { get; set; }
        public string Description { get; set; }

        public string FullDisplay => $"{DisplayName} ({ProviderName})";

        public override string ToString() => FullDisplay;
    }
}
