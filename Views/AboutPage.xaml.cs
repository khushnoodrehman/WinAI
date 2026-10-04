using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WinAI.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                AppVersionText.Text = $"Build {v.Major}.{v.Minor}.{v.Build}.{v.Revision} (Fall Creators Update)";
            }
            catch { }

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested += OnBackRequested;
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
        }

        private async void GithubProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new System.Uri("https://github.com/khushnoodrehman"));
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= OnBackRequested;
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(HomePage));
            }
        }

        private async void RunTestsButton_Click(object sender, RoutedEventArgs e)
        {
            RunTestsButton.IsEnabled = false;
            TestResultsText.Visibility = Visibility.Visible;
            TestResultsText.Text = "Running verification tests...";

            try
            {
                var dbReport = await WinAI.Data.Tests.ConversationDatabaseTests.RunAllTestsAsync();
                var switcherReport = await WinAI.Data.Tests.ModelSwitcherArchitectureTests.RunAllTestsAsync();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== DATABASE TESTS: {dbReport.PassedCount}/{dbReport.TotalTests} Passed ({(dbReport.AllPassed ? "PASS" : "FAIL")}) ===");
                foreach (var res in dbReport.Results)
                {
                    sb.AppendLine($"• [{(res.Passed ? "PASS" : "FAIL")}] {res.TestName}{(res.Passed ? "" : " - " + res.ErrorMessage)}");
                }
                sb.AppendLine();
                sb.AppendLine($"=== MODEL SWITCHER ARCHITECTURE: {switcherReport.PassedCount}/{switcherReport.TotalTests} Passed ({(switcherReport.AllPassed ? "PASS" : "FAIL")}) ===");
                foreach (var res in switcherReport.Results)
                {
                    sb.AppendLine($"• [{(res.Passed ? "PASS" : "FAIL")}] {res.TestName}{(res.Passed ? "" : " - " + res.ErrorMessage)}");
                }
                TestResultsText.Text = sb.ToString();
            }
            catch (System.Exception ex)
            {
                TestResultsText.Text = $"Test execution error: {ex.Message}";
            }
            finally
            {
                RunTestsButton.IsEnabled = true;
            }
        }
    }
}
