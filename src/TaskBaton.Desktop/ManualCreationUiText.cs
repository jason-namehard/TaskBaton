namespace TaskBaton.Desktop;

internal static class ManualCreationUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("New manual delivery task","新建手动传递任务","新增手動傳遞任務","手動受け渡しタスクを作成","새 수동 전달 작업","Nueva entrega manual");
        Add("Write the work request below, copy the complete prompt to the sender Agent, then paste its handoff JSON here. Relay keeps tracking later manual replies until no further work remains.",
            "先选择发送方和接收方，填写要发送方完成的工作正文，再复制完整提示词给发送方。把它返回的交接 JSON 粘贴到下方解析，后续手动回传会持续记录，直到整项工作结束。",
            "先選擇發送方與接收方，填寫工作正文，再複製完整提示詞給發送方。將交接 JSON 貼到下方解析，後續回傳會持續記錄，直到整項工作結束。",
            "送信者と受信者を選び、作業内容を入力して完全なプロンプトを送信者に渡します。返された JSON を貼り付けて作成すると、作業全体が終わるまで返信を追跡します。",
            "발신자와 수신자를 선택하고 작업 내용을 작성한 뒤 전체 프롬프트를 발신 Agent에게 전달하세요. 반환된 JSON을 붙여 넣으면 전체 작업이 끝날 때까지 수동 회신을 추적합니다.",
            "Elige emisor y receptor, escribe el trabajo y copia el mensaje completo al Agent emisor. Pega su JSON abajo; Relay registra las respuestas manuales hasta finalizar todo el trabajo.");
        Add("Work to give the sender Agent","要交给发送方的工作正文","要交給發送方的工作正文","送信者への作業内容","발신 Agent에게 요청할 작업","Trabajo para el Agent emisor");
        Add("Copy complete prompt","复制完整提示词","複製完整提示詞","完全なプロンプトをコピー","전체 프롬프트 복사","Copiar mensaje completo");
        Add("Complete prompt copied. Send it to the sender Agent, then paste its JSON below.","完整提示词已复制。发给发送方 Agent，完成后把它返回的 JSON 粘贴到下方。","完整提示詞已複製。發給發送方 Agent，再將回傳的 JSON 貼到下方。","コピーしました。送信者に渡し、返された JSON を下に貼り付けてください。","복사했습니다. 발신 Agent에게 전달하고 반환된 JSON을 아래에 붙여 넣으세요.","Mensaje copiado. Envíalo al Agent emisor y pega su JSON abajo.");
        Add("Paste the sender Agent's handoff JSON below. Later replies use the task's context and receipt, so another handoff does not need to be created for each turn.","在下方粘贴发送方返回的交接 JSON。后续按任务提示词写回执即可，不需要每次往返都重新建任务。","在下方貼上發送方的交接 JSON。後續按任務提示寫回執，不必每輪重新建任務。","送信者の JSON を下に貼り付けます。以降は同じタスクのコンテキストと受領記録を使い、毎回作り直す必要はありません。","발신자의 JSON을 아래에 붙여 넣으세요. 이후에는 같은 작업의 문맥과 영수증을 사용하며 매번 새 작업을 만들 필요가 없습니다.","Pega el JSON del emisor. Las respuestas posteriores usan el contexto y recibo de esta tarea; no crees otra en cada turno.");
        Add("Or select an existing file","或选择已有文件","或選擇已有檔案","既存ファイルを選ぶ","기존 파일 선택","O elegir un archivo existente");
        Add("Use the sender and recipient selected above. An existing file can start the same continuous manual handoff.","使用上方选择的发送方和接收方。已有文件也可进入同样的持续手动传递流程。","使用上方選擇的發送方與接收方，已有檔案亦可持續手動傳遞。","上で選択した送信者と受信者を使います。既存ファイルでも同じ継続的な手動受け渡しを開始できます。","위에서 선택한 발신자와 수신자를 사용합니다. 기존 파일도 같은 연속 수동 전달을 시작할 수 있습니다.","Usa el emisor y receptor de arriba. Un archivo existente inicia el mismo seguimiento manual continuo.");
    }
}
