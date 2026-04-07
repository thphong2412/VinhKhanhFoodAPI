using VinhKhanh.Models;
using Microsoft.Maui.Controls; // QUAN TRỌNG: Dòng này để hết đỏ ContentPage
using VinhKhanh.PageModels;

namespace VinhKhanh.Pages
{
    public partial class ProjectDetailPage : ContentPage
    {
        public ProjectDetailPage(ProjectDetailPageModel model)
        {
            InitializeComponent();

            BindingContext = model;
        }
    }
}
