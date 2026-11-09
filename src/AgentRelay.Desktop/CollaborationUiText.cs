namespace AgentRelay.Desktop;

internal static class CollaborationUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Collaboration","协作方式","協作方式","進め方","협업 방식","Colaboración");
        Add("Single handoff","单次交接","單次交接","1 回の引き継ぎ","단일 전달","Envío único");
        Add("Keep revising until the review passes","持续修改与验收，直到通过","持續修改與驗收，直到通過","レビューに通るまで修正を続ける","검토를 통과할 때까지 수정 반복","Revisar hasta que se apruebe");
        Add("The reviewer checks each version. The task finishes only after the reviewer passes it and the author accepts and records the deliverable.",
            "验收者检查每一版。只有验收通过、作者确认接受并写明交付物后，任务才算完成。","驗收者檢查每一版。只有驗收通過、作者確認接受並寫明交付物後，任務才算完成。",
            "レビュー担当が各版を確認します。合格し、作成者が受け入れて成果物を記録した時点で完了します。","검토자가 각 버전을 확인합니다. 통과 후 작성자가 수락하고 결과물을 기록해야 완료됩니다.",
            "El revisor comprueba cada versión. La tarea termina solo cuando se aprueba y el autor acepta y registra el entregable.");
        Add("Recipient does the work; sender reviews","接收方完成工作，发送方验收","接收方完成工作，傳送方驗收","受信者が作業し、送信者がレビュー","수신자가 작업하고 발신자가 검토","El receptor trabaja; el emisor revisa");
        Add("Sender already has the work; recipient reviews","发送方已有成果，接收方验收","傳送方已有成果，接收方驗收","送信者の成果を受信者がレビュー","발신자의 결과물을 수신자가 검토","El emisor ya tiene el trabajo; el receptor revisa");
        Add("Acceptance criteria","验收标准","驗收標準","受け入れ基準","승인 기준","Criterios de aceptación");
        Add("Final reviewer","最终验收方","最終驗收方","最終レビュー担当","최종 검토자","Revisor final");
        Add("Initiating Agent","发起方 Agent","發起方 Agent","依頼側 Agent","발신 Agent","Agente emisor");
        Add("Receiving Agent","接收方 Agent","接收方 Agent","受信側 Agent","수신 Agent","Agente receptor");
        Add("If the receiving Agent is the final reviewer, the initial work request must include the existing work or its location; the initiating Agent makes any revisions.","选择接收方验收时，请在工作正文中提供已有成果或其位置；后续修改由发起方完成。","選擇接收方驗收時，請在工作正文中提供已有成果或其位置；後續修改由發起方完成。","受信側がレビューする場合、依頼に成果またはその場所を含めてください。","수신 측이 검토하는 경우 기존 결과물 또는 위치를 작업 요청에 포함하세요.","Si revisa el receptor, incluye el trabajo existente o su ubicación en la solicitud.");
        Add("The selected final reviewer checks each version against the criteria. The task ends only when that Agent approves with no unresolved issues. The other Agent makes revisions.","最终验收方依据验收标准检查每一版，确认没有待解决问题并通过后任务结束；另一方负责修改。","最終驗收方依照驗收標準檢查每一版，確認沒有待解決問題並通過後任務結束；另一方負責修改。","最終レビュー担当が基準に照らして確認し、未解決事項がなく承認すると完了します。","최종 검토자가 기준에 따라 확인하고 미해결 항목 없이 승인하면 완료됩니다.","El revisor final comprueba los criterios y aprueba cuando no quedan problemas.");
        Add("Acceptance criteria describe observable conditions the reviewer must check. Relay sends them with every review and records the Agent's decision; Relay does not independently verify the work.","验收标准是验收方需要检查的具体条件，例如“测试通过且操作说明完整”。Relay 每次验收都会发送标准并记录 Agent 的结论；软件不会自行验证结果。","驗收標準是驗收方需要檢查的具體條件，例如「測試通過且操作說明完整」。Relay 每次驗收都會傳送標準並記錄 Agent 的結論；軟體不會自行驗證結果。","受け入れ基準はレビューで確認する具体的な条件です。Relay は毎回送信し、Agent の判断を記録します。","승인 기준은 검토할 구체적인 조건입니다. Relay는 매번 이를 보내고 Agent의 결정을 기록합니다.","Los criterios son condiciones comprobables. Relay los envía en cada revisión y registra la decisión del agente.");
        Add("I will enter the acceptance criteria","手动填写验收标准","手動填寫驗收標準","受け入れ基準を入力","승인 기준 직접 입력","Introducir criterios");
        Add("Initiating Agent creates the acceptance criteria","由发起方 Agent 自动拟定验收标准","由發起方 Agent 自動擬定驗收標準","依頼側 Agent が基準を作成","발신 Agent가 기준 작성","El agente emisor prepara los criterios");
        Add("When the initiating Agent creates criteria, Relay first asks it to write the criteria. Select its source conversation for CLI delivery; otherwise copy the prompt to it manually.","自动拟定时，先由发起方 Agent 写入标准，再开始交接。选择来源会话可通过桥接发送；未选来源会话时，请手动复制提示词给它。","自動擬定時，先由發起方 Agent 寫入標準，再開始交接。選擇來源會話可透過橋接傳送；未選來源會話時，請手動複製提示詞給它。","自動作成では依頼側が先に基準を書きます。元の会話がない場合は手動でプロンプトを渡します。","자동 작성 시 발신 Agent가 먼저 기준을 작성합니다. 원본 대화가 없으면 프롬프트를 수동으로 전달하세요.","El agente emisor prepara primero los criterios. Sin conversación de origen, copia el mensaje manualmente.");
        Add("Review limit (0 means no limit)","验收次数上限（0 表示不设上限）","驗收次數上限（0 表示不設上限）","レビュー上限（0 は無制限）","검토 상한 (0은 무제한)","Límite de revisiones (0 sin límite)");
        Add("Without a limit, automatic calls may continue and incur model charges until the reviewer approves. No-progress pauses and per-turn time limits still apply.","不设上限时，自动调用可能持续至验收通过并产生模型费用；无进展暂停与单轮时限仍然生效。","不設上限時，自動呼叫可能持續至驗收通過並產生模型費用；無進展暫停與單輪時限仍然生效。","無制限では承認まで自動呼び出しが続き、料金が発生する場合があります。","제한이 없으면 승인까지 자동 호출이 계속되어 비용이 발생할 수 있습니다.","Sin límite, las llamadas pueden continuar hasta la aprobación y generar cargos.");
        Add("Enter the acceptance criteria or let the initiating Agent create them.","请填写验收标准，或选择由发起方 Agent 拟定。","請填寫驗收標準，或選擇由發起方 Agent 擬定。","基準を入力するか依頼側 Agent に作成させてください。","승인 기준을 입력하거나 발신 Agent가 작성하도록 선택하세요.","Introduce los criterios o deja que los prepare el agente emisor.");
        Add("Preparing acceptance criteria","拟定验收标准","擬定驗收標準","受け入れ基準を作成中","승인 기준 작성 중","Preparando criterios");
        Add("criteria_ready","验收标准已拟定","驗收標準已擬定","基準を作成","기준 작성 완료","Criterios preparados");
        Add("Maximum review rounds","最多验收轮数","最多驗收輪數","最大レビュー回数","최대 검토 횟수","Rondas de revisión máximas");
        Add("Time limit per turn (minutes)","单轮时限（分钟）","單輪時限（分鐘）","1 回あたりの制限時間（分）","회차별 제한 시간(분)","Límite por turno (minutos)");
        Add("Send each next turn through CLI automatically once that Agent's CLI is verified (may incur model charges)",
            "对方 CLI 已验证时，后续每一轮自动通过 CLI 发送（可能产生模型费用）","對方 CLI 已驗證時，後續每一輪自動透過 CLI 傳送（可能產生模型費用）",
            "相手の CLI が検証済みなら次の回を CLI で自動送信（モデル料金が発生する場合あり）","상대 CLI가 검증되면 다음 회차를 CLI로 자동 전송(모델 요금 발생 가능)",
            "Enviar cada turno por CLI automáticamente cuando el CLI esté verificado (puede generar cargos)");
        Add("Enter a whole number from {0} to {1}.","请输入 {0} 到 {1} 之间的整数。","請輸入 {0} 到 {1} 之間的整數。","{0}～{1} の整数を入力してください。","{0}에서 {1} 사이의 정수를 입력하세요.","Introduce un número entero de {0} a {1}.");
        Add("Chosen conversations stay bound to this task. Without a source conversation, turns for the sender are copied manually.",
            "所选会话持续绑定到本任务。未选来源会话时，交给发送方的轮次需手动复制提示词。","所選會話持續綁定此任務。未選來源會話時，交給傳送方的輪次需手動複製提示詞。",
            "選んだ会話はこのタスクに固定されます。元の会話を選ばない場合、送信者への回は手動でコピーします。","선택한 대화는 이 작업에 고정됩니다. 원본 대화가 없으면 발신자 차례는 수동으로 복사합니다.",
            "Las conversaciones elegidas quedan vinculadas. Sin conversación de origen, los turnos del emisor se copian a mano.");
        Add("Relay sends this turn to {0} through CLI automatically.","本轮将由 Relay 自动通过 CLI 发送给 {0}。","本輪將由 Relay 自動透過 CLI 傳送給 {0}。","この回は Relay が CLI で {0} に自動送信します。","이번 회차는 Relay가 CLI로 {0}에게 자동 전송합니다.","Relay envía este turno a {0} por CLI automáticamente.");
        Add("Not received · Allow resending","确认未收到 · 允许重发","確認未收到 · 允許重傳","未着を確認 · 再送を許可","미수신 확인 · 재전송 허용","No recibido · Permitir reenviar");
        Add("Relay still accepts a valid late receipt for this turn. Check the target conversation first and allow one more send only if the request did not arrive.",
            "Relay 仍会接收本轮迟到的有效回执。请先检查目标会话，确认本轮请求确实没有送达后，再允许重发一次。","Relay 仍會接收本輪遲到的有效回執。請先檢查目標會話，確認本輪請求確實沒有送達後，再允許重傳一次。",
            "この回の有効な応答が遅れて届いても、Relay は受け付けます。先に相手の会話を確認し、依頼が届いていない場合のみ再送を許可してください。","Relay는 이번 회차의 유효한 회신이 늦게 도착해도 받습니다. 먼저 대상 대화를 확인하고 요청이 도착하지 않은 경우에만 한 번 더 보내도록 허용하세요.",
            "Relay seguirá aceptando una confirmación válida aunque llegue tarde. Revisa primero la conversación de destino y permite un reenvío solo si la solicitud no llegó.");
        Add("Implementing","实现中","實作中","作業中","작업 중","Implementando");
        Add("Revising","修改中","修改中","修正中","수정 중","Revisando cambios");
        Add("Reviewing","验收中","驗收中","レビュー中","검토 중","En revisión");
        Add("Accepting the result","确认接受","確認接受","受け入れ確認","결과 수락 확인","Aceptando el resultado");
        Add("Paused · Waiting for your decision","已暂停 · 等待你决定","已暫停 · 等待你決定","一時停止 · 判断待ち","일시 중지 · 결정 대기","En pausa · Esperando tu decisión");
        Add("Paused","已暂停","已暫停","一時停止","일시 중지","En pausa");
        Add("Delivered","已交付","已交付","納品済み","전달 완료","Entregado");
        Add("Waiting for {0} · {1}","等待 {0} · {1}","等待 {0} · {1}","{0} を待機中 · {1}","{0} 대기 중 · {1}","Esperando a {0} · {1}");
        Add("{0} is processing · {1}","{0} 正在处理 · {1}","{0} 正在處理 · {1}","{0} が処理中 · {1}","{0} 처리 중 · {1}","{0} está trabajando · {1}");
        Add("Review loop · Author {0} · Reviewer {1} · Review round {2}/{3}","持续修改与验收 · 作者 {0} · 验收者 {1} · 第 {2}/{3} 轮验收","持續修改與驗收 · 作者 {0} · 驗收者 {1} · 第 {2}/{3} 輪驗收",
            "修正とレビュー · 作成者 {0} · レビュー担当 {1} · レビュー {2}/{3} 回目","수정·검토 · 작성자 {0} · 검토자 {1} · 검토 {2}/{3}회차","Revisión · Autor {0} · Revisor {1} · Ronda {2}/{3}");
        Add("Open items","待解决问题","待解決問題","未解決の項目","미해결 항목","Pendientes");
        Add("Deliverable","交付物","交付物","成果物","결과물","Entregable");
        Add("Next: if it passes, {0} accepts; if changes are needed, {0} revises.","下一步：通过则由 {0} 确认接受；需要修改则交回 {0} 修改。","下一步：通過則由 {0} 確認接受；需要修改則交回 {0} 修改。",
            "次：合格なら {0} が受け入れ、修正が必要なら {0} が修正します。","다음: 통과하면 {0}이(가) 수락하고, 수정이 필요하면 {0}이(가) 수정합니다.","Siguiente: si se aprueba, {0} acepta; si hay cambios, {0} los hace.");
        Add("Next: if {0} accepts, the task is delivered; a new change goes back to {1} for review.","下一步：{0} 确认接受即交付；如再修改，则交回 {1} 复验。","下一步：{0} 確認接受即交付；如再修改，則交回 {1} 複驗。",
            "次：{0} が受け入れると納品。新たな変更は {1} の再レビューへ戻ります。","다음: {0}이(가) 수락하면 전달 완료, 새 변경은 {1}의 재검토로 돌아갑니다.","Siguiente: si {0} acepta, se entrega; un cambio nuevo vuelve a {1} para revisión.");
        Add("Next: {0} reviews the submission.","下一步：由 {0} 验收提交内容。","下一步：由 {0} 驗收提交內容。","次：{0} が提出物をレビューします。","다음: {0}이(가) 제출물을 검토합니다.","Siguiente: {0} revisa la entrega.");
        Add("Turn {0} of at most {1}","第 {0} 轮，最多 {1} 轮","第 {0} 輪，最多 {1} 輪","{0} 回目（最大 {1} 回）","{0}회차 (최대 {1}회)","Turno {0} de {1} como máximo");
        Add("Note for the next Agent (optional)","给下一位 Agent 的说明（选填）","給下一位 Agent 的說明（選填）","次の Agent へのメモ（任意）","다음 Agent에게 남길 메모(선택)","Nota para el siguiente agente (opcional)");
        Add("Continue with {0} more rounds","追加 {0} 轮并继续","追加 {0} 輪並繼續","{0} 回追加して続行","{0}회 추가 후 계속","Continuar con {0} rondas más");
        Add("Continue","继续","繼續","続行","계속","Continuar");
        Add("submitted","已提交","已提交","提出","제출","Entregado para revisión");
        Add("changes_requested","要求修改","要求修改","修正依頼","수정 요청","Cambios solicitados");
        Add("approved","验收通过","驗收通過","合格","승인","Aprobado");
        Add("accepted","作者已接受","作者已接受","作成者が受け入れ","작성자 수락","Aceptado por el autor");
        Add("failed","处理失败","處理失敗","失敗","실패","Fallido");
        Add("Review history","验收记录","驗收紀錄","レビュー履歴","검토 기록","Historial de revisión");
        Add("CLI blocked · Action needed","CLI 受阻 · 需要处理","CLI 受阻 · 需要處理","CLI 停止中 · 対応が必要","CLI 차단됨 · 조치 필요","CLI bloqueado · Requiere acción");
        Add("New task","新建任务","新增任務","タスクを作成","새 작업","Nueva tarea");
        Add("Create task","创建任务","建立任務","タスクを作成","작업 만들기","Crear tarea");
        Add("Agent bridge","Agent 桥接","Agent 橋接","Agent ブリッジ","Agent 브리지","Puente del agente");
        Add("Agent bridges","Agent 桥接","Agent 橋接","Agent ブリッジ","Agent 브리지","Puentes de agentes");
        Add("Test bridge","测试桥接","測試橋接","ブリッジをテスト","브리지 테스트","Probar puente");
        Add("Send via bridge","通过桥接发送","透過橋接傳送","ブリッジで送信","브리지로 보내기","Enviar por el puente");
        Add("Check bridge setup (no model call)","检查桥接配置（不调用模型）","檢查橋接設定（不呼叫模型）","ブリッジ設定を確認（モデル呼び出しなし）","브리지 설정 확인(모델 호출 없음)","Revisar el puente (sin llamar al modelo)");
        Add("None · Copy prompts manually","无 · 手动复制提示词","無 · 手動複製提示詞","なし · プロンプトを手動でコピー","없음 · 프롬프트 수동 복사","Ninguno · Copiar el prompt a mano");
        Add("Desktop","桌面","桌面","デスクトップ","데스크톱","Escritorio");
        Add("Verified","已验证","已驗證","検証済み","검증됨","Verificado");
        Add("Needs attention","需要处理","需要處理","対応が必要","조치 필요","Requiere atención");
        Add("Not yet verified","待验证","待驗證","未検証","미검증","Sin verificar");
        Add("{0} has no Agent bridge","{0} 没有 Agent 桥接","{0} 沒有 Agent 橋接","{0} には Agent ブリッジがありません","{0}에는 Agent 브리지가 없습니다","{0} no tiene puente");
        Add("Relay cannot start or continue its conversations. After you create the task, copy the prompt and paste it into the conversation yourself; Relay follows the receipt it writes back.","Relay 无法替你新建或续接它的对话。创建任务后，请在任务卡片里复制提示词，自己粘贴到它的对话中（需要新对话时也请在它那边先建好）；它写回回执后，Relay 照常推进。","Relay 無法替你新增或續接它的對話。建立任務後，請在任務卡片複製提示詞，自己貼到它的對話中（需要新對話時也請先在它那邊建立）；它寫回回執後，Relay 照常推進。","Relay はこの Agent の会話を作成・再開できません。タスク作成後、タスクカードでプロンプトをコピーし、会話に貼り付けてください（新しい会話が必要なら先に作成してください）。応答が書き戻されれば Relay が進めます。","Relay는 이 Agent의 대화를 만들거나 이어갈 수 없습니다. 작업을 만든 뒤 작업 카드에서 프롬프트를 복사해 대화에 직접 붙여 넣으세요(새 대화가 필요하면 먼저 만들어 두세요). 회신이 기록되면 Relay가 이어서 진행합니다.","Relay no puede iniciar ni continuar sus conversaciones. Tras crear la tarea, copia el prompt desde la tarjeta y pégalo tú en la conversación (si hace falta una nueva, créala antes). Relay sigue cuando escriba su recibo.");
        Add("{0}'s bridge needs attention","{0} 的桥接需要处理","{0} 的橋接需要處理","{0} のブリッジに対応が必要です","{0}의 브리지에 조치가 필요합니다","El puente de {0} requiere atención");
        Add("Until then, copy the prompt manually.","处理好之前，请手动复制提示词。","處理好之前，請手動複製提示詞。","それまではプロンプトを手動でコピーしてください。","그 전까지는 프롬프트를 수동으로 복사하세요.","Mientras tanto, copia el prompt a mano.");
        Add("{0} · {1} bridge, not yet verified","{0} · {1} 桥接，待验证","{0} · {1} 橋接，待驗證","{0} · {1} ブリッジ（未検証）","{0} · {1} 브리지, 미검증","{0} · puente {1}, sin verificar");
        Add("Creating the task sends the first turn through this bridge. Once the Agent writes its receipt, the bridge counts as verified and later turns can go automatically.","创建任务时会通过这个桥接发出第一轮；对方写回回执即算验证通过，之后的轮次可以自动投递。","建立任務時會透過這個橋接發出第一輪；對方寫回回執即算驗證通過，之後的輪次可以自動投遞。","タスク作成時にこのブリッジで最初の回を送ります。応答が書き戻されると検証済みになり、以降は自動送信できます。","작업을 만들면 이 브리지로 첫 회차를 보냅니다. 회신이 기록되면 검증되어 이후 회차는 자동으로 보낼 수 있습니다.","Al crear la tarea se envía el primer turno por este puente. Cuando el agente escriba su recibo, queda verificado y los siguientes pueden ir solos.");
        Add("{0} · {1} bridge","{0} · {1} 桥接","{0} · {1} 橋接","{0} · {1} ブリッジ","{0} · {1} 브리지","{0} · puente {1}");
        Add("Relay delivers each turn into the chosen conversation, new or existing, and waits for the Agent's receipt.","Relay 会把每一轮直接送进所选的对话（新建或已有均可），并等待它写回回执。","Relay 會把每一輪直接送進所選的對話（新增或既有皆可），並等待它寫回回執。","Relay は各回を選んだ会話（新規・既存どちらも可）に直接届け、応答を待ちます。","Relay는 각 회차를 선택한 대화(새 대화 또는 기존 대화)로 바로 보내고 회신을 기다립니다.","Relay entrega cada turno en la conversación elegida, nueva o existente, y espera el recibo del agente.");
        Add("Write the work request and choose the conversations. Agents with a bridge receive it automatically; for others you copy the prompt. A real call may incur model charges.","填写工作要求并选择双方会话。有桥接的 Agent 会直接收到，没有桥接的需要你复制提示词。真实调用可能产生模型费用。","填寫工作要求並選擇雙方會話。有橋接的 Agent 會直接收到，沒有橋接的需要你複製提示詞。真實呼叫可能產生模型費用。","作業内容と会話を選びます。ブリッジのある Agent には自動で届き、ない Agent にはプロンプトをコピーします。モデル料金が発生する場合があります。","작업 요청과 대화를 선택하세요. 브리지가 있는 Agent는 자동으로 받고, 없는 Agent에게는 프롬프트를 복사합니다. 모델 요금이 발생할 수 있습니다.","Escribe la solicitud y elige las conversaciones. Los agentes con puente la reciben solos; a los demás les copias el prompt. Puede generar cargos.");
        Add("One-click setup is for a new node. To change an existing node, select it and use Edit Agent. An Agent bridge connects Relay to the Agent through ACP or its CLI; ACP is used when the host supports it. Without a bridge you copy prompts by hand.","一键配置用于新建节点；修改已有节点请选中后编辑。Agent 桥接是 Relay 连接 Agent 的方式：宿主支持 ACP 时优先用 ACP，否则用它的命令行；没有桥接的 Agent 只能手动复制提示词。","一鍵設定用於新增節點；修改既有節點請選取後編輯。Agent 橋接是 Relay 連接 Agent 的方式：宿主支援 ACP 時優先用 ACP，否則用其命令列；沒有橋接的 Agent 只能手動複製提示詞。","ワンクリック設定は新しいノード用です。既存ノードは選んで編集してください。Agent ブリッジは Relay と Agent をつなぐ方法で、ACP 対応なら ACP、なければ CLI を使います。ブリッジがなければ手動でコピーします。","원클릭 설정은 새 노드용입니다. 기존 노드는 선택 후 편집하세요. Agent 브리지는 Relay가 Agent에 연결하는 방식으로, ACP를 지원하면 ACP를, 아니면 CLI를 씁니다. 브리지가 없으면 프롬프트를 직접 복사합니다.","La configuración en un clic es para nodos nuevos; edita los existentes. El puente conecta Relay con el agente por ACP cuando el host lo admite, o por su CLI. Sin puente, copias el prompt a mano.");
        Add("Environment variables (NAME=value, one per line)","环境变量（每行一个 名称=值）","環境變數（每行一個 名稱=值）","環境変数（1 行に 名前=値）","환경 변수(한 줄에 이름=값)","Variables de entorno (NOMBRE=valor, una por línea)");
        Add("Write each environment variable as NAME=value.","每个环境变量请写成 名称=值。","每個環境變數請寫成 名稱=值。","環境変数は 名前=値 の形式で書いてください。","환경 변수는 이름=값 형식으로 쓰세요.","Escribe cada variable como NOMBRE=valor.");
        Add("Some desktop Agents keep their models, providers and API keys apart from their command line. Point the command line at the desktop's own configuration here (for example a provider file or data folder) so it uses the same models; only if the command line truly stores keys separately does it need the key once more.","部分桌面版 Agent 把模型、供应商和 API key 存在桌面自己的配置里，命令行看不到。可以在这里用环境变量让命令行指向桌面那份配置（例如供应商配置文件或数据目录），它就会用同样的模型；只有命令行确实单独保存 key 时，才需要再填一次 key。","部分桌面版 Agent 把模型、供應商和 API key 存在桌面自己的設定裡，命令列看不到。可以在這裡用環境變數讓命令列指向桌面那份設定（例如供應商設定檔或資料目錄），它就會用同樣的模型；只有命令列確實分開保存 key 時，才需要再填一次 key。","デスクトップ版 Agent の一部は、モデル・プロバイダー・API キーをデスクトップ側の設定に保存し、CLI からは見えません。ここで環境変数を使い CLI をデスクトップの設定（プロバイダー設定ファイルやデータフォルダー）に向ければ同じモデルを使えます。CLI がキーを別に保存する場合だけ、もう一度キーが必要です。","일부 데스크톱 Agent는 모델·공급자·API 키를 데스크톱 설정에 따로 저장해 CLI에서 보이지 않습니다. 여기서 환경 변수로 CLI가 데스크톱 설정(공급자 설정 파일이나 데이터 폴더)을 가리키게 하면 같은 모델을 씁니다. CLI가 키를 따로 저장하는 경우에만 키를 한 번 더 입력합니다.","Algunos agentes de escritorio guardan modelos, proveedores y claves aparte de su CLI. Apunta aquí la CLI a la configuración del escritorio (archivo de proveedores o carpeta de datos) para usar los mismos modelos; solo si la CLI guarda las claves por separado hará falta la clave otra vez.");
        Add("New conversations Relay starts for this Agent use the model below. Continued conversations keep their own model and reasoning level.","Relay 为这个 Agent 新建的会话使用下面的模型；续接已有会话时沿用该会话自己的模型和思考强度。","Relay 為這個 Agent 新增的會話使用下面的模型；續接既有會話時沿用該會話自己的模型與思考強度。","Relay がこの Agent に新しく作る会話は下のモデルを使います。既存の会話を続けるときはその会話のモデルと思考強度のままです。","Relay가 이 Agent에 새로 만드는 대화는 아래 모델을 씁니다. 기존 대화를 이어갈 때는 그 대화의 모델과 사고 강도를 유지합니다.","Las conversaciones nuevas que Relay crea usan este modelo; las que continúan conservan su propio modelo y nivel de razonamiento.");
        Add("Default model for new conversations","新会话默认模型","新會話預設模型","新しい会話の既定モデル","새 대화 기본 모델","Modelo por defecto para conversaciones nuevas");
        Add("Not set","未设置","未設定","未設定","설정 안 됨","Sin definir");
        Add("Current default","当前默认","目前預設","現在の既定","현재 기본값","Predeterminado actual");
        Add("Saved in the host's own configuration","保存在宿主自己的配置","保存在宿主自己的設定","ホスト自身の設定に保存","호스트 자체 설정에 저장","Guardado en la configuración del host");
        Add("Model","模型","模型","モデル","모델","Modelo");
        Add("Reasoning level","思考强度","思考強度","思考強度","사고 강도","Nivel de razonamiento");
        Add("Save default model","保存默认模型","儲存預設模型","既定モデルを保存","기본 모델 저장","Guardar modelo por defecto");
        Add("Select a model.","请选择模型。","請選擇模型。","モデルを選んでください。","모델을 선택하세요.","Elige un modelo.");
        Add("Account plans the host's command line has not signed in to are hidden; they work only inside the desktop window.","命令行未登录的账号套餐已隐藏，它们只能在桌面窗口里使用。","命令列未登入的帳號套餐已隱藏，它們只能在桌面視窗裡使用。","CLI でサインインしていないアカウントプランは非表示です。デスクトップ画面でのみ使えます。","CLI에서 로그인하지 않은 계정 요금제는 숨겨집니다. 데스크톱 창에서만 쓸 수 있습니다.","Se ocultan los planes de cuenta sin sesión en la CLI; solo funcionan en la ventana de escritorio.");
        Add("Conversations Relay creates are saved by the host right away, but its window may list them only after you reopen it.","Relay 新建的会话宿主会立即保存，但它的窗口可能要重新打开后才会在列表里显示。","Relay 新增的會話宿主會立即保存，但它的視窗可能要重新開啟後才會在清單顯示。","Relay が作った会話はすぐ保存されますが、ホストの画面では開き直すまで一覧に出ないことがあります。","Relay가 만든 대화는 바로 저장되지만 호스트 창을 다시 열어야 목록에 보일 수 있습니다.","El host guarda al instante las conversaciones que crea Relay, pero su ventana puede mostrarlas solo tras reabrirla.");
        Add("CLI capability","CLI 能力","CLI 能力","CLI 機能","CLI 기능","Capacidad CLI");
        Add("New conversations verified","新会话已验证","新會話已驗證","新規会話は検証済み","새 대화 검증됨","Conversaciones nuevas verificadas");
        Add("New conversations not verified","新会话未验证","新會話未驗證","新規会話は未検証","새 대화 미검증","Conversaciones nuevas sin verificar");
        Add("Resume verified","续接已验证","續接已驗證","再開は検証済み","이어가기 검증됨","Reanudación verificada");
        Add("Resume not verified","续接未验证","續接未驗證","再開は未検証","이어가기 미검증","Reanudación sin verificar");
        Add("Proven by a real handoff","已由真实交接证实","已由真實交接證實","実際の引き継ぎで確認済み","실제 전달로 확인됨","Comprobado con un envío real");
        Add("I have resolved this · Allow automatic sends","我已处理 · 允许自动发送","我已處理 · 允許自動傳送","対応済み · 自動送信を許可","처리 완료 · 자동 전송 허용","Ya lo resolví · Permitir envíos automáticos");
        Add("A probe or one real handoff whose Agent writes its receipt verifies the CLI. Only verified CLIs receive turns automatically; you can still send by hand.",
            "探针通过，或一次真实交接中 Agent 写回了回执，即可验证 CLI。只有已验证的 CLI 会自动接收轮次；仍可手动发送。","探針通過，或一次真實交接中 Agent 寫回了回執，即可驗證 CLI。只有已驗證的 CLI 會自動接收輪次；仍可手動傳送。",
            "プローブ、または Agent が応答を書いた実際の引き継ぎ 1 回で CLI を検証します。自動送信は検証済みの CLI のみで、手動送信は可能です。","프로브 또는 Agent가 회신을 기록한 실제 전달 1회로 CLI를 검증합니다. 검증된 CLI만 자동 수신하며 수동 전송은 가능합니다.",
            "Una prueba o un envío real en el que el agente escribe su recibo verifica el CLI. Solo los verificados reciben turnos automáticos; aún puedes enviar a mano.");
    }
}
