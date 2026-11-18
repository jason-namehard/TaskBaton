namespace TaskBaton.Desktop;

internal static class WorkflowUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Open","打开","開啟","開く","열기","Abrir");
        Add("Exit","退出","結束","終了","종료","Salir");
        Add("CLI diagnostics","CLI 诊断详情","CLI 診斷詳情","CLI 診断","CLI 진단","Diagnóstico CLI");
        Add("Configure CLI in Agents: use one-click setup, or edit the program, arguments and output format. Test before enabling a direction.","在 Agent 管理中使用一键配置，或编辑程序、参数及输出格式。测试通过后再启用连接方向。","在 Agent 管理中一鍵設定，或編輯程式、參數及輸出格式。測試通過後再啟用連線方向。","Agent 管理で設定し、テスト後に方向を有効化。","Agent 관리에서 설정하고 테스트 후 연결을 활성화하세요.","Configura en Agentes y prueba antes de activar una dirección.");
        Add("No CLI calling method is registered. Use One-click setup in Agents, or Edit Agent to enter the program and arguments. This is not a failed probe.","尚未登记 CLI 调用方案。在 Agent 管理中一键配置，或编辑 Agent 填写程序和参数。这不代表探针失败。","尚未登記 CLI 呼叫方案。請在 Agent 管理中一鍵設定或編輯程式和參數。這不代表探針失敗。","CLI 未登録。Agent 管理で設定してください。テスト失敗ではありません。","CLI 미등록. Agent 관리에서 설정하세요. 테스트 실패가 아닙니다.","CLI sin registrar. Configura en Agentes; no es un fallo de prueba.");
        Add("Send through CLI","通过 CLI 发送","透過 CLI 傳送","CLI で送信","CLI로 전송","Enviar por CLI");
        Add("Sending through CLI…","正在通过 CLI 发送…","正在透過 CLI 傳送…","CLI で送信中…","CLI로 전송 중…","Enviando por CLI…");
        Add("CLI is ready for {0}. Send through CLI, or copy the prompt manually.","{0} 的 CLI 已就绪，可通过 CLI 发送，也可手动复制提示词。","{0} 的 CLI 已就緒，可透過 CLI 傳送或手動複製提示詞。","{0} の CLI が使用可能です。送信または手動コピー。","{0}의 CLI를 사용할 수 있습니다. 전송하거나 수동 복사하세요.","CLI listo para {0}. Envía o copia el prompt manualmente.");
        Add("CLI delivery is unconfirmed. Inspect the target session before sending again.","CLI 投递尚未确认。再次发送前请先检查目标会话。","CLI 投遞尚未確認。再次傳送前請先檢查目標工作階段。","CLI 配信未確認。再送前に対象セッションを確認。","CLI 전달 미확인. 재전송 전 세션을 확인하세요.","Entrega CLI sin confirmar. Revisa la sesión antes de reenviar.");
        Add("Demo mode blocks real CLI calls. Keep this workspace and switch to normal use to test your registered Agents.","当前演示模式禁止真实 CLI 调用。可保留本中转站并转为正式使用，再测试你登记的 Agent。","目前示範模式禁止真實 CLI 呼叫。可保留本中轉站並轉為正式使用，再測試已登記的 Agent。","デモでは実際の CLI 呼び出しは無効です。このワークスペースを通常利用に切り替えられます。","데모는 실제 CLI 호출을 차단합니다. 현재 공간을 일반 사용으로 전환할 수 있습니다.","La demo bloquea llamadas CLI reales. Conserva este espacio y cambia al uso normal.");
        Add("Use this workspace normally","保留当前数据并转为正式使用","保留目前資料並轉為正式使用","この空間を通常利用に切り替え","현재 데이터로 일반 사용","Usar este espacio normalmente");
    }
}
