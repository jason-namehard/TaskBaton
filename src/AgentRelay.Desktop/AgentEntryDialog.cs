using System.IO;
using System.Windows;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public sealed record AgentEntryDraft(string Name, string Host, string ConfigPath, string? CliPath,
    string? HostVersion, string[] InstructionPaths);
public sealed record AgentCliDraft(string Executable, string[] Arguments, string? Input, string OutputFormat, string? WorkingDirectory,
    Dictionary<string, string>? Environment = null);

public sealed class AgentEntryDialog : Window
{
    public AgentEntryDraft? Result { get; private set; }
    public AgentCliDraft? CliResult { get; private set; }

    public AgentEntryDialog(Window owner, string language, AgentRecord? existing = null, RelayService? service = null)
    {
        var ui = new EntryDialogLayout(this, owner, EntryText.Get(existing is null ? "New Agent" : "Edit Agent", language), language);
        ui.Body.Children.Add(ui.Note("Name is required; all other information is optional."));
        ui.Body.Children.Add(ui.Note("Optional handoff rule: after adding an Agent, choose Install handoff rule. Relay can copy a deployment prompt for the Agent, or you can pick the instruction file yourself. Nothing is installed until the file and rule are confirmed."));
        var name = ui.Field(ui.Body, "Agent name", "AgentEntryName", existing?.DisplayName ?? existing?.AgentId ?? "");
        ui.Body.Children.Add(ui.Note("Choose any name. Roles are defined by you."));
        var advancedPanel = new StackPanel { Margin = new(10) };
        var advanced = new Expander { Header = ui.T("Advanced connection details"), Content = advancedPanel, Margin = new(0, 4, 0, 12) };
        ui.Body.Children.Add(advanced);
        advancedPanel.Children.Add(ui.Note("Manual handoff works without configuration paths. CLI capabilities are tested separately."));
        var config = ui.Field(advancedPanel, "Configuration path", "AgentEntryConfig", existing?.ConfigPath ?? "");
        var version = ui.Field(advancedPanel, "CLI version (optional)", "AgentEntryVersion", existing?.HostVersion ?? "");
        var instructions = ui.Field(advancedPanel, "Instruction files (one per line)", "AgentEntryInstructions",
            existing is null ? "" : string.Join(Environment.NewLine, existing.InstructionPaths), true);
        var cliPanel = new StackPanel { Margin = new(10) };
        var cliSection = new Expander { Header = ui.T("CLI calling method (optional)"), Content = cliPanel, Margin = new(0, 4, 0, 12) };
        ui.Body.Children.Add(cliSection);
        cliPanel.Children.Add(ui.Note("Record a program alone, or add arguments containing {prompt} to save a testable calling method. Put one argument on each line."));
        var priorRecipe = existing is null ? null : service?.CliExtension(existing.AgentId)?.Recipe;
        var cli = ui.Field(cliPanel, "CLI executable path", "AgentEntryCli", priorRecipe?.Create.Executable ?? existing?.CliPath ?? "");
        var arguments = ui.Field(cliPanel, "CLI arguments (one per line)", "AgentEntryCliArguments",
            priorRecipe is null ? "" : string.Join(Environment.NewLine, priorRecipe.Create.Arguments), true);
        var input = ui.Field(cliPanel, "Standard input template (optional)", "AgentEntryCliInput", priorRecipe?.Create.Input ?? "", true);
        var outputFormat = new ComboBox { Name = "AgentEntryOutputFormat", MinHeight = 40,
            ItemsSource = CliRecipes.OutputFormats };
        System.Windows.Automation.AutomationProperties.SetAutomationId(outputFormat, outputFormat.Name);
        outputFormat.SelectedItem = priorRecipe is null ? "text" : CliRecipes.OutputFormatFor(priorRecipe);
        cliPanel.Children.Add(new Label { Content = ui.T("CLI output format"), Target = outputFormat, Padding = new(0, 0, 0, 2) });
        cliPanel.Children.Add(outputFormat);
        var working = ui.Field(cliPanel, "CLI working directory", "AgentEntryWorkingDirectory", priorRecipe?.Create.WorkingDirectory ?? "");
        cliPanel.Children.Add(ui.Note("Some desktop Agents keep their models, providers and API keys apart from their command line. Point the command line at the desktop's own configuration here (for example a provider file or data folder) so it uses the same models; only if the command line truly stores keys separately does it need the key once more."));
        var environment = ui.Field(cliPanel, "Environment variables (NAME=value, one per line)", "AgentEntryEnvironment",
            priorRecipe?.Create.Environment is { } prior ? string.Join(Environment.NewLine, prior.Select(p => p.Key + "=" + p.Value)) : "", true);
        advanced.IsExpanded = existing is not null && new[] { config.Text, version.Text, instructions.Text }
            .Any(value => !string.IsNullOrWhiteSpace(value));
        cliSection.IsExpanded = existing is not null && !string.IsNullOrWhiteSpace(cli.Text);
        ui.Button(ui.Actions, "Save", "SaveAgentEntry", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new RelayException("agent_name_required", "Enter an Agent name.");
            var configPath = config.Text.Trim(); var cliPath = cli.Text.Trim();
            var instructionPaths = instructions.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if ((configPath.Length > 0 && !Path.IsPathFullyQualified(configPath)) ||
                instructionPaths.Any(p => !Path.IsPathFullyQualified(p)) ||
                (!string.IsNullOrWhiteSpace(working.Text) && !Path.IsPathFullyQualified(working.Text.Trim())))
                throw new RelayException("agent_path_required", "Use a full path for optional configuration and executable files.");
            var commandArguments = arguments.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (cliPath.Length > 0 && !Path.IsPathFullyQualified(cliPath) && commandArguments.Length == 0 && string.IsNullOrWhiteSpace(input.Text))
                throw new RelayException("cli_arguments_required", ui.T("A PATH command needs calling arguments; to record only a program, use its full path."));
            if (commandArguments.Length > 0 || !string.IsNullOrWhiteSpace(input.Text))
            {
                if (cliPath.Length == 0) throw new RelayException("cli_executable_required", ui.T("Enter a CLI program before its arguments."));
                if (!commandArguments.Any(argument => argument.Contains("{prompt}", StringComparison.Ordinal)) && !input.Text.Contains("{prompt}", StringComparison.Ordinal))
                    throw new RelayException("cli_prompt_required", ui.T("CLI arguments must include {prompt}."));
                var variables = new Dictionary<string, string>();
                foreach (var line in environment.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var split = line.IndexOf('=');
                    if (split <= 0) throw new RelayException("cli_environment_invalid", ui.T("Write each environment variable as NAME=value."));
                    variables[line[..split].Trim()] = line[(split + 1)..].Trim();
                }
                CliResult = new(cliPath, commandArguments, string.IsNullOrWhiteSpace(input.Text) ? null : input.Text, (string)outputFormat.SelectedItem,
                    string.IsNullOrWhiteSpace(working.Text) ? null : working.Text.Trim(), variables.Count == 0 ? null : variables);
            }
            Result = new(name.Text.Trim(), existing?.Host ?? "manual", configPath,
                cliPath.Length > 0 && Path.IsPathFullyQualified(cliPath) ? cliPath : null,
                string.IsNullOrWhiteSpace(version.Text) ? null : version.Text.Trim(), instructionPaths);
            DialogResult = true;
        }, true);
        var cancel = ui.Button(ui.Actions, "Cancel", "CancelAgentEntry", () => DialogResult = false); cancel.IsCancel = true;
    }

    internal static string? ResolveRelayCommand()
    {
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var published = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "relay.exe"));
        if (File.Exists(published)) return "& " + Quote(published);
        var publishedDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "relay.dll"));
        if (File.Exists(publishedDll)) return RuntimeCommand() + " " + Quote(publishedDll);
        // Source runs use an actual built CLI; never fabricate an executable path.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "AgentRelay.slnx"))) continue;
            var cli = Path.Combine(directory.FullName, "src", "AgentRelay.Cli", "bin", "Release", "net10.0", "relay.dll");
            if (!File.Exists(cli)) return null;
            return RuntimeCommand() + " " + Quote(cli);
        }
        return null;

        static string RuntimeCommand()
        {
            var runtime = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            return runtime is not null && File.Exists(runtime) ? "& " + Quote(runtime) : "dotnet";
        }
    }
}
