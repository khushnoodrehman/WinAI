using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WinAI.Data.Models;
using WinAI.Data.Services;
using WinAI.Services;

namespace WinAI.Views
{
    public class GalleryItemViewModel
    {
        public string Id { get; set; }
        public string MessageId { get; set; }
        public string ConversationId { get; set; }
        public string LocalFileName { get; set; }
        public string ThumbnailFileName { get; set; }
        public string OriginalFileName { get; set; }
        public long FileSizeBytes { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public BitmapImage ThumbnailSource { get; set; }

        public string FormattedDate => CreatedAtUtc.ToLocalTime().ToString("MMM d, h:mm tt");

        public string FormattedSize
        {
            get
            {
                if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024) return $"{(FileSizeBytes / 1024.0):F1} KB";
                return $"{(FileSizeBytes / (1024.0 * 1024.0)):F1} MB";
            }
        }
    }

    public sealed partial class ImagesGalleryPage : Page
    {
        private readonly ConversationService _conversationService = ConversationService.Instance;
        private readonly ImageStorageService _imageStorage = ImageStorageService.Instance;

        public ObservableCollection<GalleryItemViewModel> GalleryItems { get; } =
            new ObservableCollection<GalleryItemViewModel>();

        private GalleryItemViewModel _selectedItem;

        public ImagesGalleryPage()
        {
            this.InitializeComponent();
            ImagesGridView.ItemsSource = GalleryItems;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            UpdateStatusBar();

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested += OnBackRequested;
            navManager.AppViewBackButtonVisibility = Frame.CanGoBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;

            await LoadGalleryAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= OnBackRequested;
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (LightboxOverlay.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseLightbox();
                return;
            }

            if (NavDrawer != null && NavDrawer.IsPaneOpen)
            {
                e.Handled = true;
                NavDrawer.IsPaneOpen = false;
                return;
            }

            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private async Task LoadGalleryAsync()
        {
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;

            GalleryItems.Clear();

            try
            {
                var attachments = await _conversationService.GetAllAttachmentsAsync(limit: 200);

                if (attachments != null && attachments.Count > 0)
                {
                    foreach (var att in attachments)
                    {
                        var vm = new GalleryItemViewModel
                        {
                            Id = att.Id,
                            MessageId = att.MessageId,
                            ConversationId = att.ConversationId,
                            LocalFileName = att.LocalFileName,
                            ThumbnailFileName = att.ThumbnailFileName,
                            OriginalFileName = att.OriginalFileName,
                            FileSizeBytes = att.FileSizeBytes,
                            ImageWidth = att.ImageWidth,
                            ImageHeight = att.ImageHeight,
                            CreatedAtUtc = att.CreatedAtUtc
                        };

                        // Load thumbnail efficiently
                        await LoadThumbnailAsync(vm);
                        GalleryItems.Add(vm);
                    }

                    ImageCountText.Text = $"{GalleryItems.Count} image{(GalleryItems.Count == 1 ? "" : "s")} saved";
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ImageCountText.Text = "0 images saved";
                    EmptyStatePanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImagesGalleryPage] Load error: {ex.Message}");
                ImageCountText.Text = "Could not load images";
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadThumbnailAsync(GalleryItemViewModel item)
        {
            try
            {
                var file = await _imageStorage.GetThumbnailFileAsync(item.ThumbnailFileName);
                if (file == null && !string.IsNullOrEmpty(item.LocalFileName))
                {
                    file = await _imageStorage.GetImageFileAsync(item.LocalFileName);
                }

                if (file != null)
                {
                    using (var stream = await file.OpenReadAsync())
                    {
                        var bmp = new BitmapImage();
                        bmp.DecodePixelWidth = 240;
                        await bmp.SetSourceAsync(stream);
                        item.ThumbnailSource = bmp;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImagesGalleryPage] Thumbnail load error: {ex.Message}");
            }
        }

        private async void ImagesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GalleryItemViewModel item)
            {
                await OpenLightboxAsync(item);
            }
        }

        private async Task OpenLightboxAsync(GalleryItemViewModel item)
        {
            _selectedItem = item;
            LightboxTitleText.Text = string.IsNullOrWhiteSpace(item.OriginalFileName) ? "Image" : item.OriginalFileName;
            LightboxSubtitleText.Text = $"{item.ImageWidth}x{item.ImageHeight} • {item.FormattedSize} • {item.FormattedDate}";

            LightboxLoadingRing.IsActive = true;
            LightboxLoadingRing.Visibility = Visibility.Visible;
            LightboxImage.Source = null;
            LightboxScrollViewer.ChangeView(0, 0, 1.0f);
            if (ZoomLevelText != null) ZoomLevelText.Text = "100%";

            LightboxOverlay.Visibility = Visibility.Visible;

            if (LightboxScrollViewer.ActualWidth > 0 && LightboxScrollViewer.ActualHeight > 0 && LightboxImageContainer != null)
            {
                LightboxImageContainer.Width = LightboxScrollViewer.ActualWidth;
                LightboxImageContainer.Height = LightboxScrollViewer.ActualHeight;
            }

            try
            {
                var file = await _imageStorage.GetImageFileAsync(item.LocalFileName);
                if (file != null)
                {
                    using (var stream = await file.OpenReadAsync())
                    {
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(stream);
                        LightboxImage.Source = bmp;
                    }
                }
                else
                {
                    // Fallback to thumbnail if original is missing
                    if (item.ThumbnailSource != null)
                    {
                        LightboxImage.Source = item.ThumbnailSource;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImagesGalleryPage] Lightbox full image error: {ex.Message}");
            }
            finally
            {
                LightboxLoadingRing.IsActive = false;
                LightboxLoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void LightboxScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && LightboxImageContainer != null)
            {
                LightboxImageContainer.Width = e.NewSize.Width;
                LightboxImageContainer.Height = e.NewSize.Height;
            }
        }

        private void LightboxScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ZoomLevelText != null && LightboxScrollViewer != null)
            {
                ZoomLevelText.Text = $"{(int)(LightboxScrollViewer.ZoomFactor * 100)}%";
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (LightboxScrollViewer == null) return;
            float target = Math.Min(LightboxScrollViewer.ZoomFactor + 0.35f, 4.0f);
            LightboxScrollViewer.ChangeView(null, null, target);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (LightboxScrollViewer == null) return;
            float target = Math.Max(LightboxScrollViewer.ZoomFactor - 0.35f, 1.0f);
            LightboxScrollViewer.ChangeView(null, null, target);
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            if (LightboxScrollViewer == null) return;
            LightboxScrollViewer.ChangeView(0, 0, 1.0f);
        }

        private void LightboxImageContainer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (LightboxScrollViewer == null) return;
            float target = LightboxScrollViewer.ZoomFactor > 1.2f ? 1.0f : 2.5f;
            LightboxScrollViewer.ChangeView(null, null, target);
        }

        private void CloseLightbox_Click(object sender, RoutedEventArgs e)
        {
            CloseLightbox();
        }

        private void CloseLightbox()
        {
            LightboxOverlay.Visibility = Visibility.Collapsed;
            LightboxImage.Source = null;
            if (LightboxScrollViewer != null) LightboxScrollViewer.ChangeView(0, 0, 1.0f);
            if (ZoomLevelText != null) ZoomLevelText.Text = "100%";
            _selectedItem = null;
        }

        private void LightboxImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            LightboxLoadingRing.IsActive = false;
            LightboxLoadingRing.Visibility = Visibility.Collapsed;
        }

