using Microsoft.Maui.Controls; // QUAN TRỌNG: Dòng này để hết đỏ ContentPage
using VinhKhanh.PageModels;
namespace VinhKhanh.Pages
{
    public partial class ProjectListPage : ContentPage
    {
        public ProjectListPage(ProjectListPageModel model)
        {
            
            InitializeComponent();
            BindingContext = model;

        }
    }
}