namespace TaskBaton.Desktop;

internal static class SimplifiedText
{
    public static void AddTo(Dictionary<string, string[]> entries)
    {
        void Add(string en, string cn, string tw, string ja, string ko, string es) => entries[en] = [en, cn, tw, ja, ko, es];
        Add("CLI probe passed · Confirm session visibility", "CLI 探针通过 · 请确认会话可见", "CLI 探針通過 · 請確認工作階段可見", "CLI テスト成功 · セッションの表示を確認してください", "CLI 테스트 통과 · 세션 표시를 확인하세요", "Prueba CLI superada · Confirma la visibilidad de la sesión");
        Add("CLI probe failed · See the test result", "CLI 探针失败 · 请查看测试结果", "CLI 探針失敗 · 請查看測試結果", "CLI テスト失敗 · 結果を確認してください", "CLI 테스트 실패 · 결과를 확인하세요", "Prueba CLI fallida · Consulta el resultado");
        Add("CLI operation failed · See the test window", "CLI 操作失败 · 请查看测试窗口", "CLI 操作失敗 · 請查看測試視窗", "CLI 操作失敗 · テスト画面を確認してください", "CLI 작업 실패 · 테스트 창을 확인하세요", "Operación CLI fallida · Consulta la ventana de prueba");
        Add("Visibility confirmation saved", "会话可见性确认已保存", "工作階段可見性確認已儲存", "セッション表示の確認を保存しました", "세션 표시 확인을 저장했습니다", "Confirmación de visibilidad guardada");
        Add("Handoffs", "交接情况", "交接情況", "引き継ぎ状況", "전달 상태", "Envíos");
        Add("Unfinished handoffs", "有未完成交接", "有未完成交接", "未完了の引き継ぎあり", "미완료 전달 있음", "Envíos pendientes");
        Add("No unfinished handoffs", "无未完成交接", "無未完成交接", "未完了の引き継ぎなし", "미완료 전달 없음", "Sin envíos pendientes");
        Add("This Agent has unfinished handoffs. Complete or cancel them before removing it.", "该 Agent 有未完成交接，请先完成或取消，再移除节点。", "此 Agent 有未完成交接，請先完成或取消，再移除節點。", "未完了の引き継ぎがあります。完了またはキャンセルしてから削除してください。", "미완료 전달이 있습니다. 완료하거나 취소한 후 Agent를 제거하세요.", "Este agente tiene envíos pendientes. Complétalos o cancélalos antes de quitarlo.");
        Add("Desktop notifications enabled", "桌面通知已启用", "桌面通知已啟用", "デスクトップ通知を有効にしました", "데스크톱 알림을 활성화했습니다", "Notificaciones de escritorio activadas");
        Add("Desktop notifications disabled", "桌面通知已关闭", "桌面通知已關閉", "デスクトップ通知を無効にしました", "데스크톱 알림을 비활성화했습니다", "Notificaciones de escritorio desactivadas");
        Add("Request", "请求内容", "請求內容", "依頼内容", "요청 내용", "Solicitud");
        Add("Manual node", "手动节点", "手動節點", "手動ノード", "수동 노드", "Nodo manual");
        Add("Waiting for handoff details…", "等待填写交接内容…", "等待填寫交接內容…", "引き継ぎ内容の入力待ち…", "전달 내용 입력 대기 중…", "Esperando los datos del envío…");
        Add("Waiting for Agent details…", "等待填写 Agent 信息…", "等待填寫 Agent 資訊…", "Agent 情報の入力待ち…", "Agent 정보 입력 대기 중…", "Esperando los datos del agente…");
        Add("Waiting for reply file selection…", "等待选择回稿文件…", "等待選擇回稿檔案…", "返信ファイルの選択待ち…", "응답 파일 선택 대기 중…", "Esperando la selección de la respuesta…");
        Add("Waiting for confirmation…", "等待确认…", "等待確認…", "確認待ち…", "확인 대기 중…", "Esperando confirmación…");
        Add("Importing reply…", "正在导入回稿…", "正在匯入回稿…", "返信を取り込み中…", "응답 가져오는 중…", "Importando respuesta…");
        Add("CLI test window open…", "请在 CLI 测试窗口继续操作…", "請在 CLI 測試視窗繼續操作…", "CLI テスト画面で操作してください…", "CLI 테스트 창에서 계속하세요…", "Continúa en la ventana de prueba CLI…");
        Add("Running CLI probe…", "正在执行 CLI 探针…", "正在執行 CLI 探針…", "CLI プローブを実行中…", "CLI 테스트 실행 중…", "Ejecutando prueba CLI…");
        Add("Saving visibility confirmation…", "正在保存可见性确认…", "正在儲存可見性確認…", "表示の確認を保存中…", "표시 확인 저장 중…", "Guardando confirmación de visibilidad…");
        Add("One line per pair. Arrowheads show the directions with a calling recipe; the graph is read-only.", "每对 Agent 共用一条线。箭头表示有调用方案的方向；连接图只读。", "每對 Agent 共用一條線。箭頭表示有呼叫方案的方向；連線圖唯讀。", "Agent の組ごとに線は1本です。矢印は呼び出し方法がある方向を示し、図は読み取り専用です。", "Agent 쌍마다 선 하나를 표시합니다. 화살표는 호출 방법이 있는 방향이며 그래프는 읽기 전용입니다.", "Una línea por pareja. Las flechas indican las direcciones con método; el gráfico es de solo lectura.");
        Add("A → B means A can deliver files through Relay and notify B by CLI. A double arrow means both calling directions have a recipe. Green means available; gray means no recipe. Relay chooses automatic delivery from the recipient's verified bridge; manual copy always works, and the graph never pauses a direction.", "A → B 表示 A 可通过 Relay 传递文件并用 CLI 通知 B；双向箭头表示两个方向都有调用方案。绿色表示可用，灰色表示缺少方案。自动投递取决于接收方已验证的桥接；手动复制始终可用，连接图不会暂停任何方向。", "A → B 表示 A 可透過 Relay 傳遞檔案並以 CLI 通知 B；雙向箭頭表示兩個方向都有呼叫方案。綠色表示可用，灰色表示缺少方案。自動投遞取決於接收方已驗證的橋接；手動複製始終可用，連線圖不會暫停任何方向。", "A → B は Relay でファイルを渡し、CLI で B に通知できることを示します。両矢印は両方向に設定がある状態です。緑は利用可能、灰は設定なしです。自動送信は受信側の検証済みブリッジに従い、手動コピーは常に可能で、図は方向を停止しません。", "A → B는 Relay로 파일을 전달하고 CLI로 B에 알릴 수 있다는 뜻입니다. 양방향 화살표는 두 방향 모두 호출 방법이 있음을 나타냅니다. 녹색은 사용 가능, 회색은 방법 없음입니다. 자동 전송은 수신자의 검증된 브리지에 따르며 수동 복사는 항상 가능하고 그래프는 방향을 중지하지 않습니다.", "A → B indica que A puede entregar archivos y avisar a B por CLI. La doble flecha indica métodos en ambos sentidos. Verde: disponible; gris: sin método. El envío automático depende del puente verificado del receptor; la copia manual siempre funciona y el gráfico nunca pausa una dirección.");
        Add("Save", "保存", "儲存", "保存", "저장", "Guardar");
        Add("Calling recipe", "调用方案", "呼叫方案", "呼び出し設定", "호출 설정", "Método de llamada");
        Add("Close", "关闭", "關閉", "閉じる", "닫기", "Cerrar");
        Add("Marker", "识别标记", "識別標記", "識別マーカー", "식별 표식", "Marcador");
        Add("Session", "会话", "工作階段", "セッション", "세션", "Sesión");
        Add("Machine receipt passed", "机器回执通过", "機器回執通過", "応答の検証に成功", "응답 검증 통과", "Respuesta verificada");
        Add("Run new-session probe", "发起新会话探针", "發起新工作階段探針", "新規セッションをテスト", "새 세션 테스트", "Probar sesión nueva");
        Add("Run same-session probe", "测试原会话续接", "測試原工作階段續接", "同じセッションで再開をテスト", "동일 세션 재개 테스트", "Probar la misma sesión");
        Add("Save visibility confirmation", "保存可见性确认", "儲存可見性確認", "表示の確認を保存", "표시 확인 저장", "Guardar confirmación de visibilidad");
        Add("I found this marker in the target Agent's session list.", "我已在目标 Agent 的会话界面找到此标记。", "我已在目標 Agent 的工作階段介面找到此標記。", "対象 Agent のセッション画面でこのマーカーを確認しました。", "대상 Agent의 세션 화면에서 이 표식을 확인했습니다.", "Encontré este marcador en la sesión del agente de destino.");
        Add("I found this marker in the SAME session, not a new session.", "我已在同一个原会话中找到此标记，未新建会话。", "我已在同一個原工作階段中找到此標記，未新增工作階段。", "新規ではなく、同じセッションでこのマーカーを確認しました。", "새 세션이 아닌 동일한 기존 세션에서 이 표식을 확인했습니다.", "Encontré este marcador en la MISMA sesión, no en una nueva.");
        Add("CLI is not enabled until the receipt passes and you confirm visibility.", "机器回执通过且你确认会话可见后，才能使用该 CLI 能力。", "機器回執通過且你確認工作階段可見後，才能使用此 CLI 能力。", "応答の検証と表示の確認が完了すると CLI 機能を利用できます。", "응답 검증과 세션 표시 확인을 마친 후 CLI 기능을 사용할 수 있습니다.", "La CLI solo estará disponible tras verificar la respuesta y confirmar su visibilidad.");
        Add("No CLI recipe is registered. Open Settings → Advanced: CLI interface extensions to copy a prompt for an Agent that knows the calling method.", "尚未配置 CLI 调用方案。请在设置 → 高级：CLI 接口扩充中复制提示词，交给了解调用方式的 Agent。", "尚未設定 CLI 呼叫方案。請在設定 → 進階：CLI 介面擴充中複製提示詞，交給了解呼叫方式的 Agent。", "CLI 設定がありません。設定の CLI 拡張からプロンプトをコピーし、呼び出し方法を知る Agent に渡してください。", "CLI 설정이 없습니다. 설정의 CLI 확장에서 프롬프트를 복사해 호출 방법을 아는 Agent에게 전달하세요.", "No hay método CLI. Copia el prompt de extensiones CLI en Ajustes y entrégalo a un agente que conozca el método.");
        Add("This sends a real, short probe to the selected CLI. Model charges may apply, including host context. It does not run project work. Check the command before starting.", "测试会通过所选 CLI 发起一次真实的短探针请求，可能产生模型费用（包括宿主附带的上下文）。探针不执行项目任务。开始前请检查调用方案。", "測試會透過所選 CLI 發起一次真實的短探針請求，可能產生模型費用（包括宿主附帶的上下文）。探針不執行專案任務。開始前請檢查呼叫方案。", "選択した CLI に短い実リクエストを送信します。ホストの文脈を含めモデル料金が発生する場合があります。プロジェクト作業は行いません。実行前に設定を確認してください。", "선택한 CLI로 실제 짧은 테스트 요청을 보냅니다. 호스트 문맥을 포함한 모델 비용이 발생할 수 있습니다. 프로젝트 작업은 수행하지 않습니다. 시작 전에 호출 설정을 확인하세요.", "Se envía una prueba real y breve. Puede generar cargos, incluido el contexto del entorno. No ejecuta trabajo del proyecto. Revisa el método antes de empezar.");
        Add("Demo mode does not run external CLI calls. Use a normal workspace to test a real Agent.", "演示模式不执行外部 CLI。请使用正式工作区测试真实 Agent。", "示範模式不執行外部 CLI。請使用正式工作區測試真實 Agent。", "デモでは外部 CLI を実行しません。通常のワークスペースでテストしてください。", "데모에서는 외부 CLI를 실행하지 않습니다. 일반 작업 공간에서 실제 Agent를 테스트하세요.", "La demo no ejecuta CLI externas. Prueba un agente real en un espacio normal.");
        Add("New-session verification enables delivery to a new visible session. Resume verification tests the same probe session; it does not bind future work to that probe session.", "新会话验证通过后，可向新的可见会话交接。续接测试只验证同一探针会话，不会把后续任务绑定到探针会话。", "新工作階段驗證通過後，可向新的可見工作階段交接。續接測試只驗證同一探針工作階段，不會將後續任務綁定到探針工作階段。", "新規作成の確認後は新しい表示可能なセッションに送信できます。再開テストは同じテスト用セッションを確認するだけで、今後の作業には紐づけません。", "새 세션 검증 후 새로운 표시 가능한 세션으로 전달할 수 있습니다. 재개 테스트는 동일한 테스트 세션만 확인하며 이후 작업을 해당 세션에 연결하지 않습니다.", "Verificar la creación permite enviar a una nueva sesión visible. La prueba de reanudación usa la misma sesión de prueba sin vincularle trabajo futuro.");
        Add("Import completed reply", "导入已完成回稿", "匯入已完成回稿", "完了した返信を取り込む", "완료된 응답 가져오기", "Importar respuesta terminada");
        Add("Confirm that the recipient finished this handoff and this file is its reply.", "请确认接收方已完成此次交接，此文件是它的回稿。", "請確認接收方已完成此次交接，此檔案是它的回稿。", "受信側が完了し、このファイルがその返信であることを確認してください。", "수신자가 전달 작업을 완료했고 이 파일이 해당 응답인지 확인하세요.", "Confirma que el destinatario terminó y que este archivo es su respuesta.");
        Add("TaskBaton stores collaboration messages, requests and temporary replies. Keep final deliverables and long-term references in your project folder; relay files expire under the retention policy.",
            "TaskBaton 用于中转 Agent 之间的协作消息、请求与临时回稿，并展示交接进度。请将最终成果及需要长期引用的文件保存在项目目录中；中转站文件会按保留策略清理。",
            "TaskBaton 用於中轉 Agent 之間的協作訊息、請求與暫存回稿，並顯示交接進度。請將最終成果及需要長期引用的檔案保存在專案目錄中；中轉站檔案會依保留策略清理。",
            "TaskBaton は Agent 間のメッセージ・依頼・一時的な返信を中継し、進捗を表示します。最終成果と長期保存するファイルはプロジェクトフォルダーに保存してください。中継ファイルは保持期間に従い削除されます。",
            "TaskBaton는 Agent 간 메시지, 요청 및 임시 응답을 전달하고 진행 상황을 표시합니다. 최종 결과와 장기 보관 파일은 프로젝트 폴더에 저장하세요. 중계 파일은 보관 정책에 따라 정리됩니다.",
            "TaskBaton transmite mensajes, solicitudes y respuestas temporales entre agentes y muestra su progreso. Guarda los resultados finales y las referencias duraderas en el proyecto; los archivos de intercambio caducan según la política de retención.");
        Add("Add at least two Agents first.", "请先添加至少两个 Agent。", "請先新增至少兩個 Agent。", "先に 2 つ以上の Agent を追加してください。", "먼저 Agent를 2개 이상 추가하세요.", "Añade al menos dos agentes primero.");
        Add("Open relay folder", "打开中转站文件夹", "開啟中轉站資料夾", "中継フォルダーを開く", "중계 폴더 열기", "Abrir carpeta de intercambio");
        Add("Your Agents", "你的 Agent", "你的 Agent", "登録した Agent", "내 Agent", "Tus agentes");
        Add("Choose your own Agent names. Relay only needs to know who sends and who receives.", "Agent 名称由你决定；Relay 只需知道谁发送、谁接收。", "Agent 名稱由你決定；Relay 只需知道誰傳送、誰接收。", "Agent の名前は自由に決められます。Relay に必要なのは送信元と送信先です。", "Agent 이름은 자유롭게 정하세요. Relay에는 발신자와 수신자만 필요합니다.", "Tú eliges los nombres. Relay solo necesita saber quién envía y quién recibe.");
        Add("Name", "名称", "名稱", "名前", "이름", "Nombre");
        Add("CLI capability", "CLI 能力", "CLI 能力", "CLI 機能", "CLI 기능", "Capacidad CLI");
        Add("Select an Agent.", "请选择一个 Agent。", "請選擇一個 Agent。", "Agent を選んでください。", "Agent를 선택하세요.", "Selecciona un agente.");
        Add("Remove Agent", "移除 Agent", "移除 Agent", "Agent を削除", "Agent 제거", "Quitar agente");
        Add("Remove this Agent from Relay? Existing task history is kept.", "从 Relay 移除此 Agent？已有交接记录会保留。", "從 Relay 移除此 Agent？現有交接記錄會保留。", "この Agent を Relay から削除しますか？既存の履歴は残ります。", "Relay에서 이 Agent를 제거할까요? 기존 기록은 유지됩니다.", "¿Quitar este agente de Relay? El historial existente se conservará.");
        Add("Test CLI", "测试 CLI", "測試 CLI", "CLI をテスト", "CLI 테스트", "Probar CLI");
        Add("New session and resume verified", "新建与续接已验证", "新增與續接已驗證", "新規作成と再開を確認済み", "새 세션과 재개 확인됨", "Creación y reanudación verificadas");
        Add("New session verified", "新建会话已验证", "新增工作階段已驗證", "新規作成を確認済み", "새 세션 확인됨", "Creación de sesión verificada");
        Add("Manual handoff", "手动交接", "手動交接", "手動で引き継ぎ", "수동 전달", "Envío manual");
        Add("Add or edit an Agent to copy one setup prompt and parse its reply. CLI testing is optional; manual handoff is always available.", "添加或编辑 Agent 时，可复制配置提示词并解析回复。CLI 测试为可选步骤，随时可以手动交接。", "新增或編輯 Agent 時，可複製設定提示詞並解析回覆。CLI 測試為選用步驟，隨時可以手動交接。", "Agent の追加・編集画面で設定プロンプトのコピーと返信の解析ができます。CLI テストは任意です。手動での引き継ぎはいつでも使えます。", "Agent 추가·편집에서 설정 프롬프트를 복사하고 응답을 분석할 수 있습니다. CLI 테스트는 선택 사항이며 수동 전달은 언제든 가능합니다.", "Al añadir o editar un agente puedes copiar el prompt de configuración y analizar su respuesta. Probar CLI es opcional; el envío manual siempre está disponible.");
        Add("Directed connections", "Agent 之间的连接", "Agent 之間的連線", "Agent 間の接続", "Agent 간 연결", "Conexiones entre agentes");
        Add("Green: available · Gray dashed: no calling recipe", "绿色：可用 · 灰色虚线：缺少调用方案", "綠色：可用 · 灰色虛線：缺少呼叫方案", "緑：利用可能 · 灰色の破線：呼び出し方法なし", "초록: 사용 가능 · 회색 점선: 호출 방법 없음", "Verde: disponible · Gris discontinuo: sin método de llamada");
        Add("A → B means the recipient has a calling recipe. The graph is read-only: Relay chooses automatic delivery from the recipient's verified bridge, and manual copy always works.",
            "A → B 表示接收方有调用方案。连接图只读：自动投递取决于接收方已验证的桥接，手动复制始终可用。",
            "A → B 表示接收方有呼叫方案。連線圖唯讀：自動投遞取決於接收方已驗證的橋接，手動複製始終可用。",
            "A → B は受信側に呼び出し方法があることを示します。図は読み取り専用で、自動送信は受信側の検証済みブリッジに従い、手動コピーは常に可能です。",
            "A → B는 수신자에게 호출 방법이 있음을 뜻합니다. 그래프는 읽기 전용이며 자동 전송은 수신자의 검증된 브리지에 따르고 수동 복사는 항상 가능합니다.",
            "A → B indica que el receptor tiene un método de llamada. El gráfico es de solo lectura: el envío automático depende del puente verificado del receptor y la copia manual siempre funciona.");
        Add("No calling recipe. Copy a handoff prompt, or try an advanced CLI extension.", "尚无调用方案。可手动复制交接提示词，或在高级设置中尝试 CLI 接口扩充。", "尚無呼叫方案。可手動複製交接提示詞，或在進階設定中嘗試 CLI 介面擴充。", "呼び出し方法がありません。プロンプトを手動コピーするか、詳細設定で CLI 拡張を試してください。", "호출 방법이 없습니다. 전달 프롬프트를 수동 복사하거나 고급 CLI 확장을 시도하세요.", "No hay un método de llamada. Copia el prompt manualmente o prueba una extensión CLI avanzada.");
        Add("Automatic notification is enabled for this direction.", "此方向已启用自动通知。", "此方向已啟用自動通知。", "この方向の自動通知は有効です。", "이 방향의 자동 알림이 활성화되어 있습니다.", "El aviso automático está activo en esta dirección.");
        Add("A calling recipe is available; this direction is paused or not enabled.", "已有调用方案，此方向已暂停或尚未启用。", "已有呼叫方案，此方向已暫停或尚未啟用。", "呼び出し方法はありますが、この方向は一時停止または未有効化です。", "호출 방법이 있지만 이 방향은 일시 중지되었거나 비활성 상태입니다.", "Existe un método de llamada; esta dirección está pausada o aún no activada.");
        Add("Pause connection", "暂停此方向", "暫停此方向", "この方向を一時停止", "이 방향 일시 중지", "Pausar esta dirección");
        Add("Enable connection", "启用此方向", "啟用此方向", "この方向を有効にする", "이 방향 활성화", "Activar esta dirección");
        Add("CLI interface extensions", "CLI 接口扩充", "CLI 介面擴充", "CLI インターフェース拡張", "CLI 인터페이스 확장", "Extensiones de interfaz CLI");
        Add("Route", "交接双方", "交接雙方", "送信元と送信先", "전달 대상", "Participantes");
        Add("Copy prompt for {0}", "复制给 {0} 的提示词", "複製給 {0} 的提示詞", "{0} 向けプロンプトをコピー", "{0}용 프롬프트 복사", "Copiar prompt para {0}");
        Add("Open task folder", "打开交接文件夹", "開啟交接資料夾", "引き継ぎフォルダーを開く", "전달 폴더 열기", "Abrir carpeta del envío");
        Add("File expired or missing.", "文件已过期或不存在。", "檔案已過期或不存在。", "ファイルが期限切れか見つかりません。", "파일이 만료되었거나 없습니다.", "El archivo ha caducado o no existe.");
        Add("Reply", "回复", "回覆", "返信", "응답", "Respuesta");
        Add("Cancel this handoff? This does not stop an already running Agent.", "取消此次交接？已运行的 Agent 不会因此停止。", "取消此次交接？已執行的 Agent 不會因此停止。", "この引き継ぎを取り消しますか？実行中の Agent は停止しません。", "이 전달을 취소할까요? 이미 실행 중인 Agent는 중지되지 않습니다.", "¿Cancelar este envío? No se detendrá un agente que ya esté ejecutándose.");
        Add("Retry may repeat a delivery or model call. Continue?", "重试可能重复投递或模型调用，是否继续？", "重試可能重複投遞或模型呼叫，是否繼續？", "再試行により送信やモデル呼び出しが重複する可能性があります。続行しますか？", "재시도로 전달 또는 모델 호출이 반복될 수 있습니다. 계속할까요?", "El reintento puede repetir un envío o una llamada al modelo. ¿Continuar?");
        Add("Copy diagnostics", "复制诊断信息", "複製診斷資訊", "診断情報をコピー", "진단 정보 복사", "Copiar diagnóstico");
        Add("Processing history", "处理记录", "處理記錄", "処理履歴", "처리 기록", "Historial de procesamiento");
        Add("No handoffs yet. Add two Agents, then create your first handoff.", "还没有交接。添加两个 Agent 后，创建首次交接。", "尚無交接。新增兩個 Agent 後，建立首次交接。", "まだ引き継ぎはありません。Agent を 2 つ追加して最初の引き継ぎを作成してください。", "아직 전달이 없습니다. Agent 2개를 추가한 후 첫 전달을 만드세요.", "Todavía no hay envíos. Añade dos agentes y crea el primero.");
        Add("Relay storage", "中转站", "中轉站", "中継ストレージ", "중계 저장소", "Almacenamiento de intercambio");
        Add("This folder holds temporary handoff files and the local ledger.", "此文件夹用于保存临时交接文件和本机记录。", "此資料夾用於保存暫存交接檔案與本機記錄。", "このフォルダーには一時的な引き継ぎファイルとローカル記録が保存されます。", "이 폴더에는 임시 전달 파일과 로컬 기록이 저장됩니다.", "Esta carpeta contiene los archivos temporales y el registro local.");
        Add("Change relay folder", "更换中转站文件夹", "變更中轉站資料夾", "中継フォルダーを変更", "중계 폴더 변경", "Cambiar carpeta de intercambio");
        Add("Choose relay folder", "选择中转站文件夹", "選擇中轉站資料夾", "中継フォルダーを選択", "중계 폴더 선택", "Elegir carpeta de intercambio");
        Add("Check migration", "迁移校验", "移轉檢查", "移行を確認", "이전 확인", "Comprobar traslado");
        Add("Advanced task settings", "高级交接设置", "進階交接設定", "引き継ぎの詳細設定", "고급 전달 설정", "Configuración avanzada de envíos");
        Add("Clean expired files", "清理过期文件", "清理過期檔案", "期限切れファイルを削除", "만료 파일 정리", "Limpiar archivos caducados");
        Add("Delete {0} expired relay files?", "清理 {0} 个过期中转文件？", "清理 {0} 個過期中轉檔案？", "期限切れの中継ファイル {0} 件を削除しますか？", "만료된 중계 파일 {0}개를 삭제할까요?", "¿Eliminar {0} archivos de intercambio caducados?");
        Add("Notify when Relay processes a completed handoff. Closing this app does not start a background monitor.", "Relay 处理已完成的交接时发送通知。关闭本软件后，不会另行启动后台监控。", "Relay 處理已完成的交接時傳送通知。關閉本軟體後，不會另行啟動背景監控。", "Relay が完了した引き継ぎを処理すると通知します。アプリを閉じてもバックグラウンド監視は起動しません。", "Relay가 완료된 전달을 처리할 때 알립니다. 앱을 닫아도 백그라운드 모니터가 시작되지 않습니다.", "Notifica cuando Relay procesa un envío completado. Cerrar la aplicación no inicia un monitor en segundo plano.");
        Add("CLI extensions connect verified command-line tools or bridges. Availability depends on the target software, version, environment and session support. Not every Agent supports automatic calls or resuming a session. Test before enabling; manual handoff remains available.",
            "CLI 接口扩充用于接入经验证的第三方命令行或桥接方案。可用性取决于目标软件开放的接口、版本、运行环境及会话管理能力，并非所有 Agent 均支持自动调用或原会话续接。扩充方案须完成检查与测试后方可启用；未接通时仍可使用手动交接。",
            "CLI 介面擴充用於接入經驗證的第三方命令列或橋接方案。可用性取決於目標軟體開放的介面、版本、執行環境及工作階段管理能力，並非所有 Agent 均支援自動呼叫或原工作階段續接。擴充方案須完成檢查與測試後方可啟用；未接通時仍可使用手動交接。",
            "CLI 拡張は検証済みのコマンドラインツールやブリッジに接続します。可用性は対象ソフトウェアのインターフェース、バージョン、環境、セッション対応に依存します。自動呼び出しや既存セッションの再開に対応しない Agent もあります。有効化する前にテストしてください。手動での引き継ぎも利用できます。",
            "CLI 확장은 검증된 명령줄 도구나 브리지에 연결합니다. 사용 가능 여부는 대상 소프트웨어의 인터페이스, 버전, 환경 및 세션 기능에 따라 달라집니다. 모든 Agent가 자동 호출이나 기존 세션 재개를 지원하지는 않습니다. 확인과 테스트 후 활성화하며 연결되지 않아도 수동 전달은 가능합니다.",
            "Las extensiones CLI conectan herramientas de línea de comandos o puentes verificados. Su disponibilidad depende del software, las interfaces, la versión, el entorno y las sesiones. No todos los agentes admiten llamadas automáticas o reanudar sesiones. Comprueba y prueba antes de activar; el envío manual sigue disponible.");
        Add("Copy extension prompt", "复制接口扩充提示词", "複製介面擴充提示詞", "拡張プロンプトをコピー", "확장 프롬프트 복사", "Copiar prompt de extensión");
        Add("Import calling recipe", "解析并保存调用方案", "解析並儲存呼叫方案", "呼び出し方法を解析して保存", "호출 방법 분석 및 저장", "Analizar y guardar método");
        Add("Import saves a recipe without running it. Review the executable and arguments in the test dialog before making a real call.", "导入仅保存方案。实际调用前，请在测试窗口检查程序路径和参数。", "匯入僅儲存方案。實際呼叫前，請在測試視窗檢查程式路徑與參數。", "インポートでは方法を保存するだけです。実際に呼び出す前にテスト画面で実行ファイルと引数を確認してください。", "가져오기는 방법만 저장합니다. 실제 호출 전에 테스트 창에서 실행 파일과 인수를 확인하세요.", "La importación solo guarda el método. Revisa el ejecutable y los argumentos en la ventana de prueba antes de realizar una llamada real.");
        Add("Advanced: CLI interface extensions", "高级：CLI 接口扩充", "進階：CLI 介面擴充", "詳細：CLI インターフェース拡張", "고급: CLI 인터페이스 확장", "Avanzado: extensiones de interfaz CLI");
    }
}
