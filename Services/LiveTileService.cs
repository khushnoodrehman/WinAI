using System;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.UI.Notifications;

namespace WinAI.Services
{
    public sealed class LiveTileService
    {
        private const string TileStyleKey = "Setting_TileStyle";
        public const string StyleTransparent = "Transparent";
        public const string StyleColorful = "Colorful";

        private static readonly Lazy<LiveTileService> _instance = 
            new Lazy<LiveTileService>(() => new LiveTileService());
        public static LiveTileService Instance => _instance.Value;

        private readonly ApplicationDataContainer _settings;

        private LiveTileService()
        {
            _settings = ApplicationData.Current.LocalSettings;
        }

        public string CurrentTileStyle
        {
            get
            {
                if (_settings.Values.TryGetValue(TileStyleKey, out object val) && val is string str)
                {
                    return str;
                }
                return StyleTransparent;
            }
            set
            {
                _settings.Values[TileStyleKey] = value;
                ApplyTileStyle(value);
            }
        }

        public bool IsTransparent => CurrentTileStyle == StyleTransparent;

        public void Initialize()
        {
            ApplyTileStyle(CurrentTileStyle);
        }

        public void SetTileStyle(string style)
        {
            CurrentTileStyle = style;
        }

        public void ApplyTileStyle(string style)
        {
            try
            {
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();

                if (style == StyleColorful)
                {
                    string xml = @"<tile>
                      <visual>
                        <binding template=""TileMedium"">
                          <image src=""ms-appx:///Assets/TileColorfulSquare.png"" placement=""background""/>
                        </binding>
                        <binding template=""TileWide"">
                          <image src=""ms-appx:///Assets/TileColorfulWide.png"" placement=""background""/>
                        </binding>
                      </visual>
                    </tile>";

                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    updater.Update(new TileNotification(doc));
                }
                else
                {
                    // Clear custom tile notification so default manifest transparent tile shows
                    updater.Clear();
                }
            }
            catch
            {
                // Safeguard against unsupported platforms
            }
        }
    }
}
