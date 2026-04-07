using System; // FIX LỖI: Exception
using System.Threading; // FIX LỖI: SemaphoreSlim
using System.Threading.Tasks; // FIX LỖI: Task
using Microsoft.Maui.Controls; // FIX LỖI: Shell
using VinhKhanh.Services; // Hoặc VinhKhanh.Interfaces nếu ông để ở đó

namespace VinhKhanh.Services
{
    /// <summary>
    /// Modal Error Handler.
    /// </summary>
    public class ModalErrorHandler : IErrorHandler
    {
        // SemaphoreSlim nằm trong System.Threading
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>
        /// Handle error in UI.
        /// </summary>
        /// <param name="ex">Exception.</param>
        public void HandleError(Exception ex)
        {
            // Đảm bảo ông đã có Extension Method FireAndForgetSafeAsync trong project
            DisplayAlertAsync(ex).FireAndForgetSafeAsync();
        }

        private async Task DisplayAlertAsync(Exception ex)
        {
            try
            {
                await _semaphore.WaitAsync();

                // Shell nằm trong Microsoft.Maui.Controls
                if (Shell.Current is not null)
                {
                    await Shell.Current.DisplayAlert("Error", ex.Message, "OK");
                }
            }
            catch
            {
                // Tránh việc chính hàm báo lỗi cũng bị crash
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}