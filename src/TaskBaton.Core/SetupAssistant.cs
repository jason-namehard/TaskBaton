using System.Text;
using System.Text.Json;

namespace TaskBaton.Core;

public static class SetupAssistant
{
    public const int MaxResponseBytes = 64 * 1024;

    public static string CreatePrompt(string context, IReadOnlyDictionary<string, string> fields, string language)
    {
        ValidateContext(context);
        var instructions = language switch
        {
            "zh-CN" => $"""
                请只读核实本机配置，并按下方协议仅回复 JSON。未知字段填空字符串。

                只返回一个 JSON 对象，不要附带 Markdown 代码围栏、解释或额外属性。
                保持 relay_setup_version = 1 和 context = "{context}"。所需英文字段名及大小写必须与模板完全相同。
                只返回所请求的字段，每个字段值必须是 JSON 字符串。模板中的现有值或填写提示尚未核实，请核实后再返回；未知或未核实的值填 ""。
                仅以只读方式检查本机信息，不猜测路径、已安装版本或配置值。不要读取、索要或输出身份验证信息、凭据、令牌或密钥。
                不要修改配置、写入文件、执行安装命令、登记 Agent 或发起网络请求。不要请求网络权限或调用任何模型 API。
                AgentId 是稳定的小写 Relay 标签，不是登录名或账号标识。已有标签应保留；新登记可提出如 "agent-codex" 这样的简短标签，供用户审阅。标签须以小写字母开头，仅包含小写字母、数字或连字符，最多 64 个字符。
                此回复仅填写可编辑表单，供用户审阅，不构成任何操作的批准或授权。用户会另行审阅和应用；不要执行这些操作，不要填写批准理由或批准复选框。

                JSON 模板：
                """,
            "zh-TW" => $"""
                請唯讀查證本機設定，並依下方協定只回覆 JSON。未知欄位填空字串。

                只回傳一個 JSON 物件，不要附帶 Markdown 程式碼圍欄、說明或額外屬性。
                保持 relay_setup_version = 1 和 context = "{context}"。所需英文字段名稱及大小寫必須與範本完全相同。
                只回傳所要求的欄位，每個欄位值必須是 JSON 字串。範本中的現有值或填寫提示尚未查證，請查證後再回傳；未知或未查證的值填 ""。
                僅以唯讀方式檢查本機資訊，不猜測路徑、已安裝版本或設定值。不要讀取、索取或輸出驗證資訊、認證資料、權杖或金鑰。
                不要修改設定、寫入檔案、執行安裝命令、登記 Agent 或發起網路請求。不要要求網路權限或呼叫任何模型 API。
                AgentId 是穩定的小寫 Relay 標籤，不是登入名稱或帳號識別碼。已有標籤應保留；新登記可提出如 "agent-codex" 這樣的簡短標籤，供使用者審閱。標籤須以小寫字母開頭，僅包含小寫字母、數字或連字號，最多 64 個字元。
                此回覆僅填寫可編輯表單，供使用者審閱，不構成任何操作的核准或授權。使用者會另外審閱和套用；不要執行這些操作，不要填寫核准理由或核准核取方塊。

                JSON 範本：
                """,
            "ja" => $"""
                ローカル設定を読み取り専用で確認し、次の形式の JSON のみを返してください。不明な値は空文字にしてください。

                JSON オブジェクトを一つだけ返し、Markdown のコードフェンス、説明、追加のプロパティは含めないでください。
                relay_setup_version = 1 と context = "{context}" を維持してください。英語のフィールド名と大文字・小文字はテンプレートの表記を厳密に維持してください。
                要求されたフィールドだけを返し、すべての値を JSON 文字列にしてください。テンプレート内の現在値や入力のヒントは未確認です。確認してから返し、不明または未確認の値は "" にしてください。
                ローカル情報の読み取り専用の確認だけを行い、パス、インストール済みバージョン、設定値を推測しないでください。認証情報、資格情報、トークン、秘密鍵を読み取ったり、要求したり、出力したりしないでください。
                設定の変更、ファイルへの書き込み、セットアップコマンドの実行、Agent の登録、ネットワークリクエストは行わないでください。ネットワーク権限を要求せず、モデル API を呼び出さないでください。
                AgentId は安定した小文字の Relay ラベルで、ログイン名やアカウント識別子ではありません。既存のラベルは維持してください。新規登録では "agent-codex" などの短いラベルを提案し、利用者の確認に委ねてください。小文字で始まり、小文字、数字、ハイフンのみを使い、最大 64 文字にしてください。
                この返信は利用者が確認する編集可能なフォームへの入力だけに使用され、操作の承認や許可にはなりません。利用者が別途確認して適用します。操作を実行せず、承認理由や承認チェックボックスを入力しないでください。

                JSON テンプレート：
                """,
            "ko" => $"""
                로컬 설정을 읽기 전용으로 확인하고 아래 형식의 JSON만 반환하세요. 알 수 없는 값은 빈 문자열로 남기세요.

                JSON 객체 하나만 반환하고 Markdown 코드 울타리, 설명 또는 추가 속성을 포함하지 마세요.
                relay_setup_version = 1 및 context = "{context}"를 유지하세요. 요청된 영문 필드 이름과 대소문자는 템플릿과 정확히 같아야 합니다.
                요청된 필드만 반환하고 모든 필드 값을 JSON 문자열로 작성하세요. 템플릿의 현재 값이나 입력 힌트는 확인된 사실이 아닙니다. 확인 후 반환하고 알 수 없거나 확인되지 않은 값은 ""로 남기세요.
                로컬 정보를 읽기 전용으로만 확인하고 경로, 설치된 버전 또는 설정 값을 추측하지 마세요. 인증 정보, 자격 증명, 토큰 또는 비밀 키를 읽거나 요청하거나 출력하지 마세요.
                설정 변경, 파일 쓰기, 설치 명령 실행, Agent 등록 또는 네트워크 요청을 하지 마세요. 네트워크 권한을 요청하거나 모델 API를 호출하지 마세요.
                AgentId는 고정된 소문자 Relay 라벨이며 로그인 이름이나 계정 식별자가 아닙니다. 기존 라벨은 유지하세요. 새로 등록할 때는 "agent-codex" 같은 짧은 라벨을 제안하여 사용자가 검토하게 하세요. 소문자로 시작하고 소문자, 숫자 또는 하이픈만 사용하며 최대 64자여야 합니다.
                이 응답은 사용자가 검토할 편집 가능한 양식만 채우며 어떤 작업에 대한 승인이나 권한도 부여하지 않습니다. 사용자가 별도로 검토하고 적용합니다. 작업을 실행하거나 승인 사유 또는 승인 체크박스를 입력하지 마세요.

                JSON 템플릿:
                """,
            "es" => $"""
                Verifica la configuración local en modo de solo lectura y responde solo con JSON según el protocolo siguiente. Deja vacíos los valores desconocidos.

                Devuelve exactamente un objeto JSON, sin bloques Markdown, explicaciones ni propiedades adicionales.
                Mantén relay_setup_version = 1 y context = "{context}". Conserva exactamente los nombres de campo en inglés y sus mayúsculas y minúsculas.
                Devuelve solo los campos solicitados; todos los valores deben ser cadenas JSON. Los valores actuales o las indicaciones de la plantilla no están verificados. Verifícalos antes de devolverlos y usa "" si son desconocidos o no están verificados.
                Inspecciona solo la información local en modo de lectura. No adivines rutas, versiones instaladas ni valores de configuración. No leas, solicites ni muestres datos de autenticación, credenciales, tokens ni secretos.
                No cambies la configuración, escribas archivos, ejecutes comandos de instalación, registres agentes ni realices solicitudes de red. No solicites acceso a la red ni llames a ninguna API de modelos.
                AgentId es una etiqueta estable de Relay en minúsculas, no un nombre de inicio de sesión ni un identificador de cuenta. Conserva la etiqueta existente. Para un registro nuevo, puedes proponer una etiqueta corta como "agent-codex" para que el usuario la revise. Debe empezar por una letra minúscula y contener solo letras minúsculas, dígitos o guiones, con un máximo de 64 caracteres.
                La respuesta solo rellena un formulario editable para revisión del usuario. No aprueba ni autoriza ninguna acción. El usuario revisará y aplicará los cambios por separado; no ejecutes esas acciones ni rellenes motivos o casillas de aprobación.

                Plantilla JSON:
                """,
            _ => $"""
                Verify local configuration using read-only inspection and return only JSON following the protocol below. Leave unknown values empty.

                Return exactly one JSON object, without Markdown fences, explanations or extra properties.
                Keep relay_setup_version = 1 and context = "{context}". Keep the requested English field names exactly as shown, including their casing.
                Return only the requested fields; every field value must be a JSON string. The template contains current values or filling hints, not verified facts. Verify values before returning them; use "" for anything unknown or unverified.
                Use only read-only local inspection. Do not guess paths, installed versions or configuration values. Do not read, request or output authentication data, credentials, tokens or secrets.
                Do not change configuration, write files, execute setup commands, register agents or make network requests. Do not request network access or call any model API.
                AgentId is a stable lowercase Relay label, not a login name or an account identifier. Keep an established label; for a new registration you may propose a short label such as "agent-codex" for the user to review. It must start with a lowercase letter and contain only lowercase letters, digits or hyphens (maximum 64 characters).
                This reply only fills an editable form for user review. It is not approval or permission for any action. The user reviews and applies it separately; do not perform those actions or fill approval reasons or approval checkboxes.

                JSON template:
                """
        };
        if (!fields.ContainsKey("AgentId"))
            instructions = string.Join('\n', instructions.Split('\n').Where(line => !line.TrimStart().StartsWith("AgentId", StringComparison.Ordinal)));
        if (fields.ContainsKey("AgentName"))
            instructions += "\n" + (language switch
            {
                "zh-CN" => "AgentName 是用户自定义显示名称，可使用中文；保留用户填写的名称，未填写时可建议软件名称。不设定 Agent 的工作角色，不要求账号或内部会话 ID。高级路径信息未知时留空，手动交接无需这些路径。",
                "zh-TW" => "AgentName 是使用者自訂顯示名稱，可使用中文；保留使用者填寫的名稱，未填寫時可建議軟體名稱。不設定 Agent 的工作角色，不要求帳號或內部工作階段 ID。進階路徑資訊未知時留空，手動交接無須這些路徑。",
                "ja" => "AgentName はユーザーが決める表示名です。既存の名前を維持し、空ならアプリ名を提案できます。役割、アカウント、内部セッション ID は指定しません。不明なパスは空欄にしてください。手動での引き継ぎにパスは不要です。",
                "ko" => "AgentName은 사용자가 정하는 표시 이름입니다. 기존 이름을 유지하고 비어 있으면 앱 이름을 제안할 수 있습니다. 역할, 계정 또는 내부 세션 ID를 지정하지 마세요. 모르는 경로는 비워두세요. 수동 전달에는 경로가 필요하지 않습니다.",
                "es" => "AgentName es el nombre visible elegido por el usuario. Conserva el nombre indicado; si falta, puedes proponer el nombre de la aplicación. No asignes roles ni solicites cuentas o Id internos de sesión. Deja vacías las rutas desconocidas; no se necesitan para el envío manual.",
                _ => "AgentName is a user-chosen display name in any language. Keep an existing name; if empty, you may suggest the application name. Do not assign roles or request accounts or internal session IDs. Leave unknown advanced paths empty; manual handoff does not require them."
            }) + "\n";
        var template = JsonSerializer.Serialize(new { relay_setup_version = 1, context, fields }, RelayJson.Options);
        return instructions + "\n" + template;
    }

    public static Dictionary<string, string> Parse(string text, string expectedContext, IReadOnlyCollection<string> allowedFields)
    {
        ValidateContext(expectedContext);
        // Agent replies are external input: bound their size before parsing or allocating a JSON tree.
        if (Encoding.UTF8.GetByteCount(text) > MaxResponseBytes)
            throw new RelayException("setup_response_too_large", $"The setup response exceeds {MaxResponseBytes} UTF-8 bytes.");

        var json = Unwrap(text.Trim());
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new RelayException("setup_invalid_json", "Return one complete JSON object, optionally inside one json code fence, without surrounding text.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new RelayException("setup_invalid_response", "The setup response must be a JSON object.");

            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!properties.Add(property.Name))
                    throw Duplicate(property.Name);
                if (property.Name is not ("relay_setup_version" or "context" or "fields"))
                    throw new RelayException("setup_unknown_property", $"Unknown setup response property: {property.Name}.");
            }
            if (properties.Count != 3)
                throw new RelayException("setup_invalid_response", "The setup response requires relay_setup_version, context and fields.");

            var version = root.GetProperty("relay_setup_version");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                throw new RelayException("setup_invalid_version", "The setup response requires relay_setup_version 1.");
            var context = root.GetProperty("context");
            if (context.ValueKind != JsonValueKind.String || context.GetString() != expectedContext)
                throw new RelayException("setup_context_mismatch", $"The setup response must use context '{expectedContext}'.");
            var fields = root.GetProperty("fields");
            if (fields.ValueKind != JsonValueKind.Object)
                throw new RelayException("setup_invalid_fields", "The setup response fields must be an object of string values.");

