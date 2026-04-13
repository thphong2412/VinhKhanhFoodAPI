using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class HighlightsListPage : ContentPage
    {
        public HighlightsListPage(List<PoiModel> items)
        {
            InitializeComponent();
            if (items != null)
            {
                // Convert POI list to simple highlight viewmodels (use DB service via constructor param in real app)
                var vm = new System.Collections.ObjectModel.ObservableCollection<VinhKhanh.Shared.HighlightViewModel>();
                foreach (var p in items)
                {
                    vm.Add(new VinhKhanh.Shared.HighlightViewModel
                    {
                        Poi = p,
                        ImageUrl = p.ImageUrl,
                        Name = p.Name,
                        Address = string.Empty,
                        RatingDisplay = string.Empty,
                        ReviewCount = 0,
                        OpeningHours = string.Empty,
                        OpenStatus = string.Empty,
                        OpenStatusColorHex = string.Empty
                    });
                }
                CvAllHighlights.ItemsSource = vm;
            }
        }

        private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection != null && e.CurrentSelection.Count > 0)
            {
                var poi = e.CurrentSelection[0] as PoiModel;
                if (poi != null)
                {
                    await Navigation.PushAsync(new DetailsPage(poi));
                    // deselect to allow reselect
                    if (sender is CollectionView cv) cv.SelectedItem = null;
                }
            }
        }
    }
}
