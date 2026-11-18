using System.IO;
using System.Windows;
using System.Windows.Controls;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

public sealed record AgentEntryDraft(string Name, string Host, string ConfigPath, string? CliPath,
    string? HostVersion, string[] InstructionPaths);
public sealed record AgentCliDraft(string Executable, string[] Arguments, string? Input, string OutputFormat, string? WorkingDirectory,
    Dictionary<string, string>? Environment = null, string? ResumeExecutable = null, string[]? ResumeArguments = null,
    string? ResumeInput = null, string? PresetId = null);

public sealed class AgentEntryDialog : Window
{
    public AgentEntryDraft? Result { get; private set; }
    public AgentCliDraft? CliResult { get; private set; }
    private CliRecipePreset? selectedPreset;

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
        var preset = new ComboBox { Name = "AgentEntryPreset", MinHeight = 40, ItemsSource = CliRecipePresetCatalog.All,
            DisplayMemberPath = "Name", SelectedValuePath = "Id" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(preset, preset.Name);
        cliPanel.Children.Add(new Label { Content = ui.T("Preset (optional)"), Target = preset, Padding = new(0, 0, 0, 2) });
        cliPanel.Children.Add(preset);
        var presetNote = ui.Note("Choose a preset to fill the command fields, then review them before saving. A preset is not a verified connection; the doctor or a probe still decides what actually works.");
        presetNote.Name = "AgentEntryPresetNote";
        cliPanel.Children.Add(presetNote);
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
        var resumePanel = new StackPanel { Margin = new(10) };
        cliPanel.Children.Add(new Expander { Header = ui.T("Same-session resume (optional)"), Content = resumePanel, Margin = new(0, 4, 0, 8) });
        resumePanel.Children.Add(ui.Note("Resume must receive the exact {session_id}; leave it empty when the host cannot resume by ID."));
        var resumeExecutable = ui.Field(resumePanel, "Resume executable", "AgentEntryResumeExecutable", priorRecipe?.Resume?.Executable ?? "");
        var resumeArguments = ui.Field(resumePanel, "Resume arguments (one per line)", "AgentEntryResumeArguments",
            priorRecipe?.Resume is { } resume ? string.Join(Environment.NewLine, resume.Arguments) : "", true);
        var resumeInput = ui.Field(resumePanel, "Resume standard input template (optional)", "AgentEntryResumeInput", priorRecipe?.Resume?.Input ?? "", true);
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not CliRecipePreset selected) return;
            selectedPreset = selected;
            cli.Text = selected.Executable;
            arguments.Text = string.Join(Environment.NewLine, selected.Arguments);
            resumeExecutable.Text = selected.ResumeExecutable ?? "";
            resumeArguments.Text = selected.ResumeArguments is null ? "" : string.Join(Environment.NewLine, selected.ResumeArguments);
            input.Text = "";
            resumeInput.Text = "";
            outputFormat.SelectedItem = selected.OutputFormat;
            presetNote.Text = selected.Description + "\n" + selected.Notes + "\n" +
                string.Format(System.Globalization.CultureInfo.CurrentUICulture, "Requires: {0} · Source: {1} · {2}",
                    string.Join(", ", selected.Requires), selected.Source, selected.License);
        };
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
            var resumePath = resumeExecutable.Text.Trim();
            var resumeCommands = resumeArguments.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resumeInputText = resumeInput.Text.Trim();
            if (resumePath.Length > 0)
            {
                if (!resumeCommands.Any(argument => argument.Contains("{session_id}", StringComparison.Ordinal)) &&
                    !resumeInputText.Contains("{session_id}", StringComparison.Ordinal))
                    throw new RelayException("cli_resume_session_required", ui.T("The resume command must include {session_id}."));
                if (!resumeCommands.Any(argument => argument.Contains("{prompt}", StringComparison.Ordinal)) &&
                    !resumeInputText.Contains("{prompt}", StringComparison.Ordinal))
                    throw new RelayException("cli_resume_prompt_required", ui.T("The resume command must include {prompt}."));
            }
            else if (resumeCommands.Length > 0)
                throw new RelayException("cli_resume_executable_required", ui.T("Enter a resume executable before its arguments."));
            var acp = (string)outputFormat.SelectedItem is "acp" or "dsh-desktop";
            if (!acp && cliPath.Length > 0 && !Path.IsPathFullyQualified(cliPath) && commandArguments.Length == 0 && string.IsNullOrWhiteSpace(input.Text))
                throw new RelayException("cli_arguments_required", ui.T("A PATH command needs calling arguments; to record only a program, use its full path."));
            if (acp && cliPath.Length == 0)
                throw new RelayException("cli_executable_required", ui.T("Enter a CLI program before its arguments."));
            if (acp || commandArguments.Length > 0 || !string.IsNullOrWhiteSpace(input.Text))
            {
                if (cliPath.Length == 0) throw new RelayException("cli_executable_required", ui.T("Enter a CLI program before its arguments."));
                if (!acp && !commandArguments.Any(argument => argument.Contains("{prompt}", StringComparison.Ordinal)) && !input.Text.Contains("{prompt}", StringComparison.Ordinal))
                    throw new RelayException("cli_prompt_required", ui.T("CLI arguments must include {prompt}."));
                var variables = new Dictionary<string, string>();
                foreach (var line in environment.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var split = line.IndexOf('=');
                    if (split <= 0) throw new RelayException("cli_environment_invalid", ui.T("Write each environment variable as NAME=value."));
                    variables[line[..split].Trim()] = line[(split + 1)..].Trim();
                }
                CliResult = new(cliPath, commandArguments, string.IsNullOrWhiteSpace(input.Text) ? null : input.Text, (string)outputFormat.SelectedItem,
                    string.IsNullOrWhiteSpace(working.Text) ? null : working.Text.Trim(), variables.Count == 0 ? null : variables,
                    resumePath.Length == 0 ? null : resumePath, resumeCommands.Length == 0 ? null : resumeCommands,
                    string.IsNullOrWhiteSpace(resumeInputText) ? null : resumeInputText, selectedPreset?.Id);
            }
            Result = new(name.Text.Trim(), selectedPreset?.Host ?? existing?.Host ?? "manual", configPath,
                cliPath.Length > 0 && Path.IsPathFullyQualified(cliPath) ? cliPath : null,
                string.IsNullOrWhiteSpace(version.Text) ? null : version.Text.Trim(), instructionPaths);
            DialogResult = true;
        }, true);
        var cancel = ui.Button(ui.Actions, "Cancel", "CancelAgentEntry", () => DialogResult = false); cancel.IsCancel = true;
    }

    internal static string? ResolveRelayCommand()
    {
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var published = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "taskbaton.exe"));
        if (!File.Exists(published)) published = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "relay.exe"));
        if (File.Exists(published)) return "& " + Quote(published);
        var publishedDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "taskbaton.dll"));
        if (!File.Exists(publishedDll)) publishedDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "relay.dll"));
        if (File.Exists(publishedDll)) return RuntimeCommand() + " " + Quote(publishedDll);
        // Source runs use an actual built CLI; never fabricate an executable path.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "TaskBaton.slnx"))) continue;
            var cli = Path.Combine(directory.FullName, "src", "TaskBaton.Cli", "bin", "Release", "net10.0", "taskbaton.dll");
            if (!File.Exists(cli)) cli = Path.Combine(directory.FullName, "src", "TaskBaton.Cli", "bin", "Release", "net10.0", "relay.dll");
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