            var allowed = new HashSet<string>(allowedFields, StringComparer.Ordinal);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields.EnumerateObject())
            {
                if (!allowed.Contains(field.Name))
                    throw new RelayException("setup_unknown_field", $"The setup response contains an unrequested field: {field.Name}.");
                if (result.ContainsKey(field.Name))
                    throw Duplicate(field.Name);
                if (field.Value.ValueKind != JsonValueKind.String)
                    throw new RelayException("setup_invalid_field_value", $"Setup field '{field.Name}' must contain a JSON string.");
                result.Add(field.Name, field.Value.GetString()!);
            }
            return result;
        }
    }

    private static string Unwrap(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstLineEnd = text.IndexOf('\n');
        var lastLineStart = text.LastIndexOf('\n');
        if (firstLineEnd < 0 || lastLineStart <= firstLineEnd ||
            text[..firstLineEnd].TrimEnd('\r') != "```json" || text[(lastLineStart + 1)..] != "```")
            throw new RelayException("setup_invalid_fence", "Use one complete json code fence with no surrounding text.");
        return text[(firstLineEnd + 1)..lastLineStart];
    }

    private static void ValidateContext(string context)
    {
        if (context is not ("agents" or "connections" or "adapters" or "migrations" or "settings" or "audit" or "handoff"))
            throw new RelayException("setup_invalid_context", $"Unsupported setup context: {context}.");
    }

    private static RelayException Duplicate(string name) =>
        new("setup_duplicate_property", $"Duplicate setup response property: {name}.");
}
