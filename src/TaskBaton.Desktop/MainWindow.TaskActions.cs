using System.Windows;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public partial class MainWindow
{
    private void AddTaskOperatorActions(Panel actions, TaskRecord task)
    {
        if (task.State != TaskState.Succeeded)
            Add(actions, "Mark handoff complete", () =>
            {
                if (ThemedMessageBox.Show(this, T("Mark this handoff complete yourself? Relay will stop waiting and record your decision. This does not verify the Agent's work or stop an already running Agent."),
                    T("Mark handoff complete"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                { reviewCancelled = true; return Task.CompletedTask; }
                service.ForceCompleteTask(task.TaskId, "Marked complete by user", true);
                return Task.CompletedTask;
            });
        var delete = Add(actions, "Delete task", () =>
        {
            if (ThemedMessageBox.Show(this, T("Delete this task and its history? Files will be kept; unregistered relay files remain eligible for retention cleanup. This does not stop an already running Agent. This action cannot be undone."),
                T("Delete task"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            { reviewCancelled = true; return Task.CompletedTask; }
            service.DeleteTask(task.TaskId, true);
            taskId = "";
            return Task.CompletedTask;
        });
        delete.Content = Icons.Create("Trash", size: 17);
        delete.ToolTip = T("Delete task");
        delete.SetResourceReference(StyleProperty, "IconButtonStyle");
    }
}
