using System;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WinAI.Controls
{
    public sealed class AttachmentResult
    {
        public StorageFile File { get; set; }
        public string Base64Data { get; set; }
        public string MimeType { get; set; }
        public string FileName { get; set; }
        public BitmapImage Thumbnail { get; set; }
    }

    /// <summary>
    /// Reusable Windows-style compact attachment flyout supporting
    /// Camera capture (with preview, retake, cancel, attach) and Gallery photo picking.
    /// Enforces model capability checks ("This model does not support image input.").
    /// </summary>
    public sealed class WinAIAttachmentFlyout : Flyout
    {
        public bool ModelSupportsVision { get; set; } = true;

        public event EventHandler<AttachmentResult> ImageAttached;
        public event EventHandler<string> AttachmentError;

        private Button _cameraButton;
        private Button _galleryButton;

        public WinAIAttachmentFlyout()
        {
            Content = BuildContent();
        }

        private UIElement BuildContent()
        {
            var root = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 170,
                Padding = new Thickness(0, 4, 0, 4)
            };

            // Option 1: Camera
            _cameraButton = CreateMenuButton("\uE722", "Camera", OnCameraClicked);
            root.Children.Add(_cameraButton);

            // Option 2: Gallery
            _galleryButton = CreateMenuButton("\uEB9F", "Gallery", OnGalleryClicked);
            root.Children.Add(_galleryButton);

            return root;
        }

        private Button CreateMenuButton(string iconGlyph, string labelText, RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 44,
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };

            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = (Brush)Application.Current.Resources["AppAccentBrush"],
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 15,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };

            sp.Children.Add(icon);
            sp.Children.Add(label);
            button.Content = sp;
            button.Click += clickHandler;

            return button;
        }

        public void ShowAt(FrameworkElement target, bool supportsVision)
        {
            ModelSupportsVision = supportsVision;

            if (!supportsVision)
            {
                AttachmentError?.Invoke(this, "This model does not support image input.");
                return;
            }

            base.ShowAt(target);
        }

        private async void OnCameraClicked(object sender, RoutedEventArgs e)
        {
            Hide();

            if (!ModelSupportsVision)
            {
                AttachmentError?.Invoke(this, "This model does not support image input.");
                return;
            }

            await LaunchCameraAsync();
        }

        private async void OnGalleryClicked(object sender, RoutedEventArgs e)
        {
            Hide();

            if (!ModelSupportsVision)
            {
                AttachmentError?.Invoke(this, "This model does not support image input.");
                return;
            }

            await LaunchGalleryAsync();
        }

        public async Task LaunchCameraAsync()
        {
            try
            {
                var cameraUI = new CameraCaptureUI();
                cameraUI.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Jpeg;
                cameraUI.PhotoSettings.AllowCropping = false;

                StorageFile capturedFile = await cameraUI.CaptureFileAsync(CameraCaptureUIMode.Photo);
                if (capturedFile == null)
                {
                    // User dismissed or canceled camera capture
                    return;
                }

                await ShowCameraPreviewDialogAsync(capturedFile);
            }
            catch (UnauthorizedAccessException)
            {
                AttachmentError?.Invoke(this, "Camera permission is required.");
            }
            catch (Exception ex)
            {
                uint hr = (uint)ex.HResult;
                if (hr == 0x80070005)
                {
                    AttachmentError?.Invoke(this, "Camera permission is required.");
                }
                else
                {
                    AttachmentError?.Invoke(this, "Camera could not be accessed.");
                }
            }
        }

        private async Task ShowCameraPreviewDialogAsync(StorageFile file)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var stream = await file.OpenReadAsync())
                {
                    await bmp.SetSourceAsync(stream);
                }

                var previewImage = new Image
                {
                    Source = bmp,
                    MaxHeight = 260,
                    MaxWidth = 260,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 10)
                };

                var dialog = new ContentDialog
                {
                    Title = "Photo Preview",
                    Content = previewImage,
                    PrimaryButtonText = "Attach",
                    SecondaryButtonText = "Retake",
                    CloseButtonText = "Cancel"
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    var attachment = await ProcessFileAsync(file);
                    ImageAttached?.Invoke(this, attachment);
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    try { await file.DeleteAsync(); } catch { }
                    await LaunchCameraAsync();
                }
                else
                {
                    try { await file.DeleteAsync(); } catch { }
                }
            }
            catch (Exception ex)
            {
                AttachmentError?.Invoke(this, $"Could not preview image: {ex.Message}");
            }
        }

        public async Task LaunchGalleryAsync()
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".webp");

                StorageFile file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    return; // User canceled
                }

                var attachment = await ProcessFileAsync(file);
                ImageAttached?.Invoke(this, attachment);
            }
            catch (Exception ex)
            {
                AttachmentError?.Invoke(this, $"Could not open image: {ex.Message}");
            }
        }

        public static async Task<AttachmentResult> ProcessFileAsync(StorageFile file)
        {
            string ext = file.FileType.ToLowerInvariant();
            string mimeType = "image/jpeg";
            if (ext == ".png") mimeType = "image/png";
            else if (ext == ".webp") mimeType = "image/webp";
            else if (ext == ".bmp") mimeType = "image/bmp";
            else if (ext == ".gif") mimeType = "image/gif";

            byte[] bytes;
            using (var stream = await file.OpenReadAsync())
            {
                bytes = new byte[stream.Size];
                using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                }
            }

            var bmp = new BitmapImage();
            using (var stream = await file.OpenReadAsync())
            {
                await bmp.SetSourceAsync(stream);
            }

            return new AttachmentResult
            {
                File = file,
                Base64Data = Convert.ToBase64String(bytes),
                MimeType = mimeType,
                FileName = file.Name,
                Thumbnail = bmp
            };
        }
    }
}
