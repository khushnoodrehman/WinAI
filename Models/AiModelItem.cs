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
        public ModelCapabilities Capabilities { get; set; } = ModelCapabilities.Text;
        public string Status { get; set; } = "Available";
        public bool IsAvailable { get; set; } = true;
        public bool IsDeprecated { get; set; } = false;
        public System.DateTime? LastValidatedUtc { get; set; }
        public System.DateTime? LastFetchedUtc { get; set; }
        public string StatusDetails { get; set; }
        public long? LatencyMs { get; set; }
        public bool IsTesting { get; set; }

        public string FormattedValidationTime
        {
            get
            {
                if (IsTesting) return "Testing connection...";
                if (!LastValidatedUtc.HasValue) return "Not tested yet";

                var elapsed = System.DateTime.UtcNow - LastValidatedUtc.Value;
                string timeStr;
                if (elapsed.TotalSeconds < 60) timeStr = "just now";
                else if (elapsed.TotalMinutes < 60) timeStr = $"{(int)elapsed.TotalMinutes}m ago";
                else if (elapsed.TotalHours < 24) timeStr = $"{(int)elapsed.TotalHours}h ago";
                else timeStr = $"{(int)elapsed.TotalDays}d ago";

                if (LatencyMs.HasValue && LatencyMs > 0)
                {
                    return $"Tested {timeStr} • {LatencyMs}ms";
                }
                return $"Tested {timeStr}";
            }
        }

        public string FormattedStatus
        {
            get
            {
                if (IsTesting) return "Testing...";
                if (!string.IsNullOrWhiteSpace(Status)) return Status;
                return IsAvailable ? "Available" : "Unavailable";
            }
        }

        public string FullDisplay => $"{DisplayName} ({ProviderName})";

        public AiModelItem() { }

        public AiModelItem(string id, string displayName, string providerName, string description = null, ModelCapabilities capabilities = ModelCapabilities.Text)
        {
            Id = id;
            DisplayName = displayName;
            ProviderName = providerName;
            Description = description;
            Capabilities = capabilities;
            LastFetchedUtc = System.DateTime.UtcNow;
        }

        public AiModelDescriptor ToDescriptor(string providerId = null)
        {
            var desc = new AiModelDescriptor(
                id: Id,
                displayName: DisplayName,
                providerId: providerId ?? ProviderName?.ToLowerInvariant() ?? "openai",
                providerName: ProviderName ?? "OpenAI",
                capabilities: Capabilities,
                isConfigured: true,
                description: Description
            );
            desc.IsAvailable = IsAvailable;
            desc.Status = Status;
            return desc;
        }

        public override string ToString() => FullDisplay;
    }
}
