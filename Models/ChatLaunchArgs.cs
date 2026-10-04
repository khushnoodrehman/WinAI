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
        public string ImageBase64 { get; set; }
        public string ImageMimeType { get; set; }
        public string ImageFileName { get; set; }

        public ChatLaunchArgs()
        {
        }

        public ChatLaunchArgs(
            string prompt, 
            string providerId = null, 
            string modelId = null,
            string imageBase64 = null,
            string imageMimeType = null,
            string imageFileName = null)
        {
            Prompt = prompt;
            ProviderId = providerId;
            ModelId = modelId;
            ImageBase64 = imageBase64;
            ImageMimeType = imageMimeType;
            ImageFileName = imageFileName;
        }
    }
}
