using System.Windows;

namespace TaskBaton.Desktop;

public partial class MainWindow
{
    private Task CopyBridgeDiagnostic(string targetAgentId)
    {
        Clipboard.SetText(service.BridgeDiagnosticPrompt(targetAgentId,AgentEntryDialog.ResolveRelayCommand()));
        return Task.CompletedTask;
    }
}
