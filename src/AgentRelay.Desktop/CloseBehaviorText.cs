namespace AgentRelay.Desktop;

internal static class CloseBehaviorText
{
    public static string Get(string key, string language)
    {
        var column = language switch { "en" => 0, "zh-CN" => 1, "zh-TW" => 2, "ja" => 3, "ko" => 4, "es" => 5, _ => throw new ArgumentOutOfRangeException(nameof(language)) };
        return Text[key][column];
    }

    private static readonly IReadOnlyDictionary<string,string[]> Text = new Dictionary<string,string[]>
    {
        ["title"] = ["Close TaskBaton", "关闭 TaskBaton", "關閉 TaskBaton", "TaskBaton を閉じる", "TaskBaton 닫기", "Cerrar TaskBaton"],
        ["question"] = ["Keep Relay running in the tray, or exit?", "缩小至托盘继续运行，还是直接退出？", "縮小至系統匣繼續執行，還是直接結束？", "トレイで実行を続けますか、終了しますか？", "트레이에서 계속 실행할까요, 종료할까요?", "¿Seguir en la bandeja o salir?"],
        ["remember"] = ["This choice is remembered. You can change it in Settings.", "只询问这一次，之后按本次选择关闭。可随时在设置中修改。", "只詢問這一次，之後依本次選擇關閉。可隨時在設定中修改。", "この選択を記憶します。設定から変更できます。", "이 선택을 기억합니다. 설정에서 변경할 수 있습니다.", "Se recordará tu elección. Puedes cambiarla en Ajustes."],
        ["tray"] = ["Minimize to tray", "缩小至托盘", "縮小至系統匣", "トレイに最小化", "트레이로 최소화", "Minimizar a la bandeja"],
        ["exit"] = ["Exit", "直接退出", "直接結束", "終了", "종료", "Salir"],
        ["setting"] = ["Minimize to tray when closing", "关闭时最小化到托盘", "關閉時最小化到系統匣", "閉じるときトレイに最小化", "닫을 때 트레이로 최소화", "Minimizar a la bandeja al cerrar"],
        ["description"] = ["When enabled, the top-right close button hides Relay in the tray and keeps receipt monitoring active. When disabled, it exits without asking again. Tray → Exit always exits.", "开启后，点击右上角关闭按钮将程序最小化到托盘，继续监听回执。关闭此选项后直接退出，不再询问；托盘中的“退出”始终直接退出。", "開啟後，右上角關閉按鈕會將程式縮小至系統匣並繼續監看回執。關閉選項後直接結束，不再詢問；系統匣的「結束」始終直接結束。", "有効時は閉じるボタンでトレイに隠れ、受領通知の監視を続けます。無効時は確認せず終了します。トレイの終了は常に終了します。", "활성화하면 닫기 버튼이 트레이로 숨기고 수신 확인을 계속 감시합니다. 비활성화하면 다시 묻지 않고 종료합니다. 트레이의 종료는 항상 종료합니다.", "Al activarlo, cerrar oculta Relay y mantiene la supervisión de recibos. Desactivado, sale sin volver a preguntar. Salir en la bandeja siempre termina la aplicación."],
        ["saved"] = ["Close behavior saved", "关闭方式已保存", "關閉方式已儲存", "閉じる動作を保存しました", "닫기 동작 저장됨", "Comportamiento de cierre guardado"]
    };
}
