using System; // FIX LỖI: Exception
using System.Threading.Tasks; // FIX LỖI: Task
using VinhKhanh.Services;

namespace VinhKhanh.Utilities
{
    /// <summary>
    /// Task Utilities.
    /// </summary>
    public static class TaskUtilities
    {
        /// <summary>
        /// Fire and Forget Safe Async.
        /// </summary>
        /// <param name="task">Task to Fire and Forget.</param>
        /// <param name="handler">Error Handler.</param>
        public static async void FireAndForgetSafeAsync(this Task task, IErrorHandler handler = null)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                // Gọi handler để xử lý lỗi (ví dụ: ghi log hoặc hiện thông báo)
                handler?.HandleError(ex);
            }
        }
    }
}