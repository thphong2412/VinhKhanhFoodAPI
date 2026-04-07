using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls; // FIX LỖI: ContentPage, SelectionChangedEventArgs, CollectionView, EventArgs
using Microsoft.Maui.Devices.Sensors; // Hỗ trợ Geolocation (Vị trí)
using Microsoft.Maui.Dispatching; // FIX LỖI: IDispatcherTimer
using VinhKhanh.Services;
using PoiModel = VinhKhanh.Shared.PoiModel;

namespace VinhKhanh
{
    public partial class MainPage : ContentPage
    {
        private readonly ApiService _apiService;
        private List<PoiModel> _allPois = new List<PoiModel>();
        private IDispatcherTimer _gpsTimer;

        public MainPage()
        {
            InitializeComponent();
            _apiService = new ApiService();
            StartGpsTracking();
        }

        private async void OnLoadPoisClicked(object sender, EventArgs e)
        {
            try
            {
                var pois = await _apiService.GetPoisAsync();
                if (pois != null)
                {
                    _allPois = pois;
                    PoiList.ItemsSource = _allPois;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Lỗi", ex.Message, "OK");
            }
        }

        private void StartGpsTracking()
        {
            // Sử dụng Dispatcher để tạo Timer chạy ngầm
            _gpsTimer = Dispatcher.CreateTimer();
            _gpsTimer.Interval = TimeSpan.FromSeconds(5);
            _gpsTimer.Tick += async (s, e) => await CheckLocationAsync();
            _gpsTimer.Start();
        }

        private async Task CheckLocationAsync()
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));

                if (location != null && _allPois != null && _allPois.Any())
                {
                    foreach (var poi in _allPois)
                    {
                        Location poiLoc = new Location(poi.Latitude, poi.Longitude);
                        // Tính khoảng cách giữa người dùng và quán ăn
                        double distance = location.CalculateDistance(poiLoc, DistanceUnits.Kilometers) * 1000;

                        // Nếu cách dưới 50m thì tự động mở trang thuyết minh (DetailsPage)
                        if (distance < 50)
                        {
                            _gpsTimer.Stop();
                            await Navigation.PushAsync(new DetailsPage(poi));
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private async void OnPoiSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is PoiModel selectedPoi)
            {
                await Navigation.PushAsync(new DetailsPage(selectedPoi));

                // Reset lại lựa chọn để có thể bấm lại lần sau
                if (sender is CollectionView collectionView)
                {
                    collectionView.SelectedItem = null;
                }
            }
        }
    }
}