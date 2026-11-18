namespace TaskBaton.Desktop;

internal static class SessionUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Choose existing conversation","选择已有会话","選擇已有會話","既存の会話を選択","기존 대화 선택","Elegir conversación existente");
        Add("CLI configured · Choose a task conversation","CLI 已配置 · 在任务中选择会话","CLI 已設定 · 在任務中選擇會話","CLI 設定済み・会話を選択","CLI 설정됨 · 작업 대화 선택","CLI configurado · Elige conversación");
        Add("A probe does not choose the conversation for a task. Choose an existing conversation in the handoff; later turns reuse that exact ID.","探针不替任务选择会话。交接时明确选择一次已有会话，后续复用同一准确 ID。","探針不會替任務選會話；明確選擇後，後續重用同一 ID。","テストは作業会話を選びません。選択した ID を再利用します。","테스트는 작업 대화를 선택하지 않습니다. 선택한 ID를 재사용합니다.","La prueba no elige la conversación de trabajo. Se reutiliza el ID elegido.");
        Add("Conversation routing","会话选择","會話選擇","会話の選択","대화 선택","Selección de conversación");
        Add("Conversations","会话","會話","会話","대화","Conversaciones");
        Add("Conversation","会话标题","會話標題","会話","대화 제목","Conversación");
        Add("Project folder","项目目录","專案目錄","作業フォルダー","프로젝트 폴더","Carpeta del proyecto");
        Add("Project folder (optional)","项目目录（选填）","專案目錄（選填）","作業フォルダー（任意）","프로젝트 폴더(선택)","Carpeta (opcional)");
        Add("Last active","最后活跃","最後活動","最終更新","최근 활동","Última actividad");
        Add("Model","模型","模型","モデル","모델","Modelo");
        Add("Visibility","桌面可见性","桌面可見性","表示状態","표시 상태","Visibilidad");
        Add("Search title or project","搜索标题或项目","搜尋標題或專案","タイトル・プロジェクト検索","제목 또는 프로젝트 검색","Buscar título o proyecto");
        Add("Use this conversation","使用此会话","使用此會話","この会話を使用","이 대화 사용","Usar esta conversación");
        Add("New conversation","新建会话","新增會話","新しい会話","새 대화","Nueva conversación");
        Add("Use existing conversation","使用已有会话","使用已有會話","既存の会話を使用","기존 대화 사용","Usar conversación existente");
        Add("Change conversation","更换会话","更換會話","会話を変更","대화 변경","Cambiar conversación");
        Add("Choose source conversation","选择来源会话","選擇來源會話","元の会話を選択","원본 대화 선택","Elegir conversación de origen");
        Add("Enter source Session ID","手动填写来源会话 ID","手動填寫來源會話 ID","元の会話 ID を入力","원본 세션 ID 입력","Introducir ID de origen");
        Add("Enter Session ID manually","手动输入 Session ID","手動輸入 Session ID","セッション ID を手動入力","세션 ID 직접 입력","Introducir ID manualmente");
        Add("Use Session ID","使用此 ID","使用此 ID","この ID を使用","이 ID 사용","Usar este ID");
        Add("Select a conversation first.","请先明确选择一个会话。","請先明確選擇一個會話。","会話を選択してください。","대화를 선택하세요.","Selecciona una conversación.");
        Add("Enter an exact Session ID.","请填写准确的会话 ID。","請填寫準確的會話 ID。","正確な会話 ID を入力。","정확한 세션 ID를 입력하세요.","Introduce el ID exacto.");
        Add("Use an absolute project path.","项目目录请填写完整路径。","專案目錄請填寫完整路徑。","絶対パスを入力。","절대 경로를 입력하세요.","Usa una ruta absoluta.");
        Add("Refreshing conversations…","正在刷新会话…","正在重新整理會話…","会話を更新中…","대화 새로 고침 중…","Actualizando conversaciones…");
        Add("Showing cached conversations; refreshing…","已显示缓存会话，正在刷新…","已顯示快取會話，正在更新…","キャッシュを表示し更新中…","캐시 표시 및 새로 고침 중…","Mostrando caché; actualizando…");
        Add("Source conversation is not identified.","来源会话尚未识别。","來源會話尚未識別。","元の会話は未識別です。","원본 대화가 식별되지 않았습니다.","Conversación de origen sin identificar.");
        Add("Only this Agent is scanned. Sorting never selects a conversation for you.","只查询当前 Agent 的会话；排序仅供查找，不会自动替你选择。","只查詢目前 Agent 的會話；排序不會代替你選擇。","この Agent のみ照会します。自動選択しません。","현재 Agent만 조회하며 자동 선택하지 않습니다.","Solo se consulta este agente; nunca se elige automáticamente.");
        Add("Choose a conversation explicitly. No conversation is selected automatically.","请明确选择会话，软件不会自动选中任何一项。","請明確選擇會話，軟體不會自動選取。","会話を明示的に選択してください。","대화를 명시적으로 선택하세요.","Elige una conversación explícitamente.");
        Add("Session discovery is unavailable. You can enter an exact Session ID.","此节点尚不支持会话发现，可手动输入准确 ID；自动续接仍需核实会话存在。","此節點尚不支援會話探索，可手動輸入準確 ID。","会話探索は利用不可。正確な ID を入力できます。","대화 검색 불가. 정확한 ID를 입력할 수 있습니다.","Descubrimiento no disponible; introduce el ID exacto.");
        Add("Conversation refresh failed. Check the configured storage folder.","刷新会话失败，请检查配置的会话数据目录。","更新會話失敗，請檢查資料目錄。","会話の更新失敗。保存先を確認。","새로 고침 실패. 저장 폴더를 확인하세요.","No se pudo actualizar. Revisa la carpeta de datos.");
        Add("Manual IDs are checked before CLI resume. Unsupported discovery does not prove that a session exists.","执行续接前会核实手动填写的 ID；无法查询不代表会话存在。","續接前會核實手動 ID；無法查詢不代表存在。","再開前に ID を検証します。未対応は存在確認ではありません。","재개 전 ID를 확인합니다. 검색 불가는 존재 확인이 아닙니다.","El ID se comprueba antes de reanudar; sin descubrimiento no se confirma su existencia.");
        Add("A pasted handoff does not identify its source conversation. Choose it once if replies should resume it.","粘贴的交接不能证明来自哪段会话。需要自动回传续接时，请在这里选择一次来源会话。","貼上的交接無法識別來源；需要自動回傳續接時，請選擇一次來源會話。","貼り付けだけでは元の会話を識別できません。返信先を一度選択。","붙여넣기만으로 원본 대화를 알 수 없습니다. 회신할 대화를 선택하세요.","El texto pegado no identifica el origen. Elígelo una vez para reanudar las respuestas.");
        Add("Choose an existing conversation once. Later turns reuse that exact ID.","已有会话只需选择一次，后续往返复用同一个准确 ID。","已有會話只需選擇一次，後續重用同一 ID。","一度選択すると同じ ID を再利用。","한 번 선택하면 같은 ID를 재사용합니다.","Elige una vez; los siguientes turnos reutilizan ese ID.");
        Add("Resume inherits the conversation's model and settings. Missing sessions require a new explicit choice; Relay never chooses the latest conversation.","续接继承原会话的模型及设置。会话失效时需要重新选择，Relay 不会猜测最近会话。","續接繼承原模型及設定；失效時需重新選擇，不猜測最近會話。","再開は元のモデルと設定を継承。無効時は再選択します。","재개 시 원래 모델과 설정을 유지하며 최신 대화를 추측하지 않습니다.","Reanudar conserva el modelo y la configuración; nunca se adivina la última conversación.");
        Add("Create a new conversation on the next CLI delivery? The previous conversation is kept.","下次 CLI 投递要新建会话吗？原会话会保留。","下次 CLI 投遞要新增會話嗎？原會話會保留。","次回配信で新規会話を作成しますか？","다음 전달 시 새 대화를 만들까요? 기존 대화는 유지됩니다.","¿Crear una conversación en la próxima entrega? La anterior se conserva.");
        Add("Save session discovery","保存会话发现设置","儲存會話探索設定","会話探索を保存","대화 검색 설정 저장","Guardar descubrimiento");
        Add("Select a session provider.","请选择会话提供器。","請選擇會話提供器。","提供元を選択。","제공자를 선택하세요.","Selecciona un proveedor.");
        Add("Session discovery reads this Agent's local conversation store. It never selects the latest conversation.","会话发现只读当前 Agent 的会话存储，不会自动选择最近会话。","會話探索唯讀目前 Agent 的儲存，不自動選擇最近會話。","この Agent の会話を読み取り専用で探索します。","현재 Agent의 대화 저장소만 읽기 전용으로 조회합니다.","El descubrimiento lee solo el almacén de este agente sin elegir la última conversación.");
    }
}
