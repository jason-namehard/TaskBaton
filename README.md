# Agent Relay

**English** | [简体中文](README.zh.md)

[![CI](https://github.com/jason-namehard/agent-relay/actions/workflows/ci.yml/badge.svg)](https://github.com/jason-namehard/agent-relay/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/jason-namehard/agent-relay?include_prereleases&label=latest)](https://github.com/jason-namehard/agent-relay/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT.svg)](LICENSE)

A local handoff desk for people who work with several AI agents. Pass a file or request from A to B, see who holds the task now, and stop the loop when the work is actually done.

![Task board after a completed handoff](docs/images/ui-tasks.png)

If you use more than one agent, the work usually moves by copy and paste: you paste a request into one session, wait, copy the answer into another, and lose track of which session received what, whether it was picked up, and whether a changed file means the work is finished. Agent Relay keeps each handoff as a task in a local relay folder with a SQLite ledger, shows its progress in a Windows desktop app, and separates "the message was submitted" from "the task was completed with a receipt". You decide the agents and their roles; Relay only needs to know who sends and who receives.

Current release: `0.3.0`, with a Windows x64 desktop installer. The CLI and stdio MCP server have Windows and Linux CI coverage. There is no macOS desktop package, no cloud relay and no background service after every process closes.

## Quick start

### Windows installer

Download [Agent Relay 0.3.0 for Windows x64](https://github.com/jason-namehard/agent-relay/releases/tag/v0.3.0), or run in PowerShell:

```powershell
Invoke-WebRequest 'https://github.com/jason-namehard/agent-relay/releases/download/v0.3.0/AgentRelay-v0.3.0-windows-x64-setup.exe' -UseBasicParsing -OutFile 'AgentRelay-setup.exe'
Start-Process '.\AgentRelay-setup.exe' -Wait
```

The unsigned installer defaults to a per-user installation under `%LOCALAPPDATA%\Programs\Agent Relay` and bundles the .NET runtime for the desktop app, CLI and MCP server. Start it from the Start menu, or run:

```powershell
& "$env:LOCALAPPDATA\Programs\Agent Relay\app\gui\AgentRelay.Desktop.exe"
& "$env:LOCALAPPDATA\Programs\Agent Relay\app\relay.exe" --help    # app\mcp\relay-mcp.exe is self-contained too
```

Isolated installers built from the 0.1.1 and 0.1.2 revisions passed an upgrade test. Uninstall tests covered keeping data, removing rules, clearing the ledger, a damaged rule and a missing location record. The original distributed installers have not been tested together in an isolated virtual machine. Exit Agent Relay from the tray before upgrading.

### Older preview `0.1.0-preview.22`

Download the ZIP from the [release page](https://github.com/jason-namehard/agent-relay/releases/tag/v0.1.0-preview.22) or with PowerShell. Extract the whole archive into a new folder; do not run files from inside the ZIP.

```powershell
$version = 'v0.1.0-preview.22'
$zip = "AgentRelay-$version-windows-x64.zip"
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest "https://github.com/jason-namehard/agent-relay/releases/download/$version/$zip" -UseBasicParsing -OutFile $zip
Expand-Archive $zip -DestinationPath ".\AgentRelay-$version"
Set-Location ".\AgentRelay-$version"

.\TryDemo.cmd                    # isolated demo: sample Agents, preset tasks, no model account, no model calls
.\gui\AgentRelay.Desktop.exe     # normal use
```

<details>
<summary>Verify the downloaded ZIP against the GitHub asset digest</summary>

```powershell
$release = Invoke-RestMethod "https://api.github.com/repos/jason-namehard/agent-relay/releases/tags/$version"
$asset = $release.assets | Where-Object name -eq $zip
if (-not $asset.digest) { throw 'The release API did not provide an asset digest.' }
$actual = 'sha256:' + (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $asset.digest) { throw 'The file does not match the GitHub asset.' }
```

Local and CI builds are not claimed to be byte-for-byte reproducible.

</details>

Requirements: the installer and the desktop app bundle their runtime, including the installed `relay.exe` and `mcp\relay-mcp.exe`. The portable ZIP's CLI and MCP server (`relay.dll`, `mcp\relay-mcp.dll`) need the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). There is no code signing, so Windows may show a security prompt. "No account" refers to Relay itself and the demo; the agents you connect still use their own accounts, permissions and model billing.

## The four pages

![Agents: names, bridge status and the optional handoff rule](docs/images/ui-agents.png)

| Page | What it does |
| --- | --- |
| Tasks | Left list, right detail. Create automatic or manual transfers, read request, reply and processing history together, filter by all / needs attention / in progress / completed, select several tasks for batch actions. |
| Agents | Register a sender and a recipient under any names. One-click setup inspects the local host and imports a calling method; manual fields are always available. Each Agent row has edit, test, recovery and delete actions; the detail card shows bridge status and the optional one-line handoff rule. |
| Connections | One line per Agent pair. Arrows show which directions have a verified calling method, green/red shows whether each direction is enabled, grey dashed means no method. Click a line to manage both directions with switches; manual copy works regardless. |
| Settings | Appearance (five themes, accent colour, six languages), notifications and sounds, relay storage, retention, advanced timeouts, and a danger zone. Changes apply immediately. |

![Connections: direction, method and enablement are separate](docs/images/ui-connections.png)

## How a handoff works

### Automatic CLI transfer

1. Register A and B. **One-click setup** in **New Agent** reads CLI help and non-secret settings, then imports a calling method when one is available. If the local interface cannot run, use the manual fields instead of pasting a configuration block.
2. Choose **New task → New automatic transfer task**. Select A (and optionally the source conversation; without one, the turn for A is copied manually), select B, and choose a new or existing target conversation. Write the work request; Relay prepares the prompt and calls the selected CLI.
3. Choose a **single handoff** — it ends when B finishes —, **keep revising until the review passes**, or a **fixed relay** with a set number of handoffs. In review mode you pick the final reviewer, type acceptance criteria or have the initiating Agent prepare them, and can set a review limit; if the reviewer also executed the task and you asked for independent acceptance, the review starts in a new conversation (a new conversation still shares the host's global rules and memory). The reviewer requests concrete changes or approves with no open items. Relay records that decision; it does not independently verify the work.
4. Create the task. Explicitly allow any direction that is not enabled yet, since automatic sending requires a verified method for that Agent. The board names the current worker, role, round and phase. Later turns resume the exact bound conversation; if that conversation is missing, Relay asks for another choice instead of falling back to the latest one.

### Fixed relay with a set number of handoffs

Choose **Direct handoff to the recipient**, **Work then hand off**, **Test and modify once**, or **Test and modify twice**. Pick the first Agent for multi-step templates, edit the work for each step, and check the preview for the route and endpoint. A→B has 2 steps and **1 handoff**, A→B→A has 3 steps and **2 handoffs**, and A→B→A→B→A has 5 steps and **4 handoffs**. The relay ends when the final step completes.

For **Custom steps**, write one step per line, such as `agent=work`, `agent:test=check the work` or `agent:modify=apply the test report`. Each step receives its own duty and the previous handover note. A final modify step after a test is shown as **Modified without retest**.

Each Agent uses its chosen conversation on its first turn. When the Agent works again, Relay reuses the exact conversation by default, or starts a new one if selected. Without a chosen source conversation, steps assigned to the sender use manual delivery. Each turn keeps its own `context.json` and `receipt.json` under `.relay/turn-<n>/`. Relay also reads the attempt-level receipt used by 0.3.0.

Statuses are separate facts: **sent / delivery unknown / host confirmed delivery**, **Agent declared received / working / submitted**, **host turn ended**, and **flow finished**. A submitted receipt is not proof that the host stopped: while Relay's own call is still running, the next step is not started automatically, and the board says "result submitted · host end unknown". Receipts that name the wrong field or use the wrong type are rejected with the field name, the value found and the accepted form, and the file is kept.

![Settings: appearance, notifications and storage in one place](docs/images/ui-settings.png)

### Manual transfer

1. **New task → New manual transfer task**, choose sender and recipient, write the request. Copy the prompt for the sender and paste it into that session.
2. Paste the sender's JSON reply back into Relay and parse it to create the task, or pick an existing file. Open **Relay template** to choose a fixed route. Leave **Custom steps** empty for a continuous manual handoff.
3. Copy the next prompt to the Agent the board names. Receipts are read while Relay is running and on refresh; keep passing turns as the work needs, and the task ends only when it is explicitly complete.
4. Optionally install the one-line handoff rule into the instruction file your host actually reads. Relay never edits that file silently: you see the line before it is written, and detailed instructions are read on demand.

A result file appearing or changing never completes a task. A task changes state only through a valid receipt, or through your explicit "mark handoff complete", which records a user decision. If a delivery is unconfirmed, use **View target conversation** to see its saved session ID and check it in the host. **Continue waiting** extends the deadline for a paused delivery, and **Check for receipts now** reads any receipts already on disk. If you confirm the request never arrived, **Not received · Allow resending** authorizes one manual send for that turn. Relay keeps checking for late receipts throughout.

## What it includes

| Area | Current behaviour |
| --- | --- |
| Task board | Direct CLI work requests or file handoffs, explicit conversations, single handoff or review loop, pause decisions, batch selection and task controls |
| Sessions | SQLite bindings per task, attempt, turn and Agent; targeted discovery, display cache and a fresh check before resuming |
| Agent entry | One name, one-click setup, separate manual fields, and a local configuration endpoint |
| Handoff rules | Optional one-line entry, shown for approval, details on demand, removal that preserves your other edits |
| Connection map | Direction arrows, per-direction switches, node dots for a recorded method, a failed probe or no method |
| CLI extensions | Native JSON, JSONL and final-text output adapters; separate new-session and resume probes |
| Storage | Local relay folder, retention cleanup, relocation that copies and verifies before switching |
| Appearance | Minimal, warm, fresh, night and follow-system themes; accent colours; English, Simplified and Traditional Chinese, Japanese, Korean, Spanish |
| Notifications | Optional Windows helper with selectable sounds; an in-app notice while Relay is running |
| Uninstall | Per-user Windows installer; uninstall keeps data by default, or removes rules, or clears the ledger and preferences while keeping workspace files |

## Bridges, verification and cost

A bridge is one of three kinds: ACP, the running desktop app's own interface (DSH: `dsh-desktop`), or the host's CLI. Relay adapts supported native JSON, JSONL and final-text output; the host does not have to emit Relay's private receipt format.

- Sending by hand through a bridge needs only a registered method and an enabled direction. **Automatic** delivery also needs the task's opt-in and verified capability: a passing probe, where the Agent writes a marker file and you confirm the session is visible, or one real handoff turn whose receipt the Agent wrote.
- A probe calls the real CLI and may cost money. Login refusal, a missing model or missing tool permissions block automatic sending for that Agent until you choose **I have resolved · Allow automatic sends**.
- Resuming a conversation requires an exact binding, a fresh host-store check, and a matching session ID in the response. Relay does not ask models to discover session IDs or make hidden conversations visible.
- Zcode can keep old conversation content in memory after a CLI delivery. Switch to another conversation and return after about 11 minutes, or save and restart Zcode; an old view does not mean delivery failed.
- Relay aligns the known hosts' request prefixes so prompt caches keep matching across a switch — Zcode by launch flags, Hermes through its shared gateway, DSH through its running desktop. Measured cache hits after a switch were 93–99%. `cli-recipe doctor` reports launch settings that differ from the desktop's; `agent cache-check` shows each request's cache hit. For other hosts, one-click setup hands the configuring Agent the built-in `relay-cache-parity` skill.
- WorkBuddy offers no usable external interface here, so handoffs to it are copied manually.

## Development

<details>
<summary>Build and test from source</summary>

Requires the .NET 10 SDK (see `global.json`). From a fresh clone, in PowerShell:

```powershell
git clone https://github.com/jason-namehard/agent-relay.git
cd agent-relay
dotnet restore --locked-mode
dotnet test --configuration Release --no-restore
```

This covers Core, CLI, MCP and the core test suite. The desktop app and notification helper build separately on Windows (PowerShell 7):

```powershell
dotnet restore src/AgentRelay.Notify/AgentRelay.Notify.csproj --locked-mode
pwsh ./scripts/test-desktop.ps1
pwsh ./scripts/check-licenses.ps1 -IncludeDesktop
pwsh ./scripts/package.ps1 -Flavor Windows
```

The installer is built with Inno Setup: `pwsh ./scripts/build-installer.ps1` (pass `-Dotnet` and `-InnoCompiler` if they are not on `PATH`). Scripts accept `-Dotnet <path>`; packages go to `artifacts/packages` and existing archives are not overwritten. Regular tests use local fixtures and never call a paid model.

</details>

<details>
<summary>CLI and stdio MCP</summary>

`dotnet .\relay.dll --help` lists the commands; the MCP server does not implement `--help`. A host starts one MCP instance per Agent:

```powershell
dotnet .\mcp\relay-mcp.dll --home <relay folder> --agent <registered Agent id>
```

Tools: `relay_task_dispatch`, `relay_task_ack`, `relay_task_start`, `relay_task_complete`, `relay_task_fail`, `relay_task_status`, `relay_prompt_show`. The caller identity is fixed at startup. CLI output and most detailed documents are currently in Chinese.

</details>

<details>
<summary>Relay folder resolution</summary>

| Program | Order |
| --- | --- |
| CLI, MCP | `--home` → `RELAY_HOME` → `%LOCALAPPDATA%\AgentRelay` |
| Desktop app | `--home` → `RELAY_HOME` → folder chosen in Settings → `%LOCALAPPDATA%\AgentRelay` |
| Demo | A new folder under `%LOCALAPPDATA%\AgentRelay.Demos\` |

In **Settings → Relay storage → Review relay files**, select a file to see its full path, ownership and task references. **Preview cleanup** lists the exact expired Relay-owned files eligible for deletion. After confirmation, Relay rechecks the listed files and keeps files that changed or gained an active reference. Originals, project deliverables, control files and files of unverified ownership stay in place. The default retention period is 3 days.

After moving the relay folder in Settings, point the CLI and MCP at the new `--home` and copy handoff prompts again.

</details>

## Limits

- Preview software with real gaps: real host creation, visibility and continuation need testing in that host; simulated bridges do not establish real integration. Windows display scaling and multi-monitor behaviour need manual checks.
- No background service or scheduled task. A running desktop app monitors receipts; after every process closes, overdue work waits for the next `relay recover` (CLI) or the next launch.
- Uninstall cleanup only covers rules registered for the current relay folder. Other sites you used need separate handling. Deleting the install or ZIP folder does not replace uninstalling or removing rules.
- Cancelling a task stops Relay's tracking and further delivery; it does not stop an Agent that is already working. Deleting a task keeps the files and their ownership records so eligible Relay copies can be reviewed for cleanup later.

[Getting started](docs/GETTING_STARTED.md) · [Setup and receipts](docs/CONFIGURATION_ASSISTANT.md) · [Sessions](docs/SESSIONS.md) · [Architecture](docs/ARCHITECTURE.md) · [UI spec](docs/UI_SPEC.md) · [Adapters](docs/ADAPTERS.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Issues](https://github.com/jason-namehard/agent-relay/issues)

## License

Project code is [MIT](LICENSE). The Windows package and installer also redistribute the .NET runtime, the Windows App SDK and other components under their own terms: [distribution terms](DISTRIBUTION_TERMS.md), [third-party notices](THIRD_PARTY_NOTICES.md) and `licenses/`.
