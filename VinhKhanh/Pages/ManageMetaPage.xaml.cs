using Microsoft.Maui.Controls; // FIX LỖI: ContentPage
using VinhKhanh.PageModels;
namespace VinhKhanh.Pages
{
    public partial class ManageMetaPage : ContentPage
    {
        public ManageMetaPage(ManageMetaPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}