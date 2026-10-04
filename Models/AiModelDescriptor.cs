using System;

namespace WinAI.Models
{
    /// <summary>
    /// Centralized descriptor representing an AI model, its provider, and its capabilities.
    /// Used across the Model Switcher, Provider Registry, and Chat session.
    /// </summary>
    public class AiModelDescriptor
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string ProviderId { get; set; }
        public string ProviderName { get; set; }
        public ModelCapabilities Capabilities { get; set; } = ModelCapabilities.Text;
        public bool RequiresApiKey { get; set; } = true;
        public bool IsConfigured { get; set; }
        public string Description { get; set; }
        public bool IsAvailable { get; set; } = true;
        public string Status { get; set; } = "Available";

        public bool SupportsVision => Capabilities.HasFlag(ModelCapabilities.Vision);
        public bool SupportsReasoning => Capabilities.HasFlag(ModelCapabilities.Reasoning);
        public bool SupportsStreaming => Capabilities.HasFlag(ModelCapabilities.Streaming);

        public string FullDisplay => $"{DisplayName} ({ProviderName})";

        public AiModelDescriptor()
        {
        }

        public AiModelDescriptor(
            string id, 
            string displayName, 
            string providerId, 
            string providerName, 
            ModelCapabilities capabilities = ModelCapabilities.Text,
            bool isConfigured = false,
            string description = null)
        {
            Id = id;
            DisplayName = displayName;
            ProviderId = providerId;
            ProviderName = providerName;
            Capabilities = capabilities;
            IsConfigured = isConfigured;
            Description = description ?? id;
        }

        public AiModelItem ToModelItem()
        {
            var item = new AiModelItem(Id, DisplayName, ProviderName, Description ?? Id);
            item.IsAvailable = IsAvailable;
            item.Status = Status;
            return item;
        }

        public static AiModelDescriptor FromModelItem(AiModelItem item, string providerId, bool isConfigured = false, ModelCapabilities capabilities = ModelCapabilities.Text)
        {
            if (item == null) return null;
            return new AiModelDescriptor(
                id: item.Id,
                displayName: item.DisplayName,
                providerId: providerId,
                providerName: item.ProviderName,
                capabilities: capabilities,
                isConfigured: isConfigured,
                description: item.Description
            )
            {
                IsAvailable = item.IsAvailable,
                Status = item.Status
            };
        }

        public override string ToString() => FullDisplay;
    }
}
