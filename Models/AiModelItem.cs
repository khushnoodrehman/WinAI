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

        public AiModelItem() { }

        public AiModelItem(string id, string displayName, string providerName, string description = null)
        {
            Id = id;
            DisplayName = displayName;
            ProviderName = providerName;
            Description = description;
        }

        public AiModelDescriptor ToDescriptor(string providerId = null)
        {
            return new AiModelDescriptor(
                id: Id,
                displayName: DisplayName,
                providerId: providerId ?? ProviderName?.ToLowerInvariant() ?? "openai",
                providerName: ProviderName ?? "OpenAI",
                description: Description
            );
        }

        public override string ToString() => FullDisplay;
    }
}
