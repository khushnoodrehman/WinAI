using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinAI.Controls;
using WinAI.Models;
using WinAI.Services;

namespace WinAI.Views
{
    public sealed partial class ConversationsPage : Page
    {
        private readonly ChatHistoryService _historyService = ChatHistoryService.Instance;
        private List<ChatSession> _allSessions = new List<ChatSession>();
        private string _activeFilter = "all";
        private string _activeSort = "newest";

        public ConversationsPage()
        {
            this.InitializeComponent();
            _historyService.SessionsUpdated += HistoryService_SessionsUpdated;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            navManager.BackRequested += ConversationsPage_BackRequested;

            UpdateStatusBar();
            await LoadConversationsAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= ConversationsPage_BackRequested;
        }

        private void ConversationsPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (NavDrawer != null && NavDrawer.IsPaneOpen)
            {
                NavDrawer.IsPaneOpen = false;
                e.Handled = true;
                return;
            }

            e.Handled = true;
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(HomePage));
            }
        }

        private async void HistoryService_SessionsUpdated(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await LoadConversationsAsync();
            });
        }

        private async Task LoadConversationsAsync()
        {
            _allSessions = await _historyService.GetSessionsAsync();
            ApplyFiltersAndRender();
        }

        private void ApplyFiltersAndRender()
        {
            string query = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            IEnumerable<ChatSession> result = _allSessions;

            // 1. Text Search Filter
            if (!string.IsNullOrWhiteSpace(query))
            {
                result = result.Where(s =>
                    (s.Title != null && s.Title.ToLowerInvariant().Contains(query)) ||
                    (s.DisplayPreview != null && s.DisplayPreview.ToLowerInvariant().Contains(query)) ||
                    (s.ModelDisplayName != null && s.ModelDisplayName.ToLowerInvariant().Contains(query)));
            }

            // 2. Category Filter
            DateTime now = DateTime.Now;
            switch (_activeFilter)
            {
                case "today":
                    result = result.Where(s => s.UpdatedAt.Date == now.Date);
                    break;

                case "week":
                    DateTime oneWeekAgo = now.AddDays(-7);
                    result = result.Where(s => s.UpdatedAt >= oneWeekAgo);
                    break;

                case "pinned":
                    result = result.Where(s => s.IsPinned);
                    break;

                case "all":
                default:
                    break;
            }

            // 3. Sorting
            switch (_activeSort)
            {
                case "oldest":
                    result = result.OrderBy(s => s.UpdatedAt);
                    break;

                case "alpha":
                    result = result.OrderBy(s => s.Title);
                    break;

                case "newest":
                default:
                    // Pinned always on top, then newest
                    result = result.OrderByDescending(s => s.IsPinned).ThenByDescending(s => s.UpdatedAt);
                    break;
            }

            var finalItems = result.ToList();
            RenderList(finalItems, isSearching: !string.IsNullOrWhiteSpace(query));
        }

        private void RenderList(List<ChatSession> items, bool isSearching)
        {
            ConversationsContainer.Children.Clear();

            if (items.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                if (isSearching)
                {
                    EmptyStateTitleText.Text = "No conversations found";
                    EmptyStateSubText.Text = "Try another search term";
                }
                else if (_activeFilter == "pinned")
                {
                    EmptyStateTitleText.Text = "No pinned conversations";
                    EmptyStateSubText.Text = "Pin conversations using the ... menu";
                }
                else if (_activeFilter == "today")
                {
                    EmptyStateTitleText.Text = "No conversations today";
                    EmptyStateSubText.Text = "Start a new chat to begin";
                }
                else if (_activeFilter == "week")
                {
                    EmptyStateTitleText.Text = "No conversations this week";
                    EmptyStateSubText.Text = "Start a new chat to begin";
                }
                else
                {
                    EmptyStateTitleText.Text = "No conversations yet";
                    EmptyStateSubText.Text = "Tap + above to start a new chat";
                }
                return;
            }

            EmptyStatePanel.Visibility = Visibility.Collapsed;

            // Direct sequential rendering for alphabetical or oldest sort
            if (_activeSort == "alpha" || _activeSort == "oldest")
            {
                foreach (var session in items)
                {
                    AddRowItem(session);
                }
                return;
            }

            // Default: Group by date (Today, Yesterday, This Week, Older) in local timezone
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);
            DateTime weekAgo = today.AddDays(-7);

            var todayGroup = new List<ChatSession>();
            var yesterdayGroup = new List<ChatSession>();
            var thisWeekGroup = new List<ChatSession>();
            var olderGroup = new List<ChatSession>();

            foreach (var session in items)
            {
                DateTime localDate = session.UpdatedAt.Date;
                if (localDate == today)
                {
                    todayGroup.Add(session);
                }
                else if (localDate == yesterday)
                {
                    yesterdayGroup.Add(session);
                }
                else if (localDate >= weekAgo)
                {
                    thisWeekGroup.Add(session);
                }
                else
                {
                    olderGroup.Add(session);
                }
            }

            if (todayGroup.Count > 0)
            {
                ConversationsContainer.Children.Add(CreateDateHeader("Today"));
                foreach (var s in todayGroup) AddRowItem(s);
            }

            if (yesterdayGroup.Count > 0)
            {
                ConversationsContainer.Children.Add(CreateDateHeader("Yesterday"));
                foreach (var s in yesterdayGroup) AddRowItem(s);
            }

            if (thisWeekGroup.Count > 0)
            {
                ConversationsContainer.Children.Add(CreateDateHeader("This Week"));
                foreach (var s in thisWeekGroup) AddRowItem(s);
            }

            if (olderGroup.Count > 0)
            {
                ConversationsContainer.Children.Add(CreateDateHeader("Older"));
                foreach (var s in olderGroup) AddRowItem(s);
            }
        }

        private void AddRowItem(ChatSession session)
        {
            var rowItem = new ConversationRowItem
            {
                Session = session
            };

            rowItem.ItemClick += RowItem_ItemClick;
            rowItem.RenameClick += RowItem_RenameClick;
            rowItem.PinClick += RowItem_PinClick;
            rowItem.DeleteClick += RowItem_DeleteClick;

            ConversationsContainer.Children.Add(rowItem);
        }

        private FrameworkElement CreateDateHeader(string title)
        {
            var border = new Border
            {
                Padding = new Thickness(18, 14, 18, 4),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var textBlock = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = (Brush)Application.Current.Resources["AppTextSecondaryBrush"]
            };

            border.Child = textBlock;
            return border;
        }

        #region Actions: Item Click, Pin, Rename, Delete

        private void RowItem_ItemClick(object sender, ChatSession session)
        {
            if (session != null)
            {
                Frame.Navigate(typeof(MainPage), session.Id);
            }
        }

        private async void RowItem_PinClick(object sender, ChatSession session)
        {
            if (session == null) return;
            await _historyService.TogglePinSessionAsync(session.Id);
            await LoadConversationsAsync();
        }

        private async void RowItem_RenameClick(object sender, ChatSession session)
        {
            if (session == null) return;

            var renameBox = new TextBox
            {
                Text = session.Title ?? string.Empty,
                Margin = new Thickness(0, 10, 0, 0)
            };
            if (Application.Current.Resources.TryGetValue("WinAIStandardTextBoxStyle", out object tbStyle))
            {
                renameBox.Style = tbStyle as Style;
            }
            renameBox.GotFocus += (s, ev) => renameBox.SelectAll();

            var dialog = new ContentDialog
            {
                Title = "Rename Conversation",
                Content = renameBox,
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(renameBox.Text))
            {
                await _historyService.RenameSessionAsync(session.Id, renameBox.Text);
                await LoadConversationsAsync();
            }
        }

        private async void RowItem_DeleteClick(object sender, ChatSession session)
        {
            if (session == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Conversation?",
                Content = $"Are you sure you want to delete \"{session.Title}\"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _historyService.DeleteSessionAsync(session.Id);
                await LoadConversationsAsync();
            }
        }

        #endregion

        #region Search & Filter Controls

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
            ApplyFiltersAndRender();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void Filter_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string tag)
            {
                _activeFilter = tag;
                UpdateFilterPillStyles();
                ApplyFiltersAndRender();
            }
        }

        private void UpdateFilterPillStyles()
        {
            var accentBrush = (Brush)Application.Current.Resources["AppAccentBrush"];
            var surfaceBrush = (Brush)Application.Current.Resources["AppSurfaceBrush"];
            var borderBrush = (Brush)Application.Current.Resources["AppSurfaceBorderBrush"];
            var textSecBrush = (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
            var whiteBrush = new SolidColorBrush(Colors.White);

            SetPillStyle(FilterAllBorder, FilterAllText, _activeFilter == "all", accentBrush, surfaceBrush, borderBrush, whiteBrush, textSecBrush);
            SetPillStyle(FilterTodayBorder, FilterTodayText, _activeFilter == "today", accentBrush, surfaceBrush, borderBrush, whiteBrush, textSecBrush);
            SetPillStyle(FilterWeekBorder, FilterWeekText, _activeFilter == "week", accentBrush, surfaceBrush, borderBrush, whiteBrush, textSecBrush);
            SetPillStyle(FilterPinnedBorder, FilterPinnedText, _activeFilter == "pinned", accentBrush, surfaceBrush, borderBrush, whiteBrush, textSecBrush);
        }

        private void SetPillStyle(Border border, TextBlock textBlock, bool isSelected, Brush accent, Brush surface, Brush borderBrush, Brush white, Brush muted)
        {
            if (isSelected)
            {
                border.Background = accent;
                border.BorderThickness = new Thickness(0);
                textBlock.Foreground = white;
                textBlock.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                border.Background = surface;
                border.BorderBrush = borderBrush;
                border.BorderThickness = new Thickness(1);
                textBlock.Foreground = muted;
                textBlock.FontWeight = Windows.UI.Text.FontWeights.Normal;
            }
        }

        private void SortNewest_Click(object sender, RoutedEventArgs e)
        {
            _activeSort = "newest";
            ApplyFiltersAndRender();
        }

        private void SortOldest_Click(object sender, RoutedEventArgs e)
        {
            _activeSort = "oldest";
            ApplyFiltersAndRender();
        }

        private void SortAlpha_Click(object sender, RoutedEventArgs e)
        {
            _activeSort = "alpha";
            ApplyFiltersAndRender();
        }

        #endregion

        #region Navigation & Drawer Actions

        private void Hamburger_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
            }
        }

        private void NewConversation_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage), "new");
        }

        private void BottomHome_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(HomePage));
        }

        private void BottomVault_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void BottomMore_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
            }
        }

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
            catch
            {
                // Fallback
            }
        }
    }
}