        private void LightboxImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            LightboxLoadingRing.IsActive = false;
            LightboxLoadingRing.Visibility = Visibility.Collapsed;
        }

        private async void GoToChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null && !string.IsNullOrWhiteSpace(_selectedItem.ConversationId))
            {
                string targetConvId = _selectedItem.ConversationId;
                try
                {
                    var conv = await _conversationService.GetConversationAsync(targetConvId);
                    if (conv != null)
                    {
                        CloseLightbox();
                        Frame.Navigate(typeof(MainPage), targetConvId);
                        return;
                    }
                }
                catch { }

                ShowNotification("Original conversation is no longer available.");
            }
            else
            {
                ShowNotification("Conversation reference not found.");
            }
        }

        private async void SaveToPictures_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            try
            {
                var file = await _imageStorage.GetImageFileAsync(_selectedItem.LocalFileName);
                if (file != null)
                {
                    var picturesFolder = KnownFolders.PicturesLibrary;
                    string targetName = !string.IsNullOrWhiteSpace(_selectedItem.OriginalFileName)
                        ? _selectedItem.OriginalFileName
                        : _selectedItem.LocalFileName;

                    await file.CopyAsync(picturesFolder, targetName, NameCollisionOption.GenerateUniqueName);
                    ShowNotification("Saved to Pictures Library!");
                }
                else
                {
                    ShowNotification("Original image file not found.");
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Could not save image: {ex.Message}");
            }
        }

        private async void DeleteCurrentImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            var confirmDialog = new ContentDialog
            {
                Title = "Delete Image?",
                Content = "This image will be permanently removed from your device storage and conversation attachment record.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel"
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string idToDelete = _selectedItem.Id;
                var itemObj = _selectedItem;

                CloseLightbox();

                bool deleted = await _conversationService.DeleteAttachmentAsync(idToDelete);
                if (deleted)
                {
                    GalleryItems.Remove(itemObj);
                    ImageCountText.Text = $"{GalleryItems.Count} image{(GalleryItems.Count == 1 ? "" : "s")} saved";

                    if (GalleryItems.Count == 0)
                    {
                        EmptyStatePanel.Visibility = Visibility.Visible;
                    }

                    ShowNotification("Image deleted successfully.");
                }
                else
                {
                    ShowNotification("Could not delete image.");
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadGalleryAsync();
        }

        private void StartNewChatButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage), "new");
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null) NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
        }

        private void ShowNotification(string message)
        {
            NotificationText.Text = message;
            NotificationBanner.Visibility = Visibility.Visible;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                NotificationBanner.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        }

        private void UpdateStatusBar()
        {
            try
            {
                var titleBar = ApplicationView.GetForCurrentView().TitleBar;
                if (titleBar != null)
                {
                    bool isDark = this.ActualTheme == ElementTheme.Dark ||
                                  (this.ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

                    var bg = isDark ? Color.FromArgb(255, 11, 12, 14) : Color.FromArgb(255, 248, 249, 250);
                    var fg = isDark ? Colors.White : Color.FromArgb(255, 26, 29, 32);

                    titleBar.BackgroundColor = bg;
                    titleBar.ForegroundColor = fg;
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonForegroundColor = fg;
                }
            }
            catch { }
        }

        #region Navigation Drawer Actions

        private void DrawerHome_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(HomePage));
        }

        private void DrawerNewChat_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(MainPage), "new");
        }

        private void DrawerConversations_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(ConversationsPage));
        }

        private void DrawerImages_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
        }

        private void DrawerProviders_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(AiProvidersPage));
        }

        private void DrawerVault_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void DrawerPromptKit_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(PromptKitPage));
        }

        private void DrawerSettings_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(SettingsPage));
        }

        private void DrawerPrivacy_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(PrivacyCenterPage));
        }

        private void DrawerAbout_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = false;
            Frame.Navigate(typeof(AboutPage));
        }

        #endregion
    }
}
