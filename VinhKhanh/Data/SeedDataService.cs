using System; // FIX LỖI: Exception, Console
using System.IO; // FIX LỖI: Stream
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage; // FIX LỖI: FileSystem
using VinhKhanh.Models;

namespace VinhKhanh.Data
{
    public class SeedDataService
    {
        private readonly ProjectRepository _projectRepository;
        private readonly TaskRepository _taskRepository;
        private readonly TagRepository _tagRepository;
        private readonly CategoryRepository _categoryRepository;
        private readonly string _seedDataFilePath = "SeedData.json";
        private readonly ILogger<SeedDataService> _logger;

        public SeedDataService(
            ProjectRepository projectRepository,
            TaskRepository taskRepository,
            TagRepository tagRepository,
            CategoryRepository categoryRepository,
            ILogger<SeedDataService> logger)
        {
            _projectRepository = projectRepository;
            _taskRepository = taskRepository;
            _tagRepository = tagRepository;
            _categoryRepository = categoryRepository;
            _logger = logger;
        }

        public async Task LoadSeedDataAsync()
        {
            // Sửa thành await để đảm bảo bảng cũ bị xóa xong mới nạp dữ liệu mới
            await ClearTables();

            try
            {
                using Stream templateStream = await FileSystem.OpenAppPackageFileAsync(_seedDataFilePath);

                // Giả sử JsonContext của ông đã được định nghĩa đúng cho dự án
                var payload = await JsonSerializer.DeserializeAsync(templateStream, JsonContext.Default.ProjectsJson);

                if (payload is not null)
                {
                    foreach (var project in payload.Projects)
                    {
                        if (project is null) continue;

                        if (project.Category is not null)
                        {
                            await _categoryRepository.SaveItemAsync(project.Category);
                            project.CategoryID = project.Category.ID;
                        }

                        await _projectRepository.SaveItemAsync(project);

                        if (project.Tasks is not null)
                        {
                            foreach (var task in project.Tasks)
                            {
                                task.ProjectID = project.ID;
                                await _taskRepository.SaveItemAsync(task);
                            }
                        }

                        if (project.Tags is not null)
                        {
                            foreach (var tag in project.Tags)
                            {
                                await _tagRepository.SaveItemAsync(tag, project.ID);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error processing seed data");
                throw;
            }
        }

        // Đổi async void thành async Task để await được ở trên
        private async Task ClearTables()
        {
            try
            {
                await Task.WhenAll(
                    _projectRepository.DropTableAsync(),
                    _taskRepository.DropTableAsync(),
                    _tagRepository.DropTableAsync(),
                    _categoryRepository.DropTableAsync());
            }
            catch (Exception e)
            {
                // Dòng 88 hết đỏ nhờ using System;
                Console.WriteLine($"Lỗi khi xóa bảng: {e.Message}");
            }
        }
    }
}