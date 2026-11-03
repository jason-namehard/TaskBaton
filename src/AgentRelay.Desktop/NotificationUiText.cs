namespace AgentRelay.Desktop;

internal static class NotificationUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Notification sound","提示声音","提示聲音","通知音","알림 소리","Sonido de aviso");
        Add("The in-app reminder was shown and Windows accepted a silent notification-center entry.","已显示应用内提醒，Windows 已接收静默通知中心记录。","已顯示應用程式內提醒，Windows 已接收靜默通知中心記錄。","アプリ内に表示し、Windows 通知センターへ静かに記録しました。","앱 내 알림을 표시하고 Windows 알림 센터에 조용히 기록했습니다.","Aviso mostrado en la aplicación y registrado sin banner en Windows.");
        Add("Native notifications are unavailable. In-app notifications remain enabled.","系统通知暂不可用，已启用应用内右下角提示。","系統通知暫不可用，已啟用應用程式內提示。","システム通知は利用不可。アプリ内通知を有効にしました。","시스템 알림을 사용할 수 없어 앱 내 알림을 활성화했습니다.","Avisos del sistema no disponibles; avisos internos activados.");
        Add("Silent","无声","無聲","無音","무음","Silencio");
        Add("Water drop","水滴声","水滴聲","水滴","물방울","Gota de agua");
        Add("Soft piano","钢琴轻音","鋼琴輕音","やさしいピアノ","부드러운 피아노","Piano suave");
        Add("Warm chord","温馨和弦","溫馨和弦","温かな和音","따뜻한 화음","Acorde cálido");
        Add("Error sound","错误提示","錯誤提示","エラー音","오류 소리","Error");
        Add("Preview sound","试听","試聽","試聴","미리 듣기","Escuchar");
        Add("Notification sound saved","提示声音已保存","提示聲音已儲存","通知音を保存しました","알림 소리 저장됨","Sonido guardado");
        Add("Send test notification","发送测试通知","傳送測試通知","テスト通知を送信","테스트 알림 보내기","Enviar aviso de prueba");
        Add("Test notification","测试通知","測試通知","テスト通知","테스트 알림","Aviso de prueba");
        Add("In-app notification","应用内通知","應用程式內通知","アプリ内通知","앱 내 알림","Aviso de la aplicación");
        Add("Open task","打开任务","開啟任務","タスクを開く","작업 열기","Abrir tarea");
        Add("An in-app notification was shown instead.","已改为显示应用内右下角提示。","已改為顯示應用程式內右下角提示。","代わりにアプリ内通知を表示しました。","대신 앱 내 알림을 표시했습니다.","Se mostró un aviso dentro de la aplicación.");
        Add("This tests desktop notifications only. No task or model session was created.","仅测试桌面提醒，没有创建任务或模型会话。","僅測試桌面提醒，沒有建立任務或模型會話。","通知のみをテストします。タスクやモデル呼び出しはありません。","알림만 테스트하며 작업이나 모델 세션을 만들지 않습니다.","Solo prueba avisos; no crea tareas ni sesiones del modelo.");
        Add("Windows accepted the test notification. Check notification settings if no banner appears.","Windows 已接受测试通知；未出现横幅时，请检查系统通知与勿扰设置。","Windows 已接受測試通知；沒有橫幅時請檢查系統通知與勿擾設定。","Windows が通知を受け付けました。表示されない場合は通知設定を確認。","Windows가 알림을 수락했습니다. 배너가 없으면 시스템 설정을 확인하세요.","Windows aceptó el aviso. Revisa la configuración si no aparece un banner.");
        Add("Sounds follow Windows volume settings. Notification banners also depend on Windows notification and Do not disturb settings.","右下角提醒由本程序显示；Windows 通知中心可用时静默记录。提示音遵循系统音量设置。","右下角提醒由本程式顯示；Windows 通知中心可用時靜默記錄，提示音遵循系統音量。","右下の通知は本アプリが表示し、Windows には静かに記録します。音量はシステム設定に従います。","오른쪽 아래 알림은 앱에서 표시하며 Windows에는 조용히 기록합니다. 소리는 시스템 볼륨을 따릅니다.","La aplicación muestra el aviso; Windows registra sin banner. El sonido sigue el volumen del sistema.");
    }
}
