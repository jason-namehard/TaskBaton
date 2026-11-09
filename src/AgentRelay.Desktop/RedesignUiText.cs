namespace AgentRelay.Desktop;

/// <summary>Labels introduced by the redesigned window: page subtitles, menus, settings groups and themes.</summary>
internal static class RedesignUiText
{
    public static void AddTo(Dictionary<string, string[]> entries)
    {
        void Add(string en, string cn, string tw, string ja, string ko, string es) => entries[en] = [en, cn, tw, ja, ko, es];

        // Shell
        Add("Tasks", "任务", "任務", "タスク", "작업", "Tareas");
        Add("Handoffs between your Agents and their progress", "Agent 之间的交接与进度", "Agent 之間的交接與進度", "Agent 間の引き継ぎと進捗", "Agent 간 인계와 진행 상황", "Traspasos entre tus Agentes y su progreso");
        Add("Registered Agents and how Relay reaches them", "已登记的 Agent 和它们的桥接方式", "已登記的 Agent 與其橋接方式", "登録済みの Agent と接続方法", "등록된 Agent와 연결 방식", "Agentes registrados y cómo los alcanza Relay");
        Add("One line per pair of Agents; each direction is enabled separately", "每对 Agent 一条线，两个方向分别启用", "每對 Agent 一條線，兩個方向分別啟用", "Agent の組ごとに 1 本の線。方向ごとに有効化します", "Agent 쌍마다 선 하나, 방향마다 따로 활성화", "Una línea por pareja de Agentes; cada sentido se activa por separado");
        Add("Appearance, notifications, relay storage and advanced options", "外观、通知、中转站与高级选项", "外觀、通知、中轉站與進階選項", "外観、通知、中継フォルダー、詳細設定", "모양, 알림, 중계 폴더, 고급 옵션", "Apariencia, avisos, almacenamiento y opciones avanzadas");
        Add("Watching · {0} Agents", "正在监听回执 · {0} 个 Agent", "正在監聽回執 · {0} 個 Agent", "監視中 · Agent {0} 件", "대기 중 · Agent {0}개", "Activo · {0} Agentes");
        Add("Desktop notification unavailable", "桌面通知不可用", "桌面通知無法使用", "デスクトップ通知を利用できません", "데스크톱 알림을 사용할 수 없음", "Avisos de escritorio no disponibles");
        Add("{0} tasks need attention", "{0} 个任务需要处理", "{0} 個任務需要處理", "対応が必要なタスク {0} 件", "처리가 필요한 작업 {0}개", "{0} tareas requieren atención");
        Add("Demo", "演示", "示範", "デモ", "데모", "Demo");
        Add("DEMO · 虚构 Agent 与预设结果 · 未调用模型，未读取现用配置", "演示模式：虚构的 Agent 和预设结果，不调用模型，不读取你现用的配置", "示範模式：虛構的 Agent 與預設結果，不呼叫模型，不讀取你現用的設定",
            "デモモード：架空の Agent と用意された結果。モデルは呼び出さず、現在の設定も読みません", "데모 모드: 가상의 Agent와 미리 준비된 결과. 모델을 호출하지 않고 현재 설정도 읽지 않습니다", "Modo demo: Agentes ficticios y resultados preparados. No llama a modelos ni lee tu configuración");

        // Tasks
        Add("New task", "新建任务", "新增任務", "新しいタスク", "새 작업", "Nueva tarea");
        Add("Relay delivers each turn through the Agent bridge", "Relay 通过 Agent 桥接自动发送每一轮", "Relay 透過 Agent 橋接自動傳送每一輪", "Relay が Agent ブリッジ経由で各ターンを届けます", "Relay가 Agent 브리지로 각 차례를 전달합니다", "Relay entrega cada turno por el puente del Agente");
        Add("You copy the prompt into the Agent yourself", "你自己把提示词复制给 Agent", "你自行把提示詞複製給 Agent", "プロンプトを自分で Agent に貼り付けます", "프롬프트를 직접 Agent에 붙여 넣습니다", "Copias tú el prompt en el Agente");
        Add("Search Agents, tasks or conversation IDs", "搜索 Agent、任务或会话 ID", "搜尋 Agent、任務或對話 ID", "Agent、タスク、会話 ID を検索", "Agent, 작업, 대화 ID 검색", "Buscar Agentes, tareas o ID de conversación");
        Add("Select tasks", "选择", "選取", "選択", "선택", "Seleccionar");
        Add("Done", "完成", "完成", "完了", "완료", "Listo");
        Add("All", "全部", "全部", "すべて", "전체", "Todas");
        Add("Needs attention", "需要处理", "需要處理", "要対応", "처리 필요", "Requieren atención");
        Add("In progress", "进行中", "進行中", "進行中", "진행 중", "En curso");
        Add("Completed", "已完成", "已完成", "完了", "완료됨", "Completadas");
        Add("No handoffs yet", "还没有交接", "還沒有交接", "引き継ぎはまだありません", "아직 인계가 없습니다", "Aún no hay traspasos");
        Add("Progress", "进度", "進度", "進捗", "진행 상황", "Progreso");
        Add("Chosen by you", "你选定的", "你選定的", "手動で選択", "직접 선택함", "Elegida por ti");
        Add("Entered manually", "手动填写", "手動輸入", "手入力", "직접 입력", "Introducida a mano");

        // Agents
        Add("Agent", "Agent", "Agent", "Agent", "Agent", "Agente");
        Add("Agent ID", "Agent ID", "Agent ID", "Agent ID", "Agent ID", "ID del Agente");
        Add("Added by hand", "手动添加", "手動新增", "手動で追加", "직접 추가함", "Añadido a mano");
        Add("More ways to add an Agent", "更多添加方式", "更多新增方式", "その他の追加方法", "다른 추가 방법", "Más formas de añadir un Agente");
        Add("Copy a prompt; the Agent fills in its own details", "复制一段提示词，让 Agent 自己填写", "複製一段提示詞，讓 Agent 自行填寫", "プロンプトをコピーし、Agent 自身に入力させます", "프롬프트를 복사해 Agent가 직접 입력하게 합니다", "Copia un prompt; el Agente rellena sus datos");
        Add("Fill in by hand", "手动填写", "手動填寫", "手入力する", "직접 입력", "Rellenar a mano");
        Add("Name, paths and calling method", "名称、路径和调用方式", "名稱、路徑與呼叫方式", "名前、パス、呼び出し方法", "이름, 경로, 호출 방식", "Nombre, rutas y método de llamada");
        Add("Sends one short real call", "会发起一次很短的真实调用", "會發起一次很短的真實呼叫", "短い実際の呼び出しを 1 回送ります", "짧은 실제 호출을 한 번 보냅니다", "Envía una llamada real breve");
        Add("Read-only check, no model call", "只读检查，不调用模型", "唯讀檢查，不呼叫模型", "読み取りのみ。モデルは呼び出しません", "읽기 전용 확인, 모델 호출 없음", "Comprobación de solo lectura, sin llamar al modelo");
        Add("More actions", "更多操作", "更多操作", "その他の操作", "더 많은 작업", "Más acciones");
        Add("No Agents yet. Add one with New Agent, or let an Agent fill in its own details with One-click setup.", "还没有 Agent。点“新建 Agent”添加，或用“一键配置”让 Agent 自己填写。",
            "還沒有 Agent。按「新增 Agent」新增，或用「一鍵設定」讓 Agent 自行填寫。", "Agent はまだありません。「新しい Agent」で追加するか、「ワンクリック設定」で Agent 自身に入力させてください。",
            "아직 Agent가 없습니다. '새 Agent'로 추가하거나 '원클릭 설정'으로 Agent가 직접 입력하게 하세요.", "Aún no hay Agentes. Añade uno con Nuevo Agente o deja que el Agente rellene sus datos con la configuración en un clic.");
        Add("Connection", "连接", "連線", "接続", "연결", "Conexión");
        Add("Without a bridge you copy prompts by hand.", "没有桥接时需要手动复制提示词。", "沒有橋接時需要手動複製提示詞。", "ブリッジがない場合はプロンプトを手動でコピーします。", "브리지가 없으면 프롬프트를 직접 복사합니다.", "Sin puente, copias los prompts a mano.");
        Add("Check bridge setup", "检查桥接配置", "檢查橋接設定", "ブリッジ設定を確認", "브리지 설정 확인", "Comprobar el puente");
        Add("Reads the local configuration. No model is called.", "只读取本机配置，不调用模型。", "只讀取本機設定，不呼叫模型。", "ローカル設定を読むだけで、モデルは呼び出しません。", "로컬 설정만 읽고 모델은 호출하지 않습니다.", "Lee la configuración local. No llama a ningún modelo.");
        Add("Check", "检查", "檢查", "確認", "확인", "Comprobar");
        Add("Copy diagnostic prompt", "复制诊断提示词", "複製診斷提示詞", "診断プロンプトをコピー", "진단 프롬프트 복사", "Copiar prompt de diagnóstico");
        Add("Handoff rule", "交接规则", "交接規則", "引き継ぎルール", "인계 규칙", "Regla de traspaso");
        Add("Instruction file", "指令文件", "指令檔", "指示ファイル", "지시 파일", "Archivo de instrucciones");
        Add("Session discovery", "会话发现", "對話探索", "会話の検出", "대화 찾기", "Descubrimiento de conversaciones");
        Add("Conversation store", "会话存储", "對話儲存", "会話の保存先", "대화 저장소", "Almacén de conversaciones");
        Add("Storage folder", "存储目录", "儲存資料夾", "保存フォルダー", "저장 폴더", "Carpeta de almacenamiento");
        Add("Save", "保存", "儲存", "保存", "저장", "Guardar");
        Add("Default model", "默认模型", "預設模型", "既定のモデル", "기본 모델", "Modelo predeterminado");
        Add("New conversations", "新会话", "新對話", "新しい会話", "새 대화", "Conversaciones nuevas");
        Add("Show models", "查看模型", "查看模型", "モデルを表示", "모델 보기", "Ver modelos");
        Add("Hide models", "收起模型", "收合模型", "モデルを閉じる", "모델 접기", "Ocultar modelos");

        Add("Choose how to add it. If you are not sure, let the Agent set itself up.", "选择添加方式。不确定的话，就让 Agent 自己配置。", "選擇新增方式。不確定的話，就讓 Agent 自行設定。",
            "追加方法を選んでください。迷ったら Agent 自身に設定させましょう。", "추가 방법을 고르세요. 잘 모르겠다면 Agent가 직접 설정하게 하세요.", "Elige cómo añadirlo. Si no estás seguro, deja que el Agente se configure solo.");
        Add("Let the Agent set itself up", "让 Agent 自己配置", "讓 Agent 自行設定", "Agent 自身に設定させる", "Agent가 직접 설정", "Que el Agente se configure solo");
        Add("Relay copies a setup prompt. Paste it into a new conversation with the Agent; it checks how it can be called and registers itself.",
            "Relay 会复制一段配置提示词。把它粘贴到该 Agent 的新会话里，Agent 会自己查清调用方式并完成登记。", "Relay 會複製一段設定提示詞。把它貼到該 Agent 的新對話裡，Agent 會自行查清呼叫方式並完成登記。",
            "Relay が設定用プロンプトをコピーします。Agent の新しい会話に貼り付けると、呼び出し方法を調べて自分で登録します。", "Relay가 설정 프롬프트를 복사합니다. Agent의 새 대화에 붙여 넣으면 호출 방법을 확인하고 스스로 등록합니다.",
            "Relay copia un prompt de configuración. Pégalo en una conversación nueva con el Agente; comprobará cómo se le llama y se registrará solo.");
        Add("Enter the name, configuration path and calling method yourself. Suits users who know how the Agent is set up.",
            "自己填写名称、配置路径和调用方式，适合熟悉该 Agent 配置的用户。", "自行填寫名稱、設定路徑與呼叫方式，適合熟悉該 Agent 設定的使用者。",
            "名前、設定パス、呼び出し方法を自分で入力します。Agent の設定に詳しい方向けです。", "이름, 설정 경로, 호출 방식을 직접 입력합니다. Agent 설정을 잘 아는 사용자에게 맞습니다.",
            "Introduce tú el nombre, la ruta de configuración y el método de llamada. Para quien conoce la configuración del Agente.");
        Add("OK", "确定", "確定", "OK", "확인", "Aceptar");
        Add("Yes", "是", "是", "はい", "예", "Sí");
        Add("No", "否", "否", "いいえ", "아니요", "No");
        Add("Recommended", "推荐", "推薦", "おすすめ", "추천", "Recomendado");

        // Connections
        Add("Set up a bridge for {0}", "为 {0} 配置桥接", "為 {0} 設定橋接", "{0} のブリッジを設定", "{0}의 브리지 설정", "Configurar un puente para {0}");
        Add("Switches only control Relay's automatic delivery. Copying a prompt by hand always works.", "开关只影响 Relay 的自动传递，手动复制提示词始终可用。", "開關只影響 Relay 的自動傳遞，手動複製提示詞始終可用。",
            "スイッチは Relay の自動配信だけを切り替えます。手動でのコピーはいつでも使えます。", "스위치는 Relay 자동 전달만 제어합니다. 프롬프트 직접 복사는 항상 가능합니다.", "Los interruptores solo controlan la entrega automática de Relay. Copiar el prompt a mano siempre funciona.");

        // Settings
        Add("Theme", "主题", "主題", "テーマ", "테마", "Tema");
        Add("Changes apply immediately.", "修改后立即生效。", "修改後立即生效。", "変更はすぐに反映されます。", "변경 사항은 바로 적용됩니다.", "Los cambios se aplican al instante.");
        Add("Minimal", "极简", "極簡", "ミニマル", "미니멀", "Minimalista");
        Add("Warm", "暖色", "暖色", "ウォーム", "따뜻한", "Cálido");
        Add("Fresh", "清新", "清新", "フレッシュ", "산뜻한", "Fresco");
        Add("Night", "夜间", "夜間", "ナイト", "야간", "Noche");
        Add("Follow system", "跟随系统", "跟隨系統", "システムに合わせる", "시스템 설정 따르기", "Según el sistema");
        Add("Accent color", "强调色", "強調色", "アクセントカラー", "강조 색", "Color de acento");
        Add("Follows the theme unless you choose one. Type #RRGGBB for any color.", "默认跟随主题，也可以自选，或输入 #RRGGBB。", "預設跟隨主題，也可以自選，或輸入 #RRGGBB。",
            "既定ではテーマに合わせます。選ぶか #RRGGBB を入力できます。", "기본은 테마를 따릅니다. 직접 고르거나 #RRGGBB를 입력하세요.", "Sigue el tema salvo que elijas uno. Escribe #RRGGBB para cualquier color.");
        Add("Follow the theme", "跟随主题", "跟隨主題", "テーマに合わせる", "테마 따르기", "Según el tema");
        Add("Coral", "珊瑚", "珊瑚", "コーラル", "코랄", "Coral");
        Add("Teal", "青绿", "青綠", "ティール", "청록", "Verde azulado");
        Add("Sky", "天蓝", "天藍", "スカイ", "하늘", "Celeste");
        Add("Amber", "琥珀", "琥珀", "アンバー", "앰버", "Ámbar");
        Add("Demo workspace", "演示工作区", "示範工作區", "デモ用ワークスペース", "데모 작업 공간", "Espacio de demostración");
        Add("Location", "位置", "位置", "場所", "위치", "Ubicación");
        Add("Temporary handoff files and the local ledger live here. Keep final results in your project folder.", "这里保存临时交接文件和本机记录。最终成果请存进项目目录。", "這裡保存暫存交接檔與本機記錄。最終成果請存到專案資料夾。",
            "一時的な引き継ぎファイルとローカル記録の保存先です。最終成果はプロジェクトフォルダーに保存してください。", "임시 인계 파일과 로컬 기록이 저장됩니다. 최종 결과는 프로젝트 폴더에 두세요.", "Aquí se guardan los archivos temporales y el registro local. Guarda los resultados finales en tu proyecto.");
        Add("Keep handoff files", "交接文件保留", "交接檔保留", "引き継ぎファイルの保存期間", "인계 파일 보관", "Conservar archivos de traspaso");
        Add("Review relay files", "查看中转文件", "查看中轉檔案", "中継ファイルを確認", "중계 파일 보기", "Revisar archivos de intercambio");
        Add("Relay files", "中转文件", "中轉檔案", "中継ファイル", "중계 파일", "Archivos de intercambio");
        Add("Review file ownership and task references before cleaning expired Relay files.", "清理过期中转文件前，先查看归属和任务引用。", "清理過期中轉檔案前，先查看歸屬和任務參照。",
            "期限切れの Relay ファイルを削除する前に、所有者とタスクからの参照を確認できます。", "만료된 Relay 파일을 정리하기 전에 소유권과 작업 참조를 확인하세요.", "Revisa la propiedad y las referencias de las tareas antes de limpiar los archivos caducados de Relay.");
        Add("Review Relay copies, user files, and the tasks that reference them. Only expired Relay files with no active reference qualify for cleanup.",
            "查看 Relay 副本、用户文件及引用它们的任务。只有已过期且没有活跃任务引用的 Relay 文件可清理。",
            "查看 Relay 副本、使用者檔案及參照它們的任務。只有已過期且沒有進行中任務參照的 Relay 檔案可清理。",
            "Relay のコピー、ユーザーファイル、参照元タスクを確認できます。期限切れで作業中の参照がない Relay ファイルだけ削除できます。",
            "Relay 사본, 사용자 파일 및 참조하는 작업을 확인하세요. 활성 참조가 없는 만료된 Relay 파일만 정리할 수 있습니다.",
            "Revisa las copias de Relay, los archivos del usuario y las tareas que los usan. Solo se pueden eliminar los archivos caducados de Relay sin referencias activas.");
        Add("File", "文件", "檔案", "ファイル", "파일", "Archivo");
        Add("File ownership", "文件归属", "檔案歸屬", "ファイルの所有者", "파일 소유권", "Propiedad del archivo");
        Add("Cleanup status", "清理状态", "清理狀態", "クリーンアップ状態", "정리 상태", "Estado de limpieza");
        Add("Task references", "任务引用", "任務參照", "タスクからの参照", "작업 참조", "Referencias de tareas");
        Add("File and task references", "文件路径与任务引用", "檔案路徑與任務參照", "ファイルのパスとタスク参照", "파일 경로 및 작업 참조", "Ruta y referencias de tareas");
        Add("Relay managed file", "Relay 受管文件", "Relay 受管檔案", "Relay 管理ファイル", "Relay 관리 파일", "Archivo gestionado por Relay");
        Add("User or project file", "用户或项目文件", "使用者或專案檔案", "ユーザーまたはプロジェクトのファイル", "사용자 또는 프로젝트 파일", "Archivo del usuario o del proyecto");
        Add("Task control file", "任务控制文件", "任務控制檔", "タスク制御ファイル", "작업 제어 파일", "Archivo de control de la tarea");
        Add("Ownership unverified", "归属未确认", "歸屬未確認", "所有者を未確認", "소유권 미확인", "Propiedad sin verificar");
        Add("Eligible for cleanup", "可清理", "可清理", "削除可能", "정리 가능", "Se puede limpiar");
        Add("In use by a task", "任务仍在引用", "任務仍在參照", "タスクが使用中", "작업에서 사용 중", "En uso por una tarea");
        Add("Within retention period", "仍在保留期内", "仍在保留期內", "保存期間内", "보관 기간 내", "Dentro del plazo de conservación");
        Add("Protected file", "保留文件", "保留檔案", "保護されたファイル", "보호된 파일", "Archivo protegido");
        Add("File missing", "文件缺失", "檔案遺失", "ファイルなし", "파일 없음", "Archivo ausente");
        Add("Cleaned up", "已清理", "已清理", "削除済み", "정리 완료", "Eliminado");
        Add("Path unavailable for cleanup", "路径不可清理", "路徑無法清理", "削除できないパス", "정리할 수 없는 경로", "Ruta no apta para la limpieza");
        Add("Retention ends {0}", "保留期至 {0}", "保留期至 {0}", "保存期限 {0}", "보관 기한 {0}", "Conservar hasta {0}");
        Add("Registered task {0}", "登记任务 {0}", "登記任務 {0}", "登録元タスク {0}", "등록 작업 {0}", "Tarea de registro {0}");
        Add("Preview cleanup", "预览清理", "預覽清理", "削除内容を確認", "정리 미리 보기", "Previsualizar limpieza");
        Add("No files qualify for cleanup.", "当前没有可清理的文件。", "目前沒有可清理的檔案。", "削除できるファイルはありません。", "정리할 수 있는 파일이 없습니다.", "No hay archivos que se puedan limpiar.");
        Add("Delete only these {0} reviewed files?", "确认仅删除下列 {0} 个文件？", "確認僅刪除下列 {0} 個檔案？", "確認した {0} 件のファイルだけ削除しますか？", "확인한 파일 {0}개만 삭제할까요?", "¿Eliminar únicamente estos {0} archivos revisados?");
        Add("Removed {0} files. Kept {1}.", "已清理 {0} 个文件，保留 {1} 个。", "已清理 {0} 個檔案，保留 {1} 個。", "{0} 件を削除し、{1} 件を保持しました。", "파일 {0}개를 삭제하고 {1}개를 보관했습니다.", "Se eliminaron {0} archivos y se conservaron {1}.");
        Add("File changed or is now protected.", "文件已变动或现已受到保护。", "檔案已變動或現已受到保護。", "ファイルが変更されたか保護されました。", "파일이 변경되었거나 보호되었습니다.", "El archivo cambió o ahora está protegido.");
        Add("file_reference_request", "任务输入", "任務輸入", "タスク入力", "작업 입력", "Entrada");
        Add("file_reference_result", "任务结果", "任務結果", "タスク結果", "작업 결과", "Resultado");
        Add("file_reference_source", "原始文件", "原始檔案", "元ファイル", "원본 파일", "Archivo original");
        Add("file_reference_current", "当前轮次", "目前輪次", "現在のターン", "현재 회차", "Turno actual");
        Add("file_reference_image", "任务图片", "任務圖片", "タスク画像", "작업 이미지", "Imagen de la tarea");
        Add("file_reference_review", "验收记录", "驗收紀錄", "レビュー記録", "검토 기록", "Registro de revisión");
        Add("file_reference_submission", "Agent 提交", "Agent 提交", "Agent の提出", "Agent 제출", "Entrega del agente");
        Add("file_reference_deliverable", "项目交付物", "專案交付物", "プロジェクト成果物", "프로젝트 결과물", "Entregable del proyecto");
        Add("file_reference_registered", "任务产物", "任務產物", "タスクの成果物", "작업 산출물", "Artefacto de la tarea");
        Add("file_reference_control", "交接控制", "交接控制", "引き継ぎ制御", "인계 제어", "Control del traspaso");
        Add("Advanced", "高级", "進階", "詳細", "고급", "Avanzado");
        Add("Task timeout", "任务超时", "任務逾時", "タスクのタイムアウト", "작업 제한 시간", "Tiempo límite de la tarea");
        Add("How long Relay waits for an Agent to finish one turn.", "Relay 等待 Agent 完成一轮的时间。", "Relay 等待 Agent 完成一輪的時間。", "Agent が 1 ターンを終えるまで Relay が待つ時間です。", "Agent가 한 차례를 마칠 때까지 Relay가 기다리는 시간입니다.", "Cuánto espera Relay a que un Agente termine un turno.");
        Add("minutes", "分钟", "分鐘", "分", "분", "minutos");
        Add("Delivery timeout", "投递超时", "投遞逾時", "配信のタイムアウト", "전달 제한 시간", "Tiempo límite de entrega");
        Add("How long one delivery attempt may take.", "单次投递最多等待的时间。", "單次投遞最多等待的時間。", "1 回の配信にかけられる時間です。", "한 번의 전달에 걸릴 수 있는 시간입니다.", "Cuánto puede durar un intento de entrega.");
        Add("seconds", "秒", "秒", "秒", "초", "segundos");
        Add("Delivery attempts", "投递次数", "投遞次數", "配信の試行回数", "전달 시도 횟수", "Intentos de entrega");
        Add("At most 3.", "最多 3 次。", "最多 3 次。", "最大 3 回です。", "최대 3회입니다.", "Como máximo 3.");
        Add("CLI and ACP bridges are set up and tested for each Agent on the Agents page.", "命令行与 ACP 桥接在 Agent 页为每个 Agent 配置和测试。", "命令列與 ACP 橋接在 Agent 頁為每個 Agent 設定和測試。",
            "CLI と ACP のブリッジは Agent ページで Agent ごとに設定・テストします。", "CLI와 ACP 브리지는 Agent 페이지에서 Agent마다 설정하고 테스트합니다.", "Los puentes CLI y ACP se configuran y prueban para cada Agente en la página Agentes.");
        Add("Go to Agents", "前往 Agent", "前往 Agent", "Agent へ移動", "Agent로 이동", "Ir a Agentes");
        Add("Danger zone", "危险操作", "危險操作", "危険な操作", "위험 구역", "Zona de peligro");
        Add("Removes Relay's marked rules from every registered instruction file. Your other instructions are kept. Do this before deleting the portable software folder.",
            "从所有已登记的指令文件中删除 Relay 标记的规则，你的其他指令会保留。删除便携版软件目录前请先执行。", "從所有已登記的指令檔中刪除 Relay 標記的規則，你的其他指令會保留。刪除可攜版軟體資料夾前請先執行。",
            "登録済みのすべての指示ファイルから Relay のルールを削除します。他の指示は残ります。ポータブル版フォルダーを削除する前に実行してください。",
            "등록된 모든 지시 파일에서 Relay 규칙을 제거합니다. 다른 지시는 유지됩니다. 포터블 폴더를 삭제하기 전에 실행하세요.",
            "Quita las reglas de Relay de todos los archivos de instrucciones registrados. Tus otras instrucciones se conservan. Hazlo antes de borrar la carpeta portátil.");
        Add("Notify me when a handoff finishes", "交接完成时通知我", "交接完成時通知我", "引き継ぎが終わったら通知する", "인계가 끝나면 알림", "Avisarme cuando termine un traspaso");
        Add("Shows a sample in the lower-right corner. No task or model session is created.", "在右下角弹出一条示例，不会创建任务或模型会话。", "在右下角彈出一則範例，不會建立任務或模型對話。",
            "右下に見本を表示します。タスクやモデルの会話は作成しません。", "오른쪽 아래에 예시를 표시합니다. 작업이나 모델 대화는 만들지 않습니다.", "Muestra un ejemplo abajo a la derecha. No crea tareas ni sesiones de modelo.");
    }
}
