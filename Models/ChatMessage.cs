using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;

namespace WinAI.Models
{
    /// <summary>
    /// Represents a single message in the chat conversation feed.
    /// Supports text and multimodal image attachments.
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _text;
        private bool _isUser;
        private string _senderName;
        private DateTime _timestamp;
        private string _imageBase64;
        private string _imageMimeType;
        private BitmapImage _imageSource;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsUser
        {
            get => _isUser;
            set
            {
                if (_isUser != value)
                {
                    _isUser = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SenderName
        {
            get => _senderName;
            set
            {
                if (_senderName != value)
                {
                    _senderName = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedTime));
                }
            }
        }

        public string ImageBase64
        {
            get => _imageBase64;
            set
            {
                if (_imageBase64 != value)
                {
                    _imageBase64 = value;
                    _imageSource = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImage));
                    OnPropertyChanged(nameof(ImageSource));
                }
            }
        }

        public string ImageMimeType
        {
            get => _imageMimeType;
            set
            {
                if (_imageMimeType != value)
                {
                    _imageMimeType = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        public bool HasImage => !string.IsNullOrEmpty(_imageBase64);

        [JsonIgnore]
        public Windows.UI.Xaml.Visibility ImageVisibility => HasImage
            ? Windows.UI.Xaml.Visibility.Visible
            : Windows.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public BitmapImage ImageSource
        {
            get
            {
                if (_imageSource == null && !string.IsNullOrEmpty(_imageBase64))
                {
                    LoadImageSourceAsync();
                }
                return _imageSource;
            }
        }

        private async void LoadImageSourceAsync()
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(_imageBase64);
                using (var stream = new InMemoryRandomAccessStream())
                {
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                    }
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(stream);
                    _imageSource = bmp;
                    OnPropertyChanged(nameof(ImageSource));
                }
            }
            catch
            {
                // Silently handle invalid image bytes
            }
        }

        public string FormattedTime => Timestamp.ToString("t");

        public string ProviderId { get; set; }
        public string ModelId { get; set; }
        public bool IsSystemNotification { get; set; }

        public ChatMessage()
        {
            Timestamp = DateTime.Now;
        }

        public ChatMessage(string text, bool isUser, string senderName = null, string providerId = null, string modelId = null)
        {
            Text = text;
            IsUser = isUser;
            SenderName = senderName ?? (isUser ? "You" : "WinAI");
            ProviderId = providerId;
            ModelId = modelId;
            Timestamp = DateTime.Now;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
