using System;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace WinAI.Models
{
    /// <summary>
    /// Represents an AI model provider displayed on the Home Screen.
    /// </summary>
    public class AIProvider
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SelectedModel { get; set; }
        public Color BrandColor { get; set; }
        public SolidColorBrush BrandBrush => new SolidColorBrush(BrandColor);
        public string IconType { get; set; }
        public string Tag { get; set; }

        public AIProvider()
        {
        }

        public AIProvider(string id, string name, string selectedModel, Color brandColor, string iconType)
        {
            Id = id;
            Name = name;
            SelectedModel = selectedModel;
            BrandColor = brandColor;
            IconType = iconType;
            Tag = id;
        }
    }
}
