# Session Registry 与精确续接

会话绑定回答“这个任务的下一轮应进入哪一段对话”。新建会话时，Relay 从适配器结构化结果登记真实 ID；使用已有对话时，由用户明确选择一次。同一任务 attempt 的后续 A → B → A → B 使用双方已绑定的准确 ID，不重新猜测目标。

## 用户操作

“新建任务”直接接受工作要求、发送方及来源会话、接收方和目标会话；不必先生成文件或交接 JSON。已有文件仍可走“新建交接”，存在 CLI 方案时确认会话选择。目标默认“新建会话”；选择“使用已有会话”后点击“选择已有会话”。列表优先展示标题、活跃时间、项目路径和可获取的模型信息；准确 ID 用于确认身份。列表不会默认选择第一条，也不会把最近活跃或同 cwd 当作已绑定。

已有缓存立即显示，同时后台刷新该 Agent。取消不绑定，点击“使用此会话”才确定选择。无发现提供器时保留手动 ID 入口，不显示一个注定无法工作的发现列表。

任务详情“会话”折叠区显示双方绑定，可主动更换或明确新建。会话已失效、存储不可访问或返回的 ID 不一致时，自动续接停止；用户可以重新选择或手动复制提示。不会自动跳到其他对话。

## 协作方式与会话绑定

会话绑定只确定下一轮进入哪里，协作方式确定下一轮由谁处理。单次交接由回执的 `needs_reply` 决定是否回传，默认最多 4 轮。持续修改与验收由用户指定作者，另一方验收，按回执 `outcome` 在 `implement`/`revise`、`review`、`accept` 阶段间推进，只有作者 `accepted` 才完成；格式见 [配置与回执](CONFIGURATION_ASSISTANT.md#持续修改与验收)。

经 CLI 把轮次送回发送方需要其准确来源会话和续接命令；未绑定来源时这些轮次手动复制。轮数用完、单轮超时或无进展时任务暂停，绑定保留，继续后仍进入同一对话。新任务不再使用固定 `handoff_plan`；早期版本已带计划的任务仍按原计划完成。

## 来源身份

来源 Agent 的名称不能证明当前 Session。普通剪贴板中的 `{from,to,file,message?}` 也无法提供调用进程身份，因此界面显示来源未识别，并允许用户选择一次；模型不需要查找或填写自己的 ID。

只有调用适配器已有准确会话上下文时，才能自动登记来源。精确 resume 调用会注入 `AGENT_RELAY_CALLER_AGENT_ID`、`AGENT_RELAY_CALLER_SESSION_ID`、`AGENT_RELAY_CALLER_HOST`、`AGENT_RELAY_CALLER_CWD`，以及可获取的 profile。通过上下文入口创建新交接时，发送方必须与该上下文一致。尚未返回会话 ID 的新建调用不能提前声称已经识别其来源。

这些是本机适配器传递的上下文，不是要求模型搜查文件、返回会话 ID 或修改宿主 UI。长期规则负责短 handoff 格式，每轮任务不重复注入完整协议。

## Registry 与持久化

权威数据保存在现有 Relay SQLite：

| 表 | 内容 |
| --- | --- |
| `task_session_bindings` | task、attempt、turn、agent 及准确 session ID、cwd、profile、路径、绑定依据和时间 |
| `session_discovery_sources` | 指定节点的提供器及数据根目录 |
| `session_discovery_cache` | 节点展示缓存和扫描时间 |

绑定来源包括适配器上下文 `automatic`、创建结果 `created`、用户点选 `user_selected` 和手动输入 `manual`。明确选择新建时记录 `new`；命令只返回最终文本、没有持久会话 ID 时记录 `ephemeral`，不能把它当作可续接会话。

查找绑定限定当前 attempt，再读取该 Agent 最新轮次的明确记录。新 attempt 不跨用旧记录；新的返回 ID 不会悄悄替换原绑定。回执仍决定工作是否完成，CLI 提交成功本身不完成任务。

## Discovery

提供器只读取指定节点的宿主数据，不执行模型请求，也不调用外部“全量会话总览”脚本。非标准位置可通过 `session source` 配置，或从调用方案里明确的数据目录环境变量取得。

| 提供器 | 发现依据 | 可见性依据 |
| --- | --- | --- |
| DSH | 实际持久会话日志文件；已有 projcache 提供标题、cwd、model | 默认 unknown，不把投影存在当作 GUI 可见 |
| Zcode | CLI SQLite 会话表，排除删除和归档；定向读取目标目录与用户任务索引 | 明确索引匹配为 native-visible；有索引但未收录为 persistent-but-not-indexed，否则 unknown |
| Hermes | SQLite 会话元数据、profile、cwd、model | 隐藏状态单独标注；桌面来源本身不证明当前 UI 可见 |
| WorkBuddy | SQLite 会话元数据、自定义标题、cwd、model，排除删除和归档 | 读取已知隐藏标记，其余保留 unknown |

Relay 缓存用于快速展示，不能证明续接仍然有效。每次 resume 前重新查询宿主存储中的准确 ID；数据库文件存在也不代表其中一定有这个 Session。刷新失败明确显示错误，不用旧缓存伪造一次成功刷新。

## 当前能力

以下是已实现的适配能力，不是对任意安装版本或账号状态的真实调用保证。创建与续接仍需登记符合该宿主的命令。

| 宿主/输出 | 新建并取得 ID | 精确续接 | 定向发现 | 打开宿主会话 | 模型/思考设置 |
| --- | --- | --- | --- | --- | --- |
| DSH JSONL | 有效 session/final/完成事件可登记 | 需明确 ID、resume 命令及存在性验证 | 已实现 | 未实现 | 继承宿主/原会话 |
| Zcode JSON | 有效 result 的 sessionId | 同上，返回 ID 必须一致 | 已实现 | 未实现 | 继承宿主/原会话 |
| Hermes JSONL | 有效 result 的 session_id | 同上 | 已实现 | 未实现 | 继承宿主/原会话 |
| WorkBuddy 兼容 JSON | 有效成功结果的 session_id | 同上 | 已实现 | 未实现 | 继承宿主/原会话 |
| 普通 text | 无 ID，仅一次性执行 | 不支持凭文本伪造 ID | 取决于另配提供器 | 未实现 | 由命令与宿主管理 |

`session_open` 没有统一实现，也不会用“请模型把会话显示出来”代替。可见性与可续接性分别展示；可见性未知不自动阻断一个准确且存在的绑定。Relay 为本任务创建的会话没有发现能力也可续接，返回 ID 仍须一致；用户选择的已有会话仍需发现确认。

模型和思考强度属于执行配置，当前没有统一跨宿主覆盖表单。新建会话若提示未选模型，需要在宿主设置或选择已经配置的旧会话；续接默认继承旧会话。Relay 不把模型参数塞进任务正文，也不伪造宿主不支持的参数。

## WorkBuddy

WorkBuddy 桌面版目前没有对外可用的调用接口，只能复制提示词手动交接：

- 应用自带的 CLI（`codebuddy-lite-wb.mjs`）单独启动时用不了桌面登录，报 `Authentication required`。
- 每个打开中的对话由一个本机工作进程承载，`~/.workbuddy/sessions/<pid>.json` 记录其 ACP 地址。WorkBuddy 5.9.2 对每个对话只允许一个写入方：桌面在线时外部连接只能旁观，外部 `session/prompt` 被忽略、`session/load` 报 `Persistent Session already has an external writer`。这是程序规则，不是可调的客户端设置。
- 官方 CodeBuddy Code CLI 有独立的登录、额度和会话存储，建的会话不出现在 WorkBuddy 里，不能代替 WorkBuddy。

会话发现仍读取 `~/.workbuddy/workbuddy.db`，手动交接时可以绑定 WorkBuddy 的对话以便记录。

## DSH

推荐 `dsh-desktop` 方案：由运行中的桌面执行回合，见下方“续接会话的缓存对齐（各宿主）”。下面是旧的 headless 方案，桌面不开时仍可用，但与桌面的请求不一致。

DSH 用自带的 headless profile 接入：`DeepSeek Harness.exe`（环境变量 `ELECTRON_RUN_AS_NODE=1`、`DSH_HOME` 指向 dsh-home）运行 `...\dsh-desktop-host\lib\cli.js --profile headless --json {prompt}`，续接加 `--session-id {session_id}`，`output_format` 为 `dsh-jsonl`，会话发现提供器为 `dsh`、目录为 dsh-home。

- headless profile 默认权限是 workspace-write，写不了工作区外的 Relay 回执。用 `--patch` 叠加一个只含 `permission.defaultPreset: danger-full-access` 的补丁（与桌面 profile 的默认相同）。
- 要用桌面配置的供应商（如本地网关上的 OpenCode Go）：先叠加桌面 profile 的 `cordis.patch.yml`，再叠加一个补丁把 headless 默认关闭的 `credentials`（`@deepseek-ai/dsh-credentials-local`）重新启用，它读取 DSH_HOME 下与桌面共用的 `.credentials.yaml`，不必另填 key；同一补丁可覆盖 `agent-default-model` 的供应商、模型与思考强度（整段替换，需写全）。

## Zcode CLI

Zcode 桌面按需加载 MCP 与 skill 的工具定义（工具搜索模式 `local`），命令行默认 `off`，会把全部工具定义随每次请求发送：本机仅 REA 一个 MCP 就有 207 个工具，每次请求约 26 万 token。Relay 导入 Zcode 调用方案时自动给 create 与 resume 追加 `--toolsearch-mode local`，`cli-recipe doctor` 在缺少时报错。用 `local` 而不是 `auto`：`auto` 取决于供应商是否声明原生工具搜索，换供应商时行为会变。实测最小提示的单次输入从约 26.3 万 token 降到 16,399，与桌面会话首轮（16,693）同量级，模型仍可经工具搜索找到并调用 REA 的工具。

### 续接桌面会话时保持缓存命中

Zcode 每次启动都会重新生成工具集和系统提示，这两样不随会话保存；而供应商只有在请求前缀完全一致时才复用提示缓存。所以命令行续接桌面会话时，启动方式必须和桌面一致。否则这一轮会把整段历史重新计费，桌面的下一轮也一样。除工具搜索外，命令行与桌面还有三处默认不同：

| 差异 | 桌面 | 命令行默认 | Relay 的处理 |
| --- | --- | --- | --- |
| 动态工作流 | `~/.zcode/v2/setting.json` 的 `dynamicWorkflowMode`，决定技能列表里有没有 dynamic-workflows | disabled | 导入时按桌面设置追加 `--workflow-mode` |
| 生成界面说明 | 系统提示里写明桌面的 visualizations 目录 | surface 默认为 terminal；即使用 `--surface desktop`，也要设置 `ZCODE_GEN_UI_OUTPUT_ROOT` 才写 | 导入时追加 `--surface desktop`，并把 `ZCODE_GEN_UI_OUTPUT_ROOT` 设为桌面数据目录下的 `visualizations`（surface 不影响请求的其他部分） |
| 记忆 | `memoryEnabled` 关闭时，向运行时传 `memory.enabled=false` | 开启，系统提示里多一段记忆说明 | doctor 检查；需在 `~/.zcode/cli/config.json` 设 `"features": { "memory": false }` |

桌面的设置始终放在 `~/.zcode/v2/setting.json`。数据目录如果迁移过，会记在 setting.json 的 `dataBaseDir` 里，实际位置是 `<dataBaseDir>\.zcode\v2`；没迁移过就是 `~/.zcode/v2`。桌面用的供应商配置和 visualizations 都在这个目录下。调用方案的 `ZCODE_PERSONAL_PROVIDER_CONFIG_FILE` 应指向这里的 `provider_config.json`，否则 doctor 报 `host.parity.provider`。

记忆开关只能写在 `~/.zcode/cli/config.json` 里，命令行没有对应的参数或环境变量。这个文件桌面也会读：桌面开启记忆时不传开关，直接沿用这一项。所以 Relay 只做检查，不替用户写入。以后在桌面开启记忆，要同时删掉这一项。桌面设置改动后，doctor 会提示重新导入调用方案。

实测会话 `session_fc4427…`，模型 deepseek-v4.1-flash：

- 未对齐时，桌面一轮的缓存命中为 0，此前命令行刚刚连续命中 99%；结合请求前缀对照，确认了启动方式差异。命中率本身不能排除模型变化或供应商缓存淘汰。
- 三项对齐后，命令行请求的前 27 条消息与桌面请求逐字相同，仅最后一条的缓存断点标记不同。
- 紧接着的命令行一轮读到 20,480 token 缓存，与桌面那次 21,912 token 请求的前缀相当，说明命中的是桌面写下的缓存。
- 反方向也验证了：命令行一轮之后，桌面一轮读到 22,016 / 22,154 token 缓存（99%）。
- 去掉记忆说明，每次请求还少约 2,300 token。

从桌面应用外启动 `zcode.cjs` 时，命令行只认自己登记过的账号套餐；桌面已登录、或在桌面里切换模型，都不会为它登记，于是新建会话报 “Select a model before sending a message”。在终端登录一次即可登记套餐并写入默认模型：

```powershell
$env:ZCODE_BUILTIN_PROVIDER_CONFIG_FILE = "D:\Zcode\resources\config\provider\zcode-builtin.json"   # 按实际安装目录
& <Zcode 自带 node.exe> "D:\Zcode\resources\glm\zcode.cjs" login zai --no-browser
```

不设该变量时，登录会因找不到内置供应商配置而失败。Windows 自动打开浏览器时会在第一个 `&` 处截断授权地址，页面报缺少 `response_type`、`redirect_uri`、`state`；加 `--no-browser` 后复制终端打印的完整地址到浏览器打开。`relay cli-recipe doctor` 会检查命令行是否已登记套餐。Zcode 桌面的供应商配置在工作区数据目录（如 `E:\Zspace\.zcode\v2\provider_config.json`，含 OpenCode Go 等自定义供应商及其模型规则）。调用方案把 `ZCODE_PERSONAL_PROVIDER_CONFIG_FILE` 指向这份文件，命令行就使用与桌面相同的供应商，不必另填 key；默认模型写在其中的 `defaultModelSelection`，思考强度须是该模型支持的档位。否则，命令行的供应商只来自它自己登记的账号套餐和 `~/.zcode/cli/config.json` 中的自定义供应商（`provider` 与 `model` 字段，仅在 `~/.zcode/v2/provider_config.json` 不存在时导入一次）；桌面里配置的自定义供应商（如 OpenCode Go）由桌面经内部协议交给自己的进程，命令行看不到，因此 `defaultModelSelection` 只能指向命令行已登记的供应商。续接已选过模型的会话不受影响。

## 续接会话的缓存对齐（各宿主）

续接原会话是为了让供应商复用提示缓存，前提是两次请求的前缀（工具定义、系统提示、历史）逐字相同。各宿主构建前缀的方式不同，对齐的可行性也不同。完整方法写在 Relay 自带的技能 `src/AgentRelay.Core/Skills/relay-cache-parity/SKILL.md` 里；一键配置会让 Agent 按它检查。逐次用量用 `relay agent cache-check --agent <ID> --session <会话>` 读取。

| 宿主 | 类型 | 现状 |
| --- | --- | --- |
| Zcode | 每次启动现建 | 已对齐，见上节。导入方案时自动补齐，doctor 检查 |
| Hermes | 网关型 | `hermes acp` 连接桌面同一个网关，会话的系统提示与工具集在创建时固定，并存在 `state.db`，两边续接发出的前缀相同。Relay 续接时用会话自己的 cwd，且不带 MCP 服务器（cwd 不同会让网关迁移会话并重建提示）。用量取自 `HERMES_HOME\logs\agent.log`。实测：桌面会话里，桌面一轮之后，Relay 的 ACP 一轮命中 98%；再之后的桌面一轮命中 99% |
| DSH | 桌面接口（`dsh-desktop`） | 由运行中的 DSH 桌面执行回合：Relay 找到桌面主机的本机端口，用 `DSH_HOME\.credentials.yaml` 里桌面自己的签名密钥，在内存中签出 v1 浏览器 cookie（用户已同意；密钥不保存、不显示）。之后调用 `session/create`（preset `standard`）、`session/prompt`、`session/projections`、`session/page`。新会话与桌面会话的组成完全相同（系统提示 1,838、工具 8,424 token），用的是桌面自己的默认模型。实测：Relay 一轮之后，桌面一轮命中 99%；桌面一轮之后，Relay 一轮命中 98–99%。桌面没开时这一轮失败并说明原因。旧的 headless 方案（`dsh-jsonl`）与桌面工具集不同、不能续接桌面会话，doctor 以 `host.parity.dsh` 提示 |
| 其他 | 未测 | 按技能步骤判断类型，找用量来源，在用户同意后做对照实验，再把对齐项写进调用方案；Relay 还不认识的宿主，写进方案的 `limitations` |

## 新会话默认模型

Agent 管理的“新会话默认模型”和 `relay agent models --agent <ID>` / `relay agent set-model --agent <ID> --provider <供应商> --model <模型> [--reasoning <档位>]` 列出并设置 Relay 新建会话时用的模型。Zcode 写入调用方案 `ZCODE_PERSONAL_PROVIDER_CONFIG_FILE` 指向的供应商配置（修改前留 `.bak-relay-model`）；DSH 写入调用方案中位于 `中转站\bridges\` 的补丁层，并通过 `--dump-config` 读取可选模型（不调用模型）。思考强度只能选该模型支持的档位，界面默认取中档：有 `medium` 用 `medium`，否则取中间那档。续接已有会话不传任何模型参数：Zcode 续接时忽略默认值，DSH 只给没有会话级选择的 Agent 用默认值，所以续接沿用会话自己的模型与思考强度。其他宿主暂在宿主自己的设置里选择。

Relay 每轮结束后将符合条件的 Zcode CLI 会话同步到侧栏索引。切回 Zcode 可刷新侧栏。若关闭了同步或同步失败，可在保存工作后从托盘完全退出再打开 Zcode。已有会话内容仍可能显示缓存，可先切换会话再重新打开。列表未刷新时无需重复运行付费探针。

DSH 的 `dsh-desktop` 在运行中的桌面执行，新建和续接通常实时显示；请核对准确会话，不要在执行过程中重启。旧 headless 方式与桌面桥接不同，不能混为一谈。

自动投递需分别验证新建与续接。探针机器成功且写文件通过后，实际看见相同会话标记再保存可见性确认；可确认已有成功探针，无需为了勾选重新调用模型。任务启用了自动投递但续接未验证时，看板会明确显示待验证。桥接受阻时不提供无效的发送按钮，改为进入 Agent 管理处理；超时保留投递错误，不改写成 Agent 仍在处理。

ACP 与 DSH 桌面协议通过请求携带准确 session ID，可共用一个启动入口，不需要重复填写相同的 resume 命令。ACP 服务仍须实际支持 session/load 或 session/resume；入口相同不代表能力已经验证。

### 清理测试探针

在 Agent 的桥接测试窗口点击“清理探针会话和文件”。清理按已记录的会话 ID 和归属进行，保留通过的桥接验证。会话被清理后，如需再测续接，先运行新会话探针。真实任务使用的会话和归属不明的数据会保留。

- **DSH 桌面**：保存工作，从托盘完全退出 DSH 后执行清理。Relay 核对原探针标记，保留后来用于其它工作的会话，以及仍被其它会话或缓存引用的图片。对支持的 v4 日志，清理归属明确的会话目录、独占图片附件、会话缓存及该 ID 的侧栏记录。无法读取的日志、未知格式或无法归属的共享缓存会保留。若日志已经丢失，仍可按已记录的 ID 清理侧栏和会话缓存，但无法从丢失的日志确认图片归属。
- **Zcode**：按确切会话 ID 清理会话、会话产物和对应的侧栏分组与排序记录。实际索引位置依据 Zcode 的 `dataBaseDir`。切回 Zcode 刷新列表。

### 桥接变红时诊断

在受阻任务、Agent 详情或桥接测试中点击“复制桥接诊断提示词”，粘贴给有本机访问能力的 Agent。每次复制都会读取当前节点和阻塞时间，包含运行程序集、中转站位置与配置检查、同节点导入方案的命令入口。不会复制完整方案、环境值、原始宿主输出或任务正文。诊断 Agent 先只读排查，有依据再备份并修改同一节点配置，不自动跑真实探针、解除阻塞或继续任务。

Agent 管理的配置检查和 `cli-recipe doctor` 对 DSH 追加真实的只读认证自检（`host.desktop.connection`），仅读取会话列表，不创建会话或请求模型。`host.desktop.auth` 只表示签名材料可读取，不能替代接口认证。当前连接通过和历史阻塞仍然保留可以同时存在；只有用户确认原因处理后才解除阻塞。

DSH 后续401会保留发生的操作名及非敏感监听诊断，如进程、端口和启动时间；不保存cookie或签名密钥。已有旧错误没有这些信息时，不能倒推究竟是发现、提交还是等待阶段失败。

DSH 桌面的认证检查会拒绝签发时间晚于其本机时钟的 cookie。Relay 将签发时间提前五分钟，保留有效期以容纳两进程的时钟差异。请求已被接收后若结果轮询失败，任务会提示结果待确认并避免自动重发，不把该次轮询失败记录为全局认证阻塞。

DSH 认证阻塞现在可在任务详情或 Agent 管理点击“重新检测并恢复桥接”。只有当前只读认证和必要本机检查通过，才解除对应旧阻塞；检查期间出现新失败或方案变化时不会误清除。恢复不修改探针证据，仍未验证的续接暂不自动运行；已取消任务不会重新开始。恢复后，其他已授权的自动任务可能继续。

桥接测试结果标注实际测试时间，与当前连接检查分开展示。一次失败的新建探针可能替换此前的探针结果，恢复旧认证阻塞不会把该失败记录改成通过；可用一次真实手动交接回执或完成探针确认补足能力证据。

## CLI

在发布包目录使用 .NET 10，替换示例中的节点、任务和目录：

```powershell
dotnet .\relay.dll --home <中转站目录> handoff cli-create --manifest <CLI任务清单>
dotnet .\relay.dll --home <中转站目录> handoff cli-create --manifest <CLI任务清单> --send --approve
dotnet .\relay.dll --home <中转站目录> handoff send --task <任务ID> --approve
dotnet .\relay.dll --home <中转站目录> session query --agent <节点ID> --refresh
dotnet .\relay.dll --home <中转站目录> session capabilities --agent <节点ID>
dotnet .\relay.dll --home <中转站目录> session source --agent <节点ID> --provider dsh --directory <宿主数据根目录>
dotnet .\relay.dll --home <中转站目录> session bindings --task <任务ID>
dotnet .\relay.dll --home <中转站目录> session bind --task <任务ID> --agent <节点ID> --session <准确会话ID>
dotnet .\relay.dll --home <中转站目录> session new --task <任务ID> --agent <节点ID>
```

`cli-create` 不加 `--send` 只保存工作要求和绑定，发送必须显式 `--approve`；清单结构见 [配置与回执](CONFIGURATION_ASSISTANT.md)。`source` 的 provider 可选 `dsh`、`zcode`、`hermes`、`workbuddy`，数据目录应与该提供器对应。查询只读元数据，`capabilities` 返回已登记方案声明的能力，不代表实际调用已经验证。`bind` 保存用户提供的精确 ID，保存不等于已验证；自动发送前仍核实存在。未知提供器或不能访问的存储不会回退到 latest。自动投递需任务允许且该 Agent 对应能力已验证，见 [配置与回执](CONFIGURATION_ASSISTANT.md#高级-cli-接口扩充)；真实发送和续接可能产生模型费用。
