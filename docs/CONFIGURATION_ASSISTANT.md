# Agent 配置、按需规则与文件回执

## 统一节点录入

节点只需一个自选名称。Agent 管理工具栏为“新建 Agent、编辑 Agent、删除 Agent、测试桥接、一键配置”。宿主类型属于内部适配信息，不要求用户另填软件名称。

推荐点击“一键配置”，将复制的提示词粘贴到目标 Agent 的新对话窗口。提示词带独立请求标识和当前节点目录，要求 Agent 先读取最新目录，再创建新节点；新建命令不传 `--id`，不会把当前选中的节点当作写入目标。调查仅读取帮助和非敏感设置，不启动真实模型。可用调用方式随后直接导入同一个新节点。

Agent 可将完整配置保存为 JSON 后，通过发布包的本机接口填写：

```json
{"name":"我的助手","host":"manual","config_path":null,"cli_path":null,"host_version":null,"instruction_paths":[]}
```

```powershell
dotnet .\relay.dll --home <中转站目录> agent setup --manifest <配置JSON路径>
# 更新既有节点时额外提供 --id <节点ID>
```

新节点 ID 自动生成；只有明确更新时才指定已有 `--id`。直写成功后无需在新建窗口重复保存。未知路径留 null，不用会话日志代替配置文件；`host` 可省略，默认 `manual`。

也可使用纯手动表单：名称、可选配置/指令文件，以及独立的 CLI 调用区。CLI 区分别填写程序、每行一个参数、可选标准输入模板、输出格式和工作目录。参数或标准输入必须包含 `{prompt}`；只记录程序路径时仍需补齐调用方式才能测试。GUI 不要求粘贴和解析一整段 Agent 配置。

演示模式会禁用真实探针，这不表示 CLI 失败。正式配置请运行 `gui/AgentRelay.Desktop.exe`；已在演示中登记的资料可从设置保留当前数据并转为正式使用。

## 一行常驻规则

在 Agent 管理选中节点，展开“交接规则与节点详情”，选择宿主实际加载的指令文件，预览后明确确认才写入。Relay 在自有标记区块内加入一行：需要交接文件或收到交接时，先阅读稳定入口。详细规则放在中转站之外的稳定指引文件中，迁移后刷新当前位置与节点目录。

这一行仍占用少量上下文；详细内容按需读取，不保证任意宿主都会自动触发。用户后续编辑会保留。每次安装保留原始备份，移除仅剥离本工具区块，不用旧备份覆盖整份用户文件。

```powershell
dotnet .\relay.dll --home <中转站目录> guidance preview --agent <节点ID> --file <指令文件>
dotnet .\relay.dll --home <中转站目录> guidance install --agent <节点ID> --file <指令文件> --approve
dotnet .\relay.dll --home <中转站目录> guidance remove --all
```

删除便携目录不会执行卸载程序，删除前需在 GUI 或 CLI 移除规则。

## 直接创建 CLI 任务

任务看板的“新建任务”直接接受工作要求、来源会话和目标会话。软件保存工作要求、生成上下文提示并调用已配置的程序；不需要先生成文件交接 JSON。目标可选新建或已有会话，使用已有会话时明确绑定一次，后续按准确 ID 续接。

协作方式分为“单次交接”和“持续修改与验收”。后者由用户指定作者，另一方为验收者，按验收标准往返，直到作者确认交付，见下文[持续修改与验收](#持续修改与验收)。来源会话可选；不选时，轮到发送方的步骤改为手动复制。未启用的连接方向需要明确授权，实际发送可能产生模型费用。

CLI 也能直接创建。将下列占位值替换为实际登记节点和准确会话 ID，保存为清单：

```json
{
  "from":"<发送节点ID>",
  "to":"<接收节点ID>",
  "prompt":"请检查并完成这项工作",
  "origin_session_id":"<来源会话ID>",
  "target_session_id":null,
  "collaboration":{"author_id":"<接收节点ID>","criteria":"验收标准","max_review_rounds":5},
  "auto_deliver":true,
  "turn_timeout_minutes":60,
  "enable_direction":false
}
```

字段为 snake_case，顺序不限。`target_session_id:null` 表示新建，已有目标则填写明确 ID。`collaboration` 可省略，省略即单次交接；`max_review_rounds` 取 1–20，默认 5。经 CLI 把回稿送回发送方时，发送方需配置续接命令并提供 `origin_session_id`。`auto_deliver:true` 表示本任务允许无人值守投递，仍需对应能力已验证。`turn_timeout_minutes` 为单轮时限，省略时使用任务超时设置。`enable_direction:true` 明确允许启用任务所需方向，默认不改变方向设置。

```powershell
# 只保存，不调用目标
dotnet .\relay.dll --home <中转站目录> handoff cli-create --manifest <清单JSON路径>
# 明确同意真实调用后，创建并发送
dotnet .\relay.dll --home <中转站目录> handoff cli-create --manifest <清单JSON路径> --send --approve
# 发送已创建任务的当前轮次
dotnet .\relay.dll --home <中转站目录> handoff send --task <任务ID> --approve
```

文件交接（含“新建交接”创建的任务）在首次投递前也可设置协作方式；回执导入和自动投递由 Core 执行，GUI 与 CLI 共用：

```powershell
# 首次投递前：指定作者、验收标准、验收轮数、单轮时限，并允许自动投递
dotnet .\relay.dll --home <中转站目录> handoff configure --task <任务ID> --author <作者节点ID> --criteria "<验收标准>" --max-rounds 5 --turn-timeout-minutes 60 --auto-deliver
# 导入回执；加 --deliver 时发送已允许自动投递且能力已验证的轮次
dotnet .\relay.dll --home <中转站目录> handoff sync --deliver --approve
# 当前处理者、阶段、投递方式、是否在等用户
dotnet .\relay.dll --home <中转站目录> handoff progress --task <任务ID>
# 继续已暂停任务；轮数用完时须追加轮数，说明会放在下一轮请求开头
dotnet .\relay.dll --home <中转站目录> handoff continue --task <任务ID> --note "<说明>" --extra-rounds 2
# 确认本轮未送达后允许再发送一次
dotnet .\relay.dll --home <中转站目录> handoff allow-resend --task <任务ID> --reason "<原因>" --approve
```

`task status` 对文件交接返回 `progress` 对象：`current_agent_id`、`phase`、`waiting_on`（`agent`/`user`/`none`）、`delivery`（`cli_auto`/`cli_ready`/`cli_in_flight`/`agent_working`/`cli_unconfirmed`/`copy_prompt`/`none`）、`turn`、`max_turns`、`review_round`、`max_review_rounds`、`next_if_passed`、`next_if_changed`、`pause_reason`、`pause_message`、`finished` 与 `completion`（`delivered`/`completed`/`operator_completed`/`cancelled`/`failed`/`timed_out`）。

## 按测试结果流转

在首次投递前用 `handoff relay-plan --task <任务ID> --steps "b=实现;a:test=跑回归;b:modify=按报告修改" --stop-if-test-passes-at-step 2` 配置。编号从 1 开始，每个任务可选一个后面还有步骤的 `test` 步骤。用 `handoff cli-create` 时也可在 JSON 的 `relay_plan` 中设置 `stop_if_test_passes_at_step`。

条件步骤的上下文给出 `test_condition.allowed_outcomes`、`if_passed` 和 `if_failed`。测试完成时写 `status: "done"`、真实测试报告 `result_file` 和 `outcome: "passed"` 或 `"failed"`。Relay 按这个结构化结果决定停止或继续，报告正文只作为交接内容。测试未能执行完毕时写 `status: "failed"`、`needs_reply: false` 和说明原因的 `next_action`。用 `handoff progress` 查看 `test_condition` 的步骤、后续 Agent 和已采纳的结果。

## 并行分工

`relay parallel create --manifest <清单>` 创建普通交接任务和各自的工作文件夹。清单使用 `title`、`from`、`merge_agent_id`、`project_directory`、`input_files`、`work` 和 `merge_instructions`。其中 `input_files` 是项目相对路径数组，`work` 至少有两项，每项填写 `agent_id` 和 `instructions`。同一个接收 Agent 可以重复出现。

用普通交接入口投递各任务。Agent 应在上下文的 `parallel.working_directory` 里编辑副本，并在 `reply_file` 写总结，回执填 `result_file` 为该总结路径以及 `needs_reply:false`。成功回执会保存此时的文件变化及成果归属。

用 `relay parallel status --group <组ID>` 查看分工、冲突及可用成果。把确认过的响应 `data` 对象保存为预览文件，再运行 `relay parallel merge --preview <预览文件>`。有分工未提供可用成果时须明确传 `--allow-partial`，有冲突时须明确传 `--review-conflicts`。如果预览已变化，重新查看后确认。汇合任务的 `parallel.merge_manifest` 列出各版本及归属，版本路径相对于清单所在目录。

## A 输出文件交接 JSON

让 A 先写好文件，再输出如下对象供用户粘贴。`from`、`to` 是已登记 ID 或唯一名称；`file` 是存在且可读的绝对路径；`message` 为可选的一句工作要求，不放完整正文。

```json
{"from":"节点甲","to":"节点乙","file":"C:/work/handoff.md","message":"请检查并按结论修改"}
```

示例路径须换成真实文件。Markdown、压缩包等均按文件处理。用户点击“解析并创建交接”时记录本次任务；发送方和接收方不能相同。相同源文件的同方向活动交接会复用，不重复创建。

也可手动选节点与文件，不要求先写规则。窗口只有复制格式和解析操作，没有额外的“粘贴回复”按钮。

## B 写回执，软件更新状态

给 B 的短提示只指向上下文文件，可通过手动复制或已启用的 CLI 路线投递。该文件与回执在任务的隐藏 `.relay` 子目录中；上下文给出当前文件、工作要求、回稿位置、回执位置及模板，不把协议正文反复复制到用户消息里。`reply_file` 每轮是新路径（第 0 轮 `reply.md`，之后 `reply-N.md`），回稿不互相覆盖。单次交接的上下文给出 `remaining_handoffs`。

单次交接的回执结构：

```json
{
  "task_id":"上下文中的任务ID",
  "attempt_id":"上下文中的尝试ID",
  "sender":"当前等待的节点ID",
  "turn":0,
  "status":"done",
  "needs_reply":false,
  "result_file":null,
  "next_action":null
}
```

任务 ID、尝试 ID、发送节点与轮次必须采用当前上下文原值。可先写 `received` 或 `working` 表示收到/处理，再写 `done` 或 `failed`。旧轮次不覆盖新轮次，错误或未写完的 JSON 不会当作完成。

- `done` 且 `needs_reply: false` 表示结束，无需回复寒暄或重复确认；同时给出非空 `next_action` 会因声明矛盾被拒绝。
- `done` 且 `needs_reply: true`：必须给出真实 `result_file` 与具体 `next_action`；任务继续等待另一方，按下一方向的设置自动投递或提供复制按钮。
- `failed`：任务失败，说明待处理原因；不能自动开启新一轮。
- 单次交接不能使用 `outcome`、`open_items`、`deliverable`。
- 默认 4 轮用完时任务暂停（`round_limit`），交由用户判断。

### 持续修改与验收

用户指定作者，另一方为验收者；角色与宿主类型无关。接收方为作者时从 `implement` 开始；发送方已有成果时接收方先验收（`review`），原工作要求文件作为第一次提交。上下文的 `collaboration` 对象给出 `role`、`phase`、`review_round`、`max_review_rounds`、`author`、`reviewer`、`acceptance_criteria`、`submission_file`、`open_items`、`allowed_outcomes`，`receipt_template` 已预填 `outcome`。

| 阶段 | 角色 | `outcome` | 要求与结果 |
| --- | --- | --- | --- |
| `implement` / `revise` | 作者 | `submitted` | 必须给出 `result_file`，进入验收 |
| `review` | 验收者 | `changes_requested` | `open_items` 为非空列表，作者修改 |
| `review` | 验收者 | `approved` | 不带 `open_items`，作者确认 |
| `accept` | 作者 | `accepted` | `deliverable` 写最终成果位置或简短交付说明，任务完成 |
| `accept` | 作者 | `submitted` | 作者有改动，回到验收，之前的通过作废 |

验收回执示例：

```json
{"task_id":"…","attempt_id":"…","sender":"…","turn":1,"status":"done","outcome":"changes_requested","open_items":["…"],"result_file":null,"next_action":null}
```

只有 `accepted` 结束任务，状态为 Succeeded，界面显示“已交付”。此类任务可省略 `needs_reply`；如提供，必须与 `outcome` 一致。`status:"failed"` 使任务暂停，原因 `agent_failed`。

### 暂停与用户决定

`paused` 不是终态，会话和文件保留。`pause_reason`：

- `round_limit`：单次交接默认 4 轮用完，或持续修改与验收的验收轮数用完。
- `turn_timeout`：单轮时限到期。时限取任务超时设置或任务的 `turn_timeout_minutes`，每轮重新计时。
- `no_progress`：作者重交字节相同的文件，或验收者重复完全相同的 `open_items`。
- `agent_failed`：持续修改与验收中某方回执 `failed`。

用户可继续（可附说明，放在下一轮请求开头；`round_limit` 须追加轮数）、取消或标记完成。标记完成记为 `operator_completed`，区别于 Agent 交付。超时轮次迟到的有效回执仍被接受，任务随之恢复。受管文件保留期到期时，未完成任务仍以超时结束。

GUI 通过文件事件读取回执，启动和手动刷新也会补读。已接收或正在处理时提示等待回执，不再把重复复制列为下一步。普通文件快照只能记录变更，不能代替 Agent 的明确完成声明。所有进程退出后没有常驻监视进程；通过 × 关闭到托盘时 GUI 仍在运行。

任务可以取消，也可以在确认后删除账本记录；删除保留磁盘文件，之后仍可按保留期清理。人工标记完成会记录用户决定并停止等待，不代表获得 Agent 完成证据，也不会停止已运行的外部进程。

新任务不再使用固定交接计划（`handoff_plan`）。早期版本创建、已带计划的任务仍按原计划完成，上下文继续给出 `required_next_agent`。

## 高级 CLI 接口扩充

文件交接在接收方已登记 CLI 方案、发送方向明确启用时可通过 CLI 投递。无人值守的自动投递还要求任务选择 `auto_deliver`，且该 Agent 对应能力已验证：新建会话需“新建已验证”，已有或已绑定会话需“续接已验证”。验证方式有两种：探针通过（Agent 还须写出标记文件，证明无人值守时有写回执的工具权限）并由用户确认会话可见；或经该方案真实投递的一轮交接，且 Agent 写出了本轮回执，记为证据。已登记但未验证的方案仍可手动点击“通过 CLI 发送”。

登录被拒（CLI 退出码为 0 时同样识别）、未选模型或缺少工具权限会为该 Agent 设置阻塞，停止自动投递，直到用户点击“我已处理 · 允许自动发送”或运行 `relay cli-recipe unblock --agent <节点ID>`。CLI 发送持续到本轮时限（print 模式宿主在 Agent 完成后才返回），GUI 在后台执行。投递未确认（超时、返回不同会话 ID、输出无法识别）时不自动重发；用户确认“未收到”后允许重发一次（`relay handoff allow-resend --task <任务ID> --reason <原因> --approve`）。

默认新建目标会话，也可明确选择已有会话；新建返回的真实 ID 或用户选择都存入同一 SQLite。后续往返按当前 attempt 的绑定精确续接，发送前核实宿主记录、检查返回 ID 一致。Relay 为本任务创建的会话无需发现能力即可续接，返回 ID 仍须一致；用户选择的已有会话仍需发现确认。来源未识别、会话失效或能力不足时保留手动复制，不猜最近会话。每轮只提交一次，投递不确定时不自动重发。细节见 [Session Registry 与发现](SESSIONS.md)。

方案可由一键配置直接导入，也可在编辑 Agent 中分区填写；CLI 仍提供 JSON 导入接口。包含 `version/name/host/output_format/create/resume/login/dependencies/supported_versions/source/license/limitations`；可选 `login` 是展示给用户的登录命令，Relay 不执行；命令含 `executable`、`arguments` 数组、可选 `input`、`working_directory` 和非敏感 `environment`。`{prompt}` 为必需模板变量，续接还需准确的 `{session_id}`，禁止 `latest` 之类隐式目标。

```powershell
dotnet .\relay.dll --home <中转站目录> cli-recipe import --agent <节点ID> --manifest <调用方案JSON>
```

导入只保存，不启动模型、安装程序或启用连接。Relay 负责读取下列原生输出，目标程序无需改成专用回执格式：

| `output_format` | 对应输出 |
| --- | --- |
| `dsh-jsonl` | DSH 的 JSONL 事件流 |
| `zcode-json` / `zcode-jsonl` | Zcode 的 JSON 对象或 JSONL 事件流 |
| `hermes-jsonl` | Hermes 的流式 JSON 事件 |
| `claude-json` | 采用相应结构的 CodeBuddy/WorkBuddy 等 JSON 输出 |
| `text` | 仅最终回复的普通文本；没有真实会话 ID 时无法验证同会话续接 |
| `relay-json` | 已有桥接的 `{"session_id":"真实标识","reply":"真实回复","accepted":true}` |
| `acp` | 支持 [Agent Client Protocol](https://agentclientprotocol.com) 的宿主，如 `hermes acp`、`codebuddy --acp` |

`acp` 方案的命令只启动 ACP 服务：不含 `{prompt}`、`{session_id}`，`input` 为 `null`，`resume` 与 `create` 写同一条命令。Relay 以 stdio JSON-RPC 发送 `initialize`、`session/new`（或按宿主能力 `session/resume`、`session/load`）和 `session/prompt`，取本轮 `agent_message_chunk` 作为回复，`stopReason` 为 `end_turn` 才算一轮结束；`session/load` 重放的历史不计入回复。无人值守时，宿主发来的 `session/request_permission` 由 Relay 选择 `allow_once`，等同于命令行的免确认参数，应只在信任的工作目录使用。宿主支持 ACP 时优先选用：参数和输出格式不随版本变化，新建与续接走同一协议。

选择已查证的输出格式，参数必须与安装版本的帮助及实际输出匹配。内置格式解析不等于所有版本或所有宿主都已接通。探针核对真实短标识回复与标记文件，并单独记录界面可见性，用于验证自动投递能力；这不代替已有会话的准确绑定与续接前验证。真实调用可能收费，不能保证缓存或所有未来会话可见。

配置检查不调用模型，只看本机前提：程序与入口文件是否存在、工作目录、续接命令、WorkBuddy 自带 CLI 与权限参数、Zcode 命令行的账号套餐与默认模型、当前阻塞和验证状态。Agent 管理的“检查 CLI 配置”按钮与下列命令等价；一键配置导入方案后也会运行它，把未通过项告诉用户。

```powershell
dotnet .\relay.dll --home <中转站目录> cli-recipe doctor --agent <节点ID>
dotnet .\relay.dll --home <中转站目录> cli-recipe confirm --agent <节点ID> --probe <探针ID> --visible
```

导入方案后，一键配置还要按 Relay 自带的 `relay-cache-parity` 技能做缓存对齐：让 Relay 续接会话时发出的请求与该 Agent 桌面端一致，用户在两边切换时不必为整段历史重新付费。技能写在 `<中转站目录>\skills\relay-cache-parity\SKILL.md`，配置提示词里会给出这个路径；也可以复制到 Agent 自己的技能目录。静态检查必须做；需要真实调用模型的对照实验，先说明轮数和会话，并取得用户同意。

```powershell
dotnet .\relay.dll --home <中转站目录> skill list
dotnet .\relay.dll --home <中转站目录> skill install --name relay-cache-parity --target <Agent 技能目录>
# 逐次请求的输入与缓存读取；对照桌面轮与 Relay 轮（目前支持 Zcode、Hermes）
dotnet .\relay.dll --home <中转站目录> agent cache-check --agent <节点ID> --session <会话ID>
```

`confirm` 记录用户已在宿主中看到探针会话，与测试窗口中的勾选相同。一键配置发现宿主需另装官方 CLI、ACP 适配器或其他桥接时，只向用户说明名称、来源、许可证、用途及不安装的后果（只能复制提示词手动交接），得到同意才安装，并写入方案 `dependencies`；需要交互登录的，把登录命令写入 `login`，由用户自己运行。

模型、工作目录和 profile 属于宿主执行配置。若新建提示未选择模型，应在宿主配置，或选择已配置的已有会话；续接默认继承原设置。当前没有跨宿主统一的模型/思考强度覆盖表单。会话 ID 由适配器输出或宿主元数据取得，不让模型自行查找；手动粘贴短 JSON 本身不能提供当前来源会话。

连接图节点绿点表示已收录调用方案，悬停区分待测与已验证；探针失败为红点，无方案为灰点。线路箭头及开关表示方向状态。节点绿点本身不代表已获自动投递权限。

字段、路径、正文与外部诊断保持原样。不要把用户实际会话记录、认证或私有路径提交到公开仓库。
