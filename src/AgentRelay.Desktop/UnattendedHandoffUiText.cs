namespace AgentRelay.Desktop;

internal static class UnattendedHandoffUiText
{
    public static void AddTo(Dictionary<string, string[]> entries)
    {
        void Add(string en, string cn, string tw, string ja, string ko, string es) => entries[en] = [en, cn, tw, ja, ko, es];
        Add("Start unattended run", "开始无人值守运行", "開始無人值守執行", "無人運転を開始", "무인 실행 시작", "Iniciar ejecución desatendida");
        Add("Stop unattended run", "停止无人值守运行", "停止無人值守執行", "無人運転を停止", "무인 실행 중지", "Detener ejecución desatendida");
        Add("Maximum automatic sends", "自动投递次数上限", "自動投遞次數上限", "自動送信の上限回数", "최대 자동 전송 횟수", "Máximo de envíos automáticos");
        Add("Run duration (minutes)", "运行时长（分钟）", "執行時間（分鐘）", "実行時間（分）", "실행 시간(분)", "Duración de la ejecución (minutos)");
        Add("Unattended sends: {0}/{1} · cutoff {2}", "无人值守投递 {0}/{1} 次 · 截止 {2}", "無人值守投遞 {0}/{1} 次 · 截止 {2}", "無人送信 {0}/{1} 回 · 締切 {2}", "무인 전송 {0}/{1}회 · 마감 {2}", "Envíos desatendidos {0}/{1} · hasta {2}");
        Add("Unattended delivery stopped", "已停止无人值守投递", "已停止無人值守投遞", "無人送信を停止しました", "무인 전송이 중지되었습니다", "Envío desatendido detenido");
        Add("Unattended delivery stopped at the time limit", "已达时限，停止无人值守投递", "已達時限，停止無人值守投遞", "時間制限に達したため無人送信を停止しました", "시간 한도에 도달하여 무인 전송이 중지되었습니다", "Envío desatendido detenido al alcanzar el límite de tiempo");
        Add("Unattended delivery stopped at the send limit", "已达投递次数上限，停止无人值守投递", "已達投遞次數上限，停止無人值守投遞", "送信回数の上限に達したため無人送信を停止しました", "전송 한도에 도달하여 무인 전송이 중지되었습니다", "Envío desatendido detenido al alcanzar el límite de envíos");
        Add("Unattended delivery stopped because the conversation could not be verified", "无法核实目标会话，已停止无人值守投递", "無法核實目標對話，已停止無人值守投遞", "会話を確認できないため無人送信を停止しました", "대화를 확인할 수 없어 무인 전송이 중지되었습니다", "Envío desatendido detenido porque no se pudo verificar la conversación");
        Add("Unattended delivery is on hold until the host outcome can be confirmed. Check the conversation before sending manually.", "宿主轮次结果未确认，无人值守投递暂缓。手动发送前请检查目标会话。", "宿主輪次結果未確認，無人值守投遞暫緩。手動傳送前請檢查目標對話。", "ホストの実行結果を確認できるまで無人送信を保留します。手動送信の前に会話を確認してください。", "호스트 실행 결과를 확인할 때까지 무인 전송이 보류됩니다. 수동 전송 전에 대화를 확인하세요.", "El envío desatendido queda en espera hasta confirmar el resultado del anfitrión. Revisa la conversación antes de enviar manualmente.");
        Add("Waiting for the host turn to be confirmed before sending again.", "等待确认宿主轮次结果，再进行下一次投递。", "等待確認宿主輪次結果，再進行下一次投遞。", "ホストのターンが確認されるまで次の送信を待ちます。", "호스트 차례가 확인될 때까지 다음 전송을 기다립니다.", "El siguiente envío espera a que se confirme el turno del anfitrión.");
        Add("Unattended delivery needs a verified bridge and a known conversation or a verified new-conversation route.", "无人值守投递需要已验证的桥接，以及可核实的会话或已验证的新建会话路径。", "無人值守投遞需要已驗證的橋接，以及可核實的對話或已驗證的新建對話路徑。", "無人送信には検証済みのブリッジと、確認できる会話または検証済みの新規会話経路が必要です。", "무인 전송에는 검증된 브리지와 확인 가능한 대화 또는 검증된 새 대화 경로가 필요합니다.", "El envío desatendido requiere un puente verificado y una conversación comprobable o una ruta verificada para crearla.");
        Add("Enable Minimize to tray when closing and keep the PC awake. Relay can send only while it is running and Windows is awake.", "请开启「关闭时最小化到托盘」并让电脑保持唤醒。Relay 运行且 Windows 唤醒时才能投递。", "請開啟「關閉時最小化到系統匣」並讓電腦保持喚醒。Relay 執行且 Windows 喚醒時才能投遞。", "「閉じるときにトレイへ最小化」を有効にし、PC をスリープさせないでください。送信には Relay と Windows の稼働が必要です。", "닫을 때 트레이로 최소화를 켜고 PC가 절전 상태에 들어가지 않게 하세요. Relay와 Windows가 실행 중일 때만 전송할 수 있습니다.", "Activa Minimizar en la bandeja al cerrar y evita que el PC entre en suspensión. Relay solo puede enviar mientras está en ejecución y Windows está activo.");
        Add("The limits apply to new automatic sends. A turn already sent keeps its own deadline. Balance, charges and quota are unknown.", "上限约束新的自动投递。已投递轮次沿用单轮时限。余额、费用和额度未知。", "上限約束新的自動投遞。已投遞輪次沿用單輪時限。餘額、費用和額度未知。", "制限は新しい自動送信に適用されます。送信済みのターンには個別の期限があります。残高、料金、利用枠は不明です。", "한도는 새 자동 전송에 적용됩니다. 이미 전송한 차례에는 기존 기한이 적용됩니다. 잔액, 요금, 할당량은 알 수 없습니다.", "Los límites se aplican a nuevos envíos automáticos. Un turno ya enviado conserva su plazo. Se desconocen el saldo, los cargos y la cuota.");
    }
}
