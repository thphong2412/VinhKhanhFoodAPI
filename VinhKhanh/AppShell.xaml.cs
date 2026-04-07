using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls; // FIX LỖI: Shell, Application, AppTheme
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics; // FIX LỖI: Color, Colors
using System;
using System.Threading;
using System.Threading.Tasks;
using Font = Microsoft.Maui.Font;

namespace VinhKhanh
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Kiểm tra theme hiện tại của hệ thống
            if (Application.Current != null)
            {
                var currentTheme = Application.Current.RequestedTheme;
                // Nếu ông có đặt tên x:Name cho SegmentedControl trong XAML là ThemeSegmentedControl
                // ThemeSegmentedControl.SelectedIndex = currentTheme == AppTheme.Light ? 0 : 1;
            }
        }

        public static async Task DisplaySnackbarAsync(string message)
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            var snackbarOptions = new SnackbarOptions
            {
                BackgroundColor = Color.FromArgb("#FF3300"),
                TextColor = Colors.White,
                ActionButtonTextColor = Colors.Yellow,
                CornerRadius = new CornerRadius(0),
                Font = Font.SystemFontOfSize(18),
                ActionButtonFont = Font.SystemFontOfSize(14)
            };

            var snackbar = Snackbar.Make(message, visualOptions: snackbarOptions);
            await snackbar.Show(cts.Token);
        }

        public static async Task DisplayToastAsync(string message)
        {
            // Toast không chạy trên Windows, chỉ Android/iOS
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
                return;

            var toast = Toast.Make(message, CommunityToolkit.Maui.Core.ToastDuration.Short, 18);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await toast.Show(cts.Token);
        }

        private void SfSegmentedControl_SelectionChanged(object sender, Syncfusion.Maui.Toolkit.SegmentedControl.SelectionChangedEventArgs e)
        {
            if (Application.Current != null)
            {
                Application.Current.UserAppTheme = e.NewIndex == 0 ? AppTheme.Light : AppTheme.Dark;
            }
        }
    }
}