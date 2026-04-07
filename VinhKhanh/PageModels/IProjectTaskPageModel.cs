using CommunityToolkit.Mvvm.Input;
using VinhKhanh.Models;

namespace VinhKhanh.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}