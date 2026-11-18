namespace TaskBaton.Desktop;

public static class EntryText
{
    private static readonly Dictionary<string, string[]> Text = new(StringComparer.Ordinal)
    {
        ["Add Agent"] = ["添加 Agent", "新增 Agent", "Agent を追加", "Agent 추가", "Añadir agente"],
        ["Edit Agent"] = ["编辑 Agent", "編輯 Agent", "Agent を編集", "Agent 편집", "Editar agente"],
        ["Agent name"] = ["Agent 名称", "Agent 名稱", "Agent 名", "Agent 이름", "Nombre del agente"],
        ["Choose any name. Roles are defined by you."] = ["名称由你决定，软件不设定 Agent 的角色。", "名稱由你決定，軟體不設定 Agent 的角色。", "名前と役割はユーザーが自由に決められます。", "이름과 역할은 사용자가 직접 정합니다.", "Tú eliges el nombre y el rol del agente."],
        ["Optional host details"] = ["宿主信息（选填）", "宿主資訊（選填）", "ホスト情報（任意）", "호스트 정보 (선택)", "Datos del entorno (opcionales)"],
        ["Host / application"] = ["宿主 / 软件名称", "宿主 / 軟體名稱", "ホスト / アプリ名", "호스트 / 앱 이름", "Entorno / aplicación"],
        ["Advanced connection details"] = ["高级连接信息", "進階連線資訊", "接続の詳細設定", "고급 연결 정보", "Detalles avanzados de conexión"],
        ["Manual handoff works without configuration paths. CLI capabilities are tested separately."] = ["手动交接无需填写配置路径。CLI 能力需要单独测试。", "手動交接無須填寫設定路徑。CLI 能力需要個別測試。", "手動での引き継ぎには設定パスは不要です。CLI 機能は別途テストします。", "수동 전달에는 설정 경로가 필요하지 않습니다. CLI 기능은 별도로 테스트합니다.", "El envío manual no requiere rutas. Las capacidades CLI se prueban por separado."],
        ["Configuration path"] = ["配置文件路径", "設定檔路徑", "設定ファイルのパス", "설정 파일 경로", "Ruta de configuración"],
        ["CLI executable path"] = ["CLI 程序路径", "CLI 程式路徑", "CLI 実行ファイルのパス", "CLI 실행 파일 경로", "Ruta del ejecutable CLI"],
        ["Host version"] = ["宿主版本", "宿主版本", "ホストのバージョン", "호스트 버전", "Versión del entorno"],
        ["Instruction files (one per line)"] = ["指令文件路径（每行一个）", "指令檔路徑（每行一個）", "指示ファイル（1 行に 1 件）", "지침 파일 (줄당 하나)", "Archivos de instrucciones (uno por línea)"],
        ["Fill with an Agent"] = ["让 Agent 帮你填写", "讓 Agent 幫你填寫", "Agent に入力してもらう", "Agent로 입력하기", "Rellenar con un agente"],
        ["Copy the prompt, send it to your Agent, then paste its complete reply here."] = ["复制提示词发给要登记的 Agent，将它的完整回复粘贴到下方，再解析。", "複製提示詞傳給要登記的 Agent，將它的完整回覆貼到下方，再解析。", "プロンプトを対象の Agent に送り、返信全文をここに貼り付けて解析します。", "프롬프트를 등록할 Agent에게 보내고 전체 응답을 아래에 붙여 넣어 분석하세요.", "Envía el prompt al agente que quieras registrar y pega su respuesta completa para analizarla."],
        ["Copy prompt"] = ["复制提示词", "複製提示詞", "プロンプトをコピー", "프롬프트 복사", "Copiar prompt"],
        ["Paste reply"] = ["粘贴回复", "貼上回覆", "返信を貼り付け", "응답 붙여넣기", "Pegar respuesta"],
        ["Parse and fill"] = ["解析并填写", "解析並填寫", "解析して入力", "분석 및 입력", "Analizar y rellenar"],
        ["Reply parsed. Review the filled fields before saving."] = ["解析完成，请检查填写结果后保存。", "解析完成，請檢查填寫結果後儲存。", "解析しました。入力内容を確認してから保存してください。", "분석했습니다. 입력된 내용을 확인한 후 저장하세요.", "Respuesta analizada. Revisa los campos antes de guardar."],
        ["Prompt copied."] = ["提示词已复制。", "提示詞已複製。", "プロンプトをコピーしました。", "프롬프트를 복사했습니다.", "Prompt copiado."],
        ["Enter an Agent name."] = ["请填写 Agent 名称。", "請填寫 Agent 名稱。", "Agent 名を入力してください。", "Agent 이름을 입력하세요.", "Introduce un nombre de agente."],
        ["The reply could not be parsed. Check the requested JSON format."] = ["无法解析回复，请检查是否为提示词要求的完整 JSON。", "無法解析回覆，請檢查是否為提示詞要求的完整 JSON。", "返信を解析できません。指定の JSON 形式を確認してください。", "응답을 분석할 수 없습니다. 요청한 JSON 형식을 확인하세요.", "No se pudo analizar la respuesta. Comprueba el formato JSON solicitado."],
        ["Clipboard is unavailable. Try again."] = ["暂时无法访问剪贴板，请重试。", "暫時無法存取剪貼簿，請重試。", "クリップボードを使用できません。再試行してください。", "클립보드를 사용할 수 없습니다. 다시 시도하세요.", "El portapapeles no está disponible. Inténtalo de nuevo."],
        ["New handoff"] = ["新建交接", "新增交接", "引き継ぎを作成", "새 전달", "Nuevo envío"],
        ["Sender"] = ["发送方", "傳送方", "送信元", "보내는 Agent", "Remitente"],
        ["Recipient"] = ["接收方", "接收方", "送信先", "받는 Agent", "Destinatario"],
        ["Message"] = ["交接内容", "交接內容", "引き継ぎ内容", "전달 내용", "Mensaje"],
        ["Delivery"] = ["交接方式", "交接方式", "送信方法", "전달 방법", "Método de envío"],
        ["Manual copy"] = ["手动复制提示词", "手動複製提示詞", "手動コピー", "수동 복사", "Copia manual"],
        ["Automatic CLI"] = ["自动通过 CLI 通知", "自動透過 CLI 通知", "CLI で自動通知", "CLI 자동 알림", "Aviso automático por CLI"],
        ["Automatic delivery is unavailable or paused. You can still hand off manually."] = ["此方向缺少调用方案；仍可手动交接。", "此方向缺少呼叫方案；仍可手動交接。", "この方向には呼び出し方法がありません。手動で引き継げます。", "이 방향에는 호출 설정이 없습니다. 수동 전달은 가능합니다.", "Esta dirección no tiene método de llamada; puedes entregar a mano."],
        ["Generate with an Agent"] = ["让 Agent 生成交接内容", "讓 Agent 產生交接內容", "Agent に内容を作成してもらう", "Agent로 전달 내용 생성", "Generar con un agente"],
        ["Copy this prompt to any Agent, describe the handoff, then parse its reply. Parsing does not send."] = ["将提示词发给任意 Agent，告诉它谁给谁、交接什么，再把完整回复粘贴解析。解析不会发送。", "將提示詞傳給任意 Agent，告訴它誰給誰、交接什麼，再把完整回覆貼上解析。解析不會傳送。", "任意の Agent に送信元・送信先と内容を伝え、返信全文を解析します。解析では送信しません。", "아무 Agent에게 프롬프트를 보내고 발신자, 수신자와 내용을 알려주세요. 전체 응답을 분석해도 전송되지 않습니다.", "Envía el prompt a cualquier agente e indica quién envía, quién recibe y el contenido. Analizar la respuesta no envía el mensaje."],
        ["Create handoff"] = ["创建交接", "建立交接", "引き継ぎを作成", "전달 만들기", "Crear envío"],
        ["Choose registered sender and recipient nodes."] = ["请选择已登记的发送方和接收方。", "請選擇已登記的傳送方與接收方。", "登録済みの送信元と送信先を選んでください。", "등록된 발신자와 수신자를 선택하세요.", "Elige remitente y destinatario registrados."],
        ["Sender and recipient must be different nodes."] = ["发送方和接收方不能是同一个 Agent。", "傳送方與接收方不能是同一個 Agent。", "送信元と送信先は別の Agent にしてください。", "발신자와 수신자는 서로 달라야 합니다.", "El remitente y el destinatario deben ser distintos."],
        ["Enter the handoff message."] = ["请填写交接内容。", "請填寫交接內容。", "引き継ぎ内容を入力してください。", "전달 내용을 입력하세요.", "Introduce el mensaje."],
        ["The reply requires From, To and Content."] = ["回复必须包含 From、To 和 Content。", "回覆必須包含 From、To 和 Content。", "返信には From、To、Content が必要です。", "응답에는 From, To, Content가 필요합니다.", "La respuesta debe incluir From, To y Content."],
        ["Response parsed. Review the sender, recipient and message before creating."] = ["解析完成，请检查双方和内容，再创建交接。", "解析完成，請檢查雙方與內容，再建立交接。", "解析しました。送信元・送信先と内容を確認して作成してください。", "분석했습니다. 발신자, 수신자와 내용을 확인한 후 생성하세요.", "Respuesta analizada. Revisa los participantes y el mensaje antes de crear el envío."],
        ["Use a full path for optional configuration and executable files."] = ["选填的配置、程序及指令文件请使用完整路径。", "選填的設定、程式與指令檔請使用完整路徑。", "任意の設定・実行・指示ファイルには絶対パスを使用してください。", "선택적 설정, 실행 및 지침 파일에는 전체 경로를 사용하세요.", "Usa rutas absolutas para los archivos opcionales de configuración, ejecutables e instrucciones."],
        ["Name is required; all other information is optional."] = ["只需填写名称，其余信息均可选填。", "只需填寫名稱，其餘資訊均可選填。", "名前のみ必須です。その他の情報は任意です。", "이름만 필수이며 다른 정보는 선택 사항입니다.", "Solo el nombre es obligatorio; los demás datos son opcionales."]
    };

    public static string Get(string source, string language)
        => TryGet(source, language, out var text) ? text : UiText.Translate(source, language);

    public static bool TryGet(string source, string language, out string text)
    {
        var index = language switch { "zh-CN" => 0, "zh-TW" => 1, "ja" => 2, "ko" => 3, "es" => 4, _ => -1 };
        if (Text.TryGetValue(source, out var variants))
        {
            text = index >= 0 ? variants[index] : source;
            return true;
        }
        text = source;
        return false;
    }
}
