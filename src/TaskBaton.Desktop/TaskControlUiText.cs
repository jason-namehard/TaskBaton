namespace TaskBaton.Desktop;

internal static class TaskControlUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void A(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        A("File preview","文件内容预览","檔案內容預覽","ファイル内容プレビュー","파일 내용 미리보기","Vista del contenido");
        A("Delete task","删除任务","刪除任務","タスクを削除","작업 삭제","Eliminar tarea");
        A("Mark handoff complete","标记传递完成","標記傳遞完成","引き継ぎを完了にする","전달 완료로 표시","Marcar entrega completada");
        A("Marked complete by the user. Agent completion was not verified.","用户已标记传递完成，未据此验证 Agent 的执行结果。","使用者已標記傳遞完成，未據此驗證 Agent 的執行結果。","ユーザーが完了にしました。Agent の実行結果は未確認です。","사용자가 완료로 표시했습니다. Agent 실행 결과는 검증되지 않았습니다.","El usuario marcó la entrega como completada. El resultado del agente no se ha verificado.");
        A("Mark this handoff complete yourself? Relay will stop waiting and record your decision. This does not verify the Agent's work or stop an already running Agent.","由你标记传递完成？软件将停止等待并记录此次人工决定。这不代表已验证 Agent 的工作，也不会终止已运行的 Agent。","由你標記傳遞完成？軟體將停止等待並記錄人工決定。這不代表已驗證 Agent 的工作，也不會終止已執行的 Agent。","手動で完了にしますか？待機を終了し、判断を記録します。Agent の作業確認や実行停止は行いません。","직접 전달을 완료로 표시할까요? 대기를 끝내고 결정을 기록합니다. Agent 작업을 검증하거나 실행을 중지하지는 않습니다.","¿Marcar la entrega como completada? Relay dejará de esperar y registrará tu decisión. Esto no verifica el trabajo ni detiene un agente en ejecución.");
        A("Delete this task and its history? Files will be kept; unregistered relay files remain eligible for retention cleanup. This does not stop an already running Agent. This action cannot be undone.","删除此任务及其历史记录？磁盘文件保留，未登记的中转文件仍按保留期清理。这不会终止已运行的 Agent。任务记录删除后无法恢复。","刪除此任務及其歷史紀錄？磁碟檔案保留，未登記的中轉檔案仍依保留期清理。這不會終止已執行的 Agent。刪除後無法復原。","タスクと履歴を削除しますか？ファイルは保持し、未登録の中継ファイルは保存期限に従い整理します。実行中の Agent は停止しません。記録は復元できません。","작업과 기록을 삭제할까요? 파일은 유지하며 미등록 중계 파일은 보존 기간에 따라 정리됩니다. 실행 중인 Agent는 중지되지 않습니다. 기록은 복구할 수 없습니다.","¿Eliminar la tarea y su historial? Los archivos se conservan; los archivos sin registrar siguen sujetos a retención. No se detiene un agente en ejecución. El registro no se puede recuperar.");
        A("Waiting for {0} to finish and write its receipt. Do not send the same request again.","等待 {0} 完成工作并写入回执，请勿重复发送同一要求。","等待 {0} 完成工作並寫入回執，請勿重複傳送同一要求。","{0} の作業完了と応答保存を待っています。同じ依頼を再送しないでください。","{0} 작업 완료 및 회신 저장 대기 중입니다. 같은 요청을 다시 보내지 마세요.","Esperando que {0} termine y guarde el recibo. No envíes la misma solicitud de nuevo.");
        const string jsonError="交接或回执 JSON 不完整，或字段名称、类型不符合格式。请重新复制完整 JSON；回执请按当前任务上下文中的模板重写。";
        entries[jsonError]=["The handoff or receipt JSON is incomplete, or a field name or type is invalid. Copy the complete JSON again; rewrite receipts using the current task context template.",jsonError,"交接或回執 JSON 不完整，或欄位名稱、型別不符合格式。請重新複製完整 JSON；回執請依目前任務內容中的範本重寫。","JSON が不完全か、フィールド名・型が一致しません。完全な JSON を再コピーし、応答には現在のタスクのテンプレートを使用してください。","JSON이 불완전하거나 필드 이름 또는 형식이 잘못되었습니다. 전체 JSON을 다시 복사하고 현재 작업의 회신 템플릿을 사용하세요.","El JSON está incompleto o un nombre o tipo de campo es incorrecto. Copia el JSON completo y usa la plantilla actual para los recibos."];
    }
}
