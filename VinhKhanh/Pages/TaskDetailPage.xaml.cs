using Microsoft.Maui.Controls; // QUAN TRỌNG: Thêm dòng này để hết đỏ ContentPage
using VinhKhanh.PageModels;
namespace VinhKhanh.Pages
{
    public partial class TaskDetailPage : ContentPage
    {
        public TaskDetailPage(TaskDetailPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}