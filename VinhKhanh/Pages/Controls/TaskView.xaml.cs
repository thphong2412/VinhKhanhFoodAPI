using System.Windows.Input;
using Microsoft.Maui.Controls;
using VinhKhanh.Models;

namespace VinhKhanh.Pages.Controls
{
    public partial class TaskView : ContentView // ĐÃ KHỚP VỚI XAML
    {
        public TaskView()
        {
            InitializeComponent();
        }

        public static readonly BindableProperty TaskCompletedCommandProperty =
            BindableProperty.Create(nameof(TaskCompletedCommand), typeof(ICommand), typeof(TaskView), null);

        public ICommand TaskCompletedCommand
        {
            get => (ICommand)GetValue(TaskCompletedCommandProperty);
            set => SetValue(TaskCompletedCommandProperty, value);
        }

        private void CheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            if (BindingContext is not ProjectTask task) return;
            if (task.IsCompleted == e.Value) return;

            task.IsCompleted = e.Value;
            if (TaskCompletedCommand != null && TaskCompletedCommand.CanExecute(task))
            {
                TaskCompletedCommand.Execute(task);
            }
        }
    }
}