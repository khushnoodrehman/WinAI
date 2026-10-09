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

        private string _attachmentId;
        private string _localFileName;
        private string _thumbnailFileName;
        private string _originalFileName;
        private int _imageWidth;
        private int _imageHeight;
        private long _fileSizeBytes;

        public string AttachmentId
        {
            get => _attachmentId;
            set { _attachmentId = value; OnPropertyChanged(); }
        }

        public string LocalFileName
        {
            get => _localFileName;
            set
            {
                if (_localFileName != value)
                {
                    _localFileName = value;
                    _imageSource = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImage));
                    OnPropertyChanged(nameof(ImageVisibility));
                    OnPropertyChanged(nameof(ImageSource));
                }
            }
        }

        public string ThumbnailFileName
        {
            get => _thumbnailFileName;
            set
            {
                if (_thumbnailFileName != value)
                {
                    _thumbnailFileName = value;
                    _imageSource = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ImageSource));
                }
            }
        }

        public string OriginalFileName
        {
            get => _originalFileName;
            set { _originalFileName = value; OnPropertyChanged(); }
        }

        public int ImageWidth
        {
            get => _imageWidth;
            set { _imageWidth = value; OnPropertyChanged(); }
        }

        public int ImageHeight
        {
            get => _imageHeight;
            set { _imageHeight = value; OnPropertyChanged(); }
        }

        public long FileSizeBytes
        {
            get => _fileSizeBytes;
            set { _fileSizeBytes = value; OnPropertyChanged(); }
        }

        private bool _isImageLoading;
        private bool _isImageFailed;

        [JsonIgnore]
        public bool IsImageLoading
        {
            get => _isImageLoading;
            set { _isImageLoading = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsImageFailed
        {
            get => _isImageFailed;
            set { _isImageFailed = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool HasText => !string.IsNullOrWhiteSpace(_text);

        [JsonIgnore]
        public Windows.UI.Xaml.Visibility TextVisibility => HasText
            ? Windows.UI.Xaml.Visibility.Visible
            : Windows.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public bool HasImage => !string.IsNullOrEmpty(_imageBase64) || !string.IsNullOrEmpty(_localFileName);

        [JsonIgnore]
        public Windows.UI.Xaml.Visibility ImageVisibility => HasImage
            ? Windows.UI.Xaml.Visibility.Visible
            : Windows.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public BitmapImage ImageSource
        {
            get
            {
                if (_imageSource == null && HasImage && !_isImageFailed)
                {
                    LoadImageSourceAsync();
                }
                return _imageSource;
            }
        }

        private async void LoadImageSourceAsync()
        {
            if (_isImageLoading) return;
            IsImageLoading = true;
            IsImageFailed = false;

            try
            {
                // Prioritize fast, memory-efficient local thumbnail/image
                if (!string.IsNullOrEmpty(_thumbnailFileName) || !string.IsNullOrEmpty(_localFileName))
                {
                    var file = await WinAI.Services.ImageStorageService.Instance.GetThumbnailFileAsync(
                        !string.IsNullOrEmpty(_thumbnailFileName) ? _thumbnailFileName : _localFileName);

                    if (file == null && !string.IsNullOrEmpty(_localFileName))
                    {
                        file = await WinAI.Services.ImageStorageService.Instance.GetImageFileAsync(_localFileName);
                    }

                    if (file != null)
                    {
                        using (var stream = await file.OpenReadAsync())
                        {
                            var bmp = new BitmapImage();
                            bmp.DecodePixelWidth = 320;
                            await bmp.SetSourceAsync(stream);
                            _imageSource = bmp;
                            IsImageLoading = false;
                            IsImageFailed = false;
                            OnPropertyChanged(nameof(ImageSource));
                            return;
                        }
                    }
                }

                // Fallback to Base64 in memory
                if (!string.IsNullOrEmpty(_imageBase64))
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
                        bmp.DecodePixelWidth = 320;
                        await bmp.SetSourceAsync(stream);
                        _imageSource = bmp;
                        IsImageLoading = false;
                        IsImageFailed = false;
                        OnPropertyChanged(nameof(ImageSource));
                        return;
                    }
                }

                // If file not found and no base64, mark as failed
                IsImageLoading = false;
                IsImageFailed = true;
            }
            catch
            {
                IsImageLoading = false;
                IsImageFailed = true;
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
