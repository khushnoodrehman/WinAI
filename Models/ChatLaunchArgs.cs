namespace WinAI.Models
{
    /// <summary>
    /// Arguments passed when launching chat from the Home screen or external launcher.
    /// </summary>
    public class ChatLaunchArgs
    {
        public string Prompt { get; set; }
        public string ProviderId { get; set; }
        public string ModelId { get; set; }

        public ChatLaunchArgs()
        {
        }

        public ChatLaunchArgs(string prompt, string providerId = null, string modelId = null)
        {
            Prompt = prompt;
            ProviderId = providerId;
            ModelId = modelId;
        }
    }
}
