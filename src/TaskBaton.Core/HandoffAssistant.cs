namespace TaskBaton.Core;

public sealed record HandoffAgentOption(string Id, string Name);
public sealed record HandoffDraft(string From, string To, string Content, bool Automatic = false);

public static class HandoffAssistant
{
    public static string CreatePrompt(IReadOnlyList<HandoffAgentOption> agents, string from, string to, string content, string language)
    {
        var instruction = language switch
        {
            "zh-CN" => "请根据用户的交接意图整理一条消息。软件只传递消息，不指定 Agent 角色。From 和 To 必须使用下方节点的 Id，且不能相同；Content 写入完整交接内容。内容不明确时先向用户确认，不猜测任务。仅返回模板格式的 JSON，不添加字段，不执行交接。此回复仅供填写表单，用户确认后才发送。",
            "zh-TW" => "請根據使用者的交接意圖整理一則訊息。軟體只傳遞訊息，不指定 Agent 角色。From 和 To 必須使用下方節點的 Id，且不能相同；Content 寫入完整交接內容。內容不明確時先向使用者確認，不猜測任務。僅回傳範本格式的 JSON，不新增欄位，不執行交接。此回覆僅供填寫表單，使用者確認後才傳送。",
            "ja" => "ユーザーの意図に沿って引き継ぎメッセージを作成してください。アプリは Agent の役割を決めません。From と To は下記の異なるノードの Id にし、Content に内容全文を入れてください。不明な内容はユーザーに確認してください。テンプレート形式の JSON だけを返し、フィールドを追加せず、送信を実行しないでください。返信は確認用フォームに入力されます。",
            "ko" => "사용자의 의도에 따라 전달 메시지를 작성하세요. 앱은 Agent 역할을 지정하지 않습니다. From과 To는 아래의 서로 다른 노드 Id여야 하며 Content에는 전체 내용을 넣으세요. 내용이 불분명하면 사용자에게 확인하세요. 템플릿 형식의 JSON만 반환하고 필드를 추가하거나 전송하지 마세요. 응답은 확인할 양식만 채웁니다.",
            "es" => "Prepara un mensaje según la intención del usuario. La aplicación no asigna roles a los agentes. From y To deben ser Id de nodos distintos de la lista; Content debe contener el mensaje completo. Pregunta al usuario si falta información. Devuelve solo JSON con el formato de la plantilla, sin campos adicionales ni ejecutar el envío. La respuesta solo rellenará el formulario para su revisión.",
            _ => "Prepare a handoff message from the user's intent. The app does not assign Agent roles. From and To must be distinct node Id values from the list below; Content contains the complete message. Ask the user if the intent is unclear. Return only JSON matching the template, with no extra fields. Do not send anything. This reply only fills a form for user review."
        };
        return instruction + "\n\nNodes:\n" + RelayJson.Write(agents) + "\n\nJSON:\n" + RelayJson.Write(new
        {
            relay_setup_version = 1, context = "handoff", fields = new Dictionary<string, string>
            { ["From"] = from, ["To"] = to, ["Content"] = content }
        });
    }

    public static HandoffDraft Parse(string reply, IReadOnlyList<HandoffAgentOption> agents)
    {
        var fields = SetupAssistant.Parse(reply, "handoff", ["From", "To", "Content"]);
        if (!fields.TryGetValue("From", out var from) || !fields.TryGetValue("To", out var to) ||
            !fields.TryGetValue("Content", out var content))
            throw new RelayException("handoff_missing_fields", "The reply requires From, To and Content.");
        return Validate(from, to, content, agents);
    }

    public static HandoffDraft Validate(string from, string to, string content, IReadOnlyList<HandoffAgentOption> agents)
    {
        if (!agents.Any(a => a.Id == from) || !agents.Any(a => a.Id == to))
            throw new RelayException("handoff_unknown_agent", "Choose registered sender and recipient nodes.");
        if (from == to)
            throw new RelayException("handoff_same_agent", "Sender and recipient must be different nodes.");
        if (string.IsNullOrWhiteSpace(content))
            throw new RelayException("handoff_empty_content", "Enter the handoff message.");
        return new(from, to, content);
    }
}
