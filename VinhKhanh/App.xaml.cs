using Microsoft.Maui.Controls;
namespace VinhKhanh
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Sửa dòng này để dùng AppShell - giúp hiện Menu và Bản đồ làm trang chủ
            MainPage = new AppShell();
        }
    }
}