namespace AgentRelay.Desktop;

internal static class AgentEntryUiText
{
    public static void AddTo(Dictionary<string, string[]> entries)
    {
        void Add(string en, string cn, string tw, string ja, string ko, string es) => entries.Add(en, [en, cn, tw, ja, ko, es]);
        Add("New Agent", "新建 Agent", "新增 Agent", "Agent を作成", "Agent 만들기", "Nuevo agente");
        Add("Delete Agent", "删除 Agent", "刪除 Agent", "Agent を削除", "Agent 삭제", "Eliminar agente");
        Add("One-click setup", "一键配置", "一鍵設定", "ワンクリック設定", "원클릭 설정", "Configuración con un clic");
        Add("Each new node has a unique ID. Copy a fresh setup prompt for each node; do not give the same prompt to two Agents.",
            "每个新节点都有唯一 ID。每新增一个节点都要重新复制一次配置提示词，不要把同一段提示词交给两个 Agent。",
            "每個新節點都有唯一 ID。每新增一個節點都要重新複製一次設定提示詞，不要把同一段提示詞交給兩個 Agent。",
            "新しいノードにはそれぞれ固有の ID があります。ノードを追加するたびに設定プロンプトをコピーし、同じプロンプトを二つの Agent に渡さないでください。",
            "새 노드마다 고유 ID가 있습니다. 노드를 추가할 때마다 설정 프롬프트를 다시 복사하고 같은 프롬프트를 두 Agent에게 주지 마세요.",
            "Cada nodo nuevo tiene un ID único. Copia un prompt de configuración nuevo para cada nodo; no entregues el mismo prompt a dos agentes.");
        Add("Optional handoff rule: after adding an Agent, choose Install handoff rule. Relay can copy a deployment prompt for the Agent, or you can pick the instruction file yourself. Nothing is installed until the file and rule are confirmed.",
            "可选交接规则：添加 Agent 后，点击“安装交接规则”。Relay 可以复制部署提示词交给该 Agent，也可以由你手动选择指令文件；在文件和规则确认后才安装。",
            "可選交接規則：新增 Agent 後，點擊「安裝交接規則」。Relay 可以複製部署提示詞交給該 Agent，也可以由你手動選擇指令檔；在檔案和規則確認後才安裝。",
            "任意の引き継ぎルール：追加後に「引き継ぎ規則を追加」を押します。Relay は展開プロンプトをコピーするか、ユーザーが指示ファイルを選べます。ファイルとルールの確認後にのみ書き込みます。",
            "선택 사항인 전달 규칙: Agent를 추가한 뒤 전달 규칙 설치를 누르세요. Relay가 배포 프롬프트를 복사하거나 사용자가 지침 파일을 직접 선택할 수 있습니다. 파일과 규칙을 확인한 뒤에만 설치합니다.",
            "Regla de entrega opcional: después de añadir un agente, elige instalar la regla. Relay puede copiar un prompt de despliegue o puedes elegir tú el archivo de instrucciones; no se escribe nada hasta confirmar el archivo y la regla.");
        Add("Check rule status", "检查规则状态", "檢查規則狀態", "ルール状態を確認", "규칙 상태 확인", "Comprobar estado de la regla");
        Add("Copied. Paste it into the matching Agent to finish setup. One prompt works for one Agent only; copy a new prompt for each additional Agent.",
            "已复制，请粘贴给对应 Agent 完成配置；同一份提示词只能用于一个 Agent，新增其他 Agent 请重新复制。",
            "已複製，請貼給對應 Agent 完成設定；同一份提示詞只能用於一個 Agent，新增其他 Agent 請重新複製。",
            "コピーしました。対応する Agent に貼り付けて設定を完了してください。同じプロンプトは一つの Agent にのみ使えます。追加の Agent には新しくコピーしてください。",
            "복사했습니다. 해당 Agent에 붙여 넣어 설정을 완료하세요. 같은 프롬프트는 한 Agent에만 사용할 수 있으며, 추가 Agent에는 새로 복사하세요.",
            "Copiado. Pégalo en el agente correspondiente para terminar la configuración. Un prompt sirve para un solo agente; copia uno nuevo para cada agente adicional.");
        Add("Relay copies a setup prompt. Paste it into a new conversation with the Agent; it checks how it can be called and registers itself. One prompt works for one Agent only; copy a new prompt for each additional Agent.",
            "Relay 会复制一段配置提示词。请粘贴到该 Agent 的新对话，它会检查调用方式并自行登记。同一份提示词只能用于一个 Agent，新增其他 Agent 请重新复制。",
            "Relay 會複製一段設定提示詞。請貼到該 Agent 的新對話，它會檢查呼叫方式並自行登記。同一份提示詞只能用於一個 Agent，新增其他 Agent 請重新複製。",
            "Relay は設定プロンプトをコピーします。Agent の新しい会話に貼り付けると、呼び出し方法を確認して登録します。同じプロンプトは一つの Agent にのみ使え、追加の Agent には新しくコピーが必要です。",
            "Relay가 설정 프롬프트를 복사합니다. Agent의 새 대화에 붙여 넣으면 호출 방법을 확인해 스스로 등록합니다. 같은 프롬프트는 한 Agent에만 사용할 수 있으며 추가 Agent에는 새로 복사하세요.",
            "Relay copia un prompt de configuración. Pégalo en una conversación nueva con el agente; comprobará cómo se le llama y se registrará. Un prompt sirve para un solo agente; copia uno nuevo para cada agente adicional.");
        Add("Relay cannot know which instruction file this host actually loads. Choose how to confirm it. Nothing is written until the file and the one-line rule are confirmed.",
            "Relay 无法判断这个宿主实际加载哪个指令文件。请选择确认方式；在文件和一行规则确认前不会写入任何内容。",
            "Relay 無法判斷這個宿主實際載入哪個指令檔。請選擇確認方式；在檔案和一行規則確認前不會寫入任何內容。",
            "Relay はこのホストが実際に読み込む指示ファイルを特定できません。確認方法を選んでください。ファイルと一行ルールの確認まで何も書き込みません。",
            "Relay는 이 호스트가 실제로 읽는 지침 파일을 알 수 없습니다. 확인 방법을 선택하세요. 파일과 한 줄 규칙을 확인하기 전에는 아무것도 쓰지 않습니다.",
            "Relay no puede saber qué archivo de instrucciones carga este host. Elige cómo confirmarlo; no se escribe nada hasta confirmar el archivo y la regla de una línea.");
        Add("Copy a deployment prompt", "复制部署提示词", "複製部署提示詞", "展開プロンプトをコピー", "배포 프롬프트 복사", "Copiar prompt de despliegue");
        Add("Paste it into this Agent's conversation. The Agent must identify its own instruction file and get your approval before changing it; then it installs the rule. Relay shows the rule as pending until that confirmation is recorded.",
            "粘贴到该 Agent 的对话。Agent 必须先确认自己实际加载的指令文件，并取得你的同意后才能修改；随后安装规则。在安装被记录前，Relay 会把它显示为待部署。",
            "貼到該 Agent 的對話。Agent 必須先確認自己實際載入的指令檔，並取得你的同意後才能修改；接著安裝規則。在安裝被記錄前，Relay 會顯示為待部署。",
            "この Agent の会話に貼り付けます。Agent は実際に読み込む指示ファイルを特定し、変更前にユーザーの同意を得る必要があります。その後ルールを導入します。記録されるまで Relay は保留中と表示します。",
            "이 Agent의 대화에 붙여 넣으세요. Agent가 실제로 읽는 지침 파일을 확인하고 변경 전에 사용자 동의를 받아야 합니다. 그런 다음 규칙을 설치합니다. 기록되기 전까지 Relay는 대기 중으로 표시합니다.",
            "Pégalo en la conversación de este agente. El agente debe identificar su archivo de instrucciones real y obtener tu aprobación antes de cambiarlo; después instala la regla. Relay la muestra como pendiente hasta registrar la instalación.");
        Add("Choose the instruction file myself", "我自己选择指令文件", "我自己選擇指令檔", "指示ファイルを自分で選ぶ", "지침 파일 직접 선택", "Elegir yo el archivo de instrucciones");
        Add("Pick the md file your host actually loads, review the one-line rule, and let Relay write it. Other instructions and a backup are preserved.",
            "选择宿主实际加载的 md 文件，检查一行规则后由 Relay 写入。其他指令内容和备份都会保留。",
            "選擇宿主實際載入的 md 檔案，檢查一行規則後由 Relay 寫入。其他指令內容和備份都會保留。",
            "ホストが実際に読み込む md ファイルを選び、一行ルールを確認して Relay に書き込ませます。他の指示とバックアップは保持されます。",
            "호스트가 실제로 읽는 md 파일을 선택하고 한 줄 규칙을 확인한 뒤 Relay가 기록하게 하세요. 다른 지침과 백업은 보존됩니다.",
            "Elige el archivo md que carga el host, revisa la regla de una línea y deja que Relay la escriba. Se conservan las demás instrucciones y una copia de seguridad.");
        Add("Deployment prompt copied. Paste it into this Agent's conversation. The Agent must confirm its own instruction file and get your approval before installing; Relay shows the rule as pending until the installation is recorded.",
            "部署提示词已复制。请粘贴到该 Agent 的对话。Agent 必须先确认自己实际加载的指令文件，并取得你的同意后安装；在安装被记录前，Relay 会显示为待部署。",
            "部署提示詞已複製。請貼到該 Agent 的對話。Agent 必須先確認自己實際載入的指令檔，並取得你的同意後安裝；在安裝被記錄前，Relay 會顯示為待部署。",
            "展開プロンプトをコピーしました。この Agent の会話に貼り付けてください。Agent は実際の指示ファイルを確認し、同意を得てから導入します。記録されるまで Relay は保留中と表示します。",
            "배포 프롬프트를 복사했습니다. 이 Agent의 대화에 붙여 넣으세요. Agent는 실제 지침 파일을 확인하고 동의를 받은 뒤 설치해야 합니다. 기록되기 전까지 Relay는 대기 중으로 표시합니다.",
            "Prompt de despliegue copiado. Pégalo en la conversación del agente. Debe confirmar su archivo de instrucciones y obtener tu aprobación antes de instalar; Relay lo muestra como pendiente hasta registrar la instalación.");
        Add("Pending deployment: prompt copied {0}. Waiting for the Agent to confirm its instruction file and install; copy a fresh prompt if that conversation was lost.",
            "待部署：提示词复制于 {0}。等待 Agent 确认指令文件并安装；如果该对话已丢失，请重新复制提示词。",
            "待部署：提示詞複製於 {0}。等待 Agent 確認指令檔並安裝；若該對話已遺失，請重新複製提示詞。",
            "保留中: プロンプトのコピー日時 {0}。Agent が指示ファイルを確認して導入するのを待っています。会話を失った場合は新しくコピーしてください。",
            "대기 중: 프롬프트 복사 시각 {0}. Agent가 지침 파일을 확인해 설치하기를 기다리는 중입니다. 대화를 잃었다면 새로 복사하세요.",
            "Pendiente: prompt copiado el {0}. Esperando a que el agente confirme su archivo e instale; si perdiste esa conversación, copia un prompt nuevo.");
        Add("Select the probes to clean up. Only this Agent's probes are listed; nothing is removed until you confirm.",
            "选择要清理的探针。列表只包含当前 Agent 的探针；在你确认前不会删除任何内容。",
            "選擇要清理的探針。清單只包含目前 Agent 的探針；在你確認前不會刪除任何內容。",
            "整理するプローブを選択します。現在の Agent のプローブのみ表示され、確認するまで削除しません。",
            "정리할 프로브를 선택하세요. 현재 Agent의 프로브만 표시되며 확인 전에는 삭제하지 않습니다.",
            "Elige las sondas que quieres limpiar. Solo se listan las de este agente; no se borra nada hasta que confirmes.");
        Add("Select the probe sessions to clean up; nothing is removed until you confirm.",
            "选择要清理的探针会话；在你确认前不会删除任何内容。",
            "選擇要清理的探針工作階段；在你確認前不會刪除任何內容。",
            "整理するプローブセッションを選択してください。確認するまで何も削除しません。",
            "정리할 프로브 세션을 선택하세요. 확인 전에는 아무것도 삭제하지 않습니다.",
            "Elige las sesiones de sonda que quieres limpiar; no se borra nada hasta que confirmes.");
        Add("Clean up selected probes", "清理所选探针", "清理所選探針", "選択したプローブを整理", "선택한 프로브 정리", "Limpiar sondas seleccionadas");
        Add("Cancel probe selection", "取消探针选择", "取消探針選擇", "プローブ選択を取消", "프로브 선택 취소", "Cancelar selección de sondas");
        Add("Select at least one probe.", "请至少选择一个探针。", "請至少選擇一個探針。", "プローブを1つ以上選択してください。", "프로브를 하나 이상 선택하세요.", "Selecciona al menos una sonda.");
        Add("Legacy probe without a pre-probe session record; ownership cannot be confirmed and it will be kept.",
            "旧探针没有新建前的会话记录，无法确认归属，将保留。",
            "舊探針沒有新增前的工作階段記錄，無法確認歸屬，將保留。",
            "旧プローブには作成前のセッション記録がなく、所有を確認できないため保持します。",
            "이전 프로브에는 생성 전 세션 기록이 없어 소유를 확인할 수 없으므로 보존합니다.",
            "Sonda antigua sin registro de sesión previo; no se puede confirmar la propiedad y se conservará.");
        Add("The probe did not finish normally; its artifacts are kept.",
            "探针未正常结束，其产物将保留。",
            "探針未正常結束，其產物將保留。",
            "プローブが正常終了していないため、成果物を保持します。",
            "프로브가 정상 종료되지 않아 산출물을 보존합니다.",
            "La sonda no terminó normalmente; sus artefactos se conservan.");
        Add("Clean up {0} selected probe(s) for {1}? Verification stays enabled. Sessions used by real tasks, items whose ownership cannot be confirmed, and unsupported items are kept and reported. Selected sessions: {2}",
            "确定清理 {1} 的 {0} 个所选探针吗？验证状态保持不变。真实任务使用的会话、无法确认归属的项目和不支持的项目都会保留并报告。所选会话：{2}",
            "確定清理 {1} 的 {0} 個所選探針嗎？驗證狀態保持不變。真實任務使用的工作階段、無法確認歸屬的項目和不支援的項目都會保留並報告。所選工作階段：{2}",
            "{1} の選択した {0} 個のプローブを整理しますか？検証状態は維持されます。実タスクが使うセッション、所有を確認できない項目、未対応の項目は保持して報告します。選択セッション: {2}",
            "{1}의 선택한 프로브 {0}개를 정리할까요? 검증 상태는 유지됩니다. 실제 작업이 사용하는 세션, 소유를 확인할 수 없는 항목, 지원되지 않는 항목은 보존하고 보고합니다. 선택한 세션: {2}",
            "¿Limpiar {0} sonda(s) seleccionada(s) de {1}? La verificación se mantiene. Las sesiones usadas por tareas reales, los elementos sin propiedad confirmada y los no compatibles se conservan y se informan. Sesiones seleccionadas: {2}");
        Add("New session", "新会话", "新增工作階段", "新規セッション", "새 세션", "Sesión nueva");
        Add("Same session", "同一会话", "同一工作階段", "同じセッション", "같은 세션", "Misma sesión");
        Add("Probe", "探针", "探針", "プローブ", "프로브", "Sonda");
        Add("None", "无", "無", "なし", "없음", "Ninguna");

        Add("Not installed", "未安装", "未安裝", "未導入", "설치되지 않음", "No instalada");
        Add("Recorded installation: {0}", "已登记安装路径：{0}", "已登記安裝路徑：{0}", "登録済みの導入先: {0}", "기록된 설치 위치: {0}", "Instalación registrada: {0}");
        Add("Use one name per Agent. Configure it directly with a prompt, or enter the fields yourself.",
            "每个 Agent 只需一个名称。用一键配置让 Agent 直接登记，也可按字段手动填写。", "每個 Agent 只需一個名稱。可一鍵設定直接登記，或按欄位手動填寫。",
            "Agent ごとに名前は一つです。ワンクリック設定か手動入力で登録します。", "Agent마다 이름 하나만 사용합니다. 원클릭 설정이나 직접 입력으로 등록하세요.", "Un nombre por agente. Regístralo con un prompt o introduce los campos manualmente.");
        Add("Setup prompt copied. Paste it into a new conversation with the Agent you want to register. It will inspect its calling method and fill Relay directly. This creates a new node and does not overwrite the selected Agent.",
            "配置提示词已复制。请粘贴到想要登记的 Agent 的新对话窗口，它会调查调用方式并直接配置 Relay。本次新建节点，不会覆盖当前选中的 Agent。",
            "設定提示詞已複製。請貼到要登記的 Agent 的新對話視窗，它會調查呼叫方式並直接設定 Relay。本次新增節點，不會覆蓋目前選取的 Agent。",
            "設定プロンプトをコピーしました。登録する Agent の新しい会話に貼り付けてください。呼び出し方法を調べて Relay に直接登録します。選択中の Agent は上書きしません。",
            "설정 프롬프트를 복사했습니다. 등록할 Agent의 새 대화에 붙여 넣으세요. 호출 방법을 조사해 Relay에 직접 등록합니다. 선택한 Agent를 덮어쓰지 않습니다.",
            "Prompt copiado. Pégalo en una conversación nueva con el agente que quieras registrar. Investigará su método de llamada y configurará Relay directamente. No sobrescribe el agente seleccionado.");
        Add("One-click setup is for a new node. To change an existing node, select it and use Edit Agent. A configured CLI is ready to test; it is not a failed connection.",
            "一键配置用于新建节点；修改已有节点请选中后编辑。已配置 CLI 表示可以发起测试，不代表测试失败。", "一鍵設定用於新增節點；修改既有節點請選取後編輯。已設定 CLI 表示可發起測試，不代表測試失敗。",
            "ワンクリック設定は新規登録用です。既存の Agent は選択して編集してください。CLI 設定済みはテスト可能を意味します。", "원클릭 설정은 새 노드용입니다. 기존 노드는 선택 후 편집하세요. CLI 설정 완료는 테스트 준비 상태입니다.", "La configuración con un clic crea nodos nuevos. Para cambiar uno, selecciónalo y edítalo. Una CLI configurada está lista para probar.");
        Add("Handoff rules and node details", "交接规则与节点详情", "交接規則與節點詳情", "引き継ぎルールとノード詳細", "전달 규칙 및 노드 정보", "Reglas de envío y detalles del nodo");
        Add("CLI configured · Ready to test", "CLI 已配置 · 待测试", "CLI 已設定 · 待測試", "CLI 設定済み · テスト可能", "CLI 설정됨 · 테스트 준비", "CLI configurada · Lista para probar");
        Add("CLI not configured", "CLI 未配置", "CLI 未設定", "CLI 未設定", "CLI 미설정", "CLI sin configurar");
        Add("CLI program recorded · Add calling arguments", "已记录 CLI 程序 · 待补调用参数", "已記錄 CLI 程式 · 待補呼叫參數", "CLI 登録済み · 引数が必要", "CLI 프로그램 기록됨 · 인수 필요", "Programa CLI registrado · Faltan argumentos");
        Add("CLI probe passed · Confirm visibility", "CLI 探针通过 · 待确认会话可见", "CLI 探針通過 · 待確認會話可見", "CLI 応答確認済み · 会話表示を確認", "CLI 프로브 통과 · 세션 표시 확인 필요", "Sonda CLI aprobada · Confirma visibilidad");
        Add("CLI probe failed · Retry available", "CLI 探针失败 · 可重试", "CLI 探針失敗 · 可重試", "CLI テスト失敗 · 再試行可能", "CLI 프로브 실패 · 재시도 가능", "Sonda CLI fallida · Puedes reintentar");
        Add("CLI calling method (optional)", "CLI 调用方式（选填）", "CLI 呼叫方式（選填）", "CLI 呼び出し方法（任意）", "CLI 호출 방법 (선택)", "Método de llamada CLI (opcional)");
        Add("Record a program alone, or add arguments containing {prompt} to save a testable calling method. Put one argument on each line.",
            "可以仅记录程序；要发起测试，还需填写包含 {prompt} 的参数或标准输入模板。参数每行一个。", "可以僅記錄程式；要測試還需填寫含 {prompt} 的參數或標準輸入模板。參數每行一個。",
            "実行ファイルのみの登録も可能です。テストには {prompt} を含む引数または標準入力が必要です。引数は1行に1つ記入します。", "프로그램만 기록할 수 있습니다. 테스트하려면 {prompt}가 포함된 인수나 표준 입력을 추가하세요. 인수는 줄마다 하나씩 입력하세요.", "Puedes registrar solo el programa. Para probarlo, añade argumentos o entrada estándar con {prompt}. Un argumento por línea.");
        Add("CLI arguments (one per line)", "CLI 调用参数（每行一个）", "CLI 呼叫參數（每行一個）", "CLI 引数（1行に1つ）", "CLI 인수 (줄당 하나)", "Argumentos CLI (uno por línea)");
        Add("Standard input template (optional)", "标准输入模板（选填）", "標準輸入模板（選填）", "標準入力テンプレート（任意）", "표준 입력 템플릿 (선택)", "Plantilla de entrada estándar (opcional)");
        Add("CLI output format", "CLI 输出格式", "CLI 輸出格式", "CLI 出力形式", "CLI 출력 형식", "Formato de salida CLI");
        Add("CLI working directory", "CLI 工作目录（选填）", "CLI 工作目錄（選填）", "CLI 作業ディレクトリ", "CLI 작업 디렉터리", "Directorio de trabajo CLI");
        Add("CLI version (optional)", "CLI 版本（选填）", "CLI 版本（選填）", "CLI バージョン（任意）", "CLI 버전 (선택)", "Versión CLI (opcional)");
        Add("Enter a CLI program before its arguments.", "请先填写 CLI 程序路径，再填写参数。", "請先填寫 CLI 程式路徑，再填寫參數。", "引数の前に CLI の実行ファイルを指定してください。", "인수보다 먼저 CLI 프로그램을 입력하세요.", "Introduce un programa CLI antes de sus argumentos.");
        Add("CLI arguments must include {prompt}.", "调用参数或标准输入模板必须包含 {prompt}。", "呼叫參數或標準輸入模板必須包含 {prompt}。", "引数または標準入力には {prompt} が必要です。", "인수 또는 표준 입력에 {prompt}가 필요합니다.", "Los argumentos o la entrada estándar deben incluir {prompt}.");
        Add("A PATH command needs calling arguments; to record only a program, use its full path.", "使用 PATH 命令时请补充调用参数；如仅记录程序，请使用完整路径。", "使用 PATH 命令時請補充呼叫參數；如僅記錄程式，請使用完整路徑。", "PATH コマンドには引数を指定してください。実行ファイルのみ記録する場合は絶対パスを使用します。", "PATH 명령에는 호출 인수가 필요합니다. 프로그램만 기록하려면 전체 경로를 사용하세요.", "Un comando PATH necesita argumentos. Para registrar solo el programa, usa su ruta absoluta.");
    }
}
