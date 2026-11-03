namespace AgentRelay.Desktop;

internal static class ManualUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void A(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        A("Relay moved to {0}. Previous folder: {1}. Cleanup is scheduled for {2}; changed and new files will be kept.","中转站已迁移到 {0}。原目录：{1}。计划清理时间：{2}；新增或修改的文件会保留。","中轉站已遷移至 {0}。原目錄：{1}。預定清理時間：{2}；新增或修改的檔案會保留。","中継先を {0} に移しました。旧フォルダー: {1}。削除予定: {2}。追加・変更されたファイルは保持します。","중계 폴더가 {0}(으)로 이동했습니다. 이전 폴더: {1}. 정리 예정: {2}. 추가·변경된 파일은 보존합니다.","Entrega trasladada a {0}. Carpeta anterior: {1}. Limpieza prevista: {2}; se conservan archivos nuevos o modificados.");
        A("Relay folder moved","中转站迁移完成","中轉站遷移完成","中継先を移動しました","중계 폴더 이동 완료","Carpeta de entrega trasladada");
        A("Pending relay move","等待迁移中转站","等待遷移中轉站","中継先の移動待ち","중계 폴더 이동 대기","Traslado pendiente");
        A("Relay will move when active handoffs and notifications finish. Check migration retries a move interrupted during copying.","正在执行的交接和通知结束后会自动迁移。复制中断时，可用“迁移校验”重试。","執行中的交接和通知結束後會自動遷移。複製中斷時可用「遷移校驗」重試。","進行中の引き継ぎと通知が終わると移動します。コピーが中断した場合は移行チェックで再試行してください。","진행 중인 전달과 알림이 끝나면 이동합니다. 복사가 중단되면 마이그레이션 확인으로 재시도하세요.","Se trasladará cuando terminen las entregas y los avisos activos. La comprobación reintenta una copia interrumpida.");
        A("Destination: {0}","目标目录：{0}","目標目錄：{0}","移動先: {0}","대상 폴더: {0}","Destino: {0}");
        A("Cancel pending move","取消待执行迁移","取消待執行遷移","移動予約を取り消す","대기 중인 이동 취소","Cancelar traslado pendiente");
        A("Previous folder: {0}\nScheduled cleanup: {1}","原目录：{0}\n计划清理时间：{1}","原目錄：{0}\n預定清理時間：{1}","旧フォルダー: {0}\n削除予定: {1}","이전 폴더: {0}\n정리 예정: {1}","Carpeta anterior: {0}\nLimpieza prevista: {1}");
        A("Changed or new files kept:","已保留新增或修改的文件：","已保留新增或修改的檔案：","追加・変更のため保持したファイル:","추가·변경되어 보존한 파일:","Archivos nuevos o modificados conservados:");
        A("Previous relay folder","原中转站目录","原中轉站目錄","以前の中継フォルダー","이전 중계 폴더","Carpeta de entrega anterior");
        A("Unchanged migrated files are deleted after 24 hours while Relay is running or the next time it starts. Changed or new files are kept.","迁移完成 24 小时后，Relay 运行时或下次启动时会清理原目录中未改动的迁移文件；新增或修改的文件会保留。","遷移完成 24 小時後，Relay 執行時或下次啟動時會清理原目錄中未變更的遷移檔案；新增或修改的檔案會保留。","移動から24時間後、Relayの実行中または次回起動時に、旧フォルダーの未変更ファイルを削除します。追加・変更ファイルは保持します。","이동 24시간 후 Relay 실행 중 또는 다음 시작 시 이전 폴더의 변경되지 않은 파일을 정리합니다. 추가·변경 파일은 보존합니다.","Tras 24 horas, Relay elimina los archivos trasladados sin cambios al ejecutarse o iniciarse de nuevo. Conserva archivos nuevos o modificados.");
        A("Retry cleanup","重试清理","重試清理","削除を再試行","정리 재시도","Reintentar limpieza");
        A("Handoff receipts updated","交接回执已更新","交接回執已更新","応答を更新しました","회신 갱신됨","Recibos actualizados");
        A("Install the rule in the instruction file your host actually loads. The one-line rule uses a little context; details are read on demand. Existing sessions may need to reread the file or restart.","请安装到宿主实际加载的指令文件中。常驻的一行占用少量上下文，详细规则按需读取；已有会话可能需要重新读取文件或重开会话。","請安裝至宿主實際載入的指令檔案。一行占用少量上下文，詳細規則按需讀取；既有工作階段可能需要重新讀取或重開。","ホストが読み込む指示ファイルに追加してください。詳細は必要時に読みます。既存セッションの再読込が必要な場合があります。","호스트가 읽는 지침 파일에 설치하세요. 상세 규칙은 필요할 때 읽으며 기존 세션은 다시 읽거나 재시작해야 할 수 있습니다.","Instala la regla en el archivo que el host realmente lee. Los detalles se cargan cuando hacen falta; las sesiones existentes pueden necesitar releerlo o reiniciarse.");
        A("Handoff file","交接文件","交接檔案","引き継ぎファイル","전달 파일","Archivo de entrega");
        A("Short work request","简短工作要求","簡短工作要求","短い作業依頼","간단한 작업 요청","Solicitud breve");
        A("Agent handoff JSON","Agent 生成的交接 JSON","Agent 產生的交接 JSON","Agent の引き継ぎ JSON","Agent 전달 JSON","JSON de entrega del agente");
        A("Copy handoff format","复制交接格式提示词","複製交接格式提示詞","引き継ぎ形式をコピー","전달 형식 복사","Copiar formato de entrega");
        A("Parse and create handoff","解析并创建交接","解析並建立交接","解析して引き継ぎ作成","분석하여 전달 생성","Analizar y crear entrega");
        A("Or select a file yourself","或者手动选择文件","或手動選擇檔案","または手動でファイルを選択","또는 파일 직접 선택","O selecciona un archivo");
        A("Choose file","选择文件","選擇檔案","ファイルを選択","파일 선택","Seleccionar archivo");
        A("File preview","文件预览","檔案預覽","ファイルプレビュー","파일 미리보기","Vista del archivo");
        A("Receipt needs attention","回执需要处理","回執需要處理","応答の確認が必要","회신 확인 필요","Revisar recibo");
        A("Waiting for {0} to receive and reply","等待 {0} 接收并回复","等待 {0} 接收並回覆","{0} の受信と返信待ち","{0} 수신 및 응답 대기","Esperando recepción y respuesta de {0}");
        A("{0} is processing","{0} 正在处理","{0} 正在處理","{0} が処理中","{0} 처리 중","{0} está procesando");
        A("Waiting for {0} to handle the reply","等待 {0} 查看回稿并处理","等待 {0} 查看回稿並處理","{0} の返信処理待ち","{0} 회신 처리 대기","Esperando que {0} procese la respuesta");
        A("Next: copy the prompt to {0}. Relay updates automatically when the Agent writes its receipt.","下一步：复制提示词给 {0}。Agent 写入回执后，看板自动更新。","下一步：複製提示詞給 {0}。Agent 寫入回執後，看板自動更新。","次に {0} へプロンプトをコピーしてください。応答保存後に自動更新します。","다음: {0}에게 프롬프트를 복사하세요. 회신 저장 후 자동 갱신됩니다.","Copia el prompt para {0}. El tablero se actualiza cuando se guarda el recibo.");
        A("A file is waiting for you. Read {0}, perform the request and write its receipt. No extra acknowledgement message is needed.","有文件交接给你。请读取 {0}，完成其中要求并写回执，无需额外寒暄或确认消息。","有檔案交接給你。請讀取 {0}，完成其中要求並寫回執，無須額外寒暄或確認訊息。","{0} を読み、作業と応答の保存を行ってください。挨拶は不要です。","{0}을 읽고 작업 후 회신을 저장하세요. 추가 인사는 필요하지 않습니다.","Lee {0}, realiza la solicitud y guarda el recibo. No hace falta otro acuse.");
        A("Ask Agent A to write a file for B, then append the copied instructions. Paste its JSON here and create the handoff. Installed Agent rules can provide this format automatically.","先告诉 A 要写什么文件给 B，再附上复制的格式提示词。将 A 的 JSON 粘贴到这里即可创建交接；安装接入规则后，Agent 可按规则主动输出该格式。","先告訴 A 要寫什麼檔案給 B，再附上複製的格式提示詞。將 A 的 JSON 貼到此處即可建立交接；安裝規則後可按規則主動輸出。","A に B へのファイル作成を依頼し、形式プロンプトを添えてください。返信 JSON を貼り付けて作成します。","A에게 B용 파일을 요청하고 복사한 형식을 덧붙이세요. JSON 응답을 붙여넣어 전달을 만드세요.","Pide a A un archivo para B y añade el formato copiado. Pega su JSON para crear la entrega.");
        A("After creating the requested file, return only this JSON. Use registered node IDs or names and an existing absolute file path. This message describes a handoff; do not create a second file containing the JSON.","完成用户要求的交接文件后，只输出下面格式的 JSON。收发方使用已登记节点名称或 ID，file 为已存在文件的完整路径；不要另建一个装 JSON 的文件。","完成交接檔案後，只輸出下列格式 JSON。收發方使用已登記節點名稱或 ID，file 為完整檔案路徑；不要另建裝 JSON 的檔案。","ファイル作成後、登録ノードと実在する絶対パスを使い JSON のみ返してください。JSON 用ファイルは作成しません。","파일 생성 후 등록 노드와 실제 절대 경로를 사용한 JSON만 반환하세요. JSON 파일은 따로 만들지 마세요.","Tras crear el archivo, devuelve solo este JSON con nodos registrados y una ruta absoluta existente. No crees otro archivo para el JSON.");
        A("Install handoff rule","安装交接规则","安裝交接規則","引き継ぎ規則を追加","전달 규칙 설치","Instalar regla de entrega");
        A("Remove handoff rule","移除交接规则","移除交接規則","引き継ぎ規則を削除","전달 규칙 제거","Quitar regla de entrega");
        A("Remove this Agent's Relay handoff rule? Other instructions will be kept.","移除此 Agent 的 Relay 交接规则？其他指令将保留。","移除此 Agent 的 Relay 交接規則？其他指令將保留。","この Agent の引き継ぎ規則を削除しますか？他の指示は保持されます。","이 Agent의 Relay 전달 규칙을 제거할까요? 다른 지침은 유지됩니다.","¿Quitar la regla de entrega de este agente? Se conservarán las demás instrucciones.");
        A("Copy Agent setup instructions","复制 Agent 自填配置提示词","複製 Agent 自填設定提示詞","Agent 設定手順をコピー","Agent 설정 지침 복사","Copiar instrucciones de configuración");
        A("Choose the Agent instruction file","选择 Agent 的指令文件","選擇 Agent 的指令檔案","Agent の指示ファイルを選択","Agent 지침 파일 선택","Seleccionar archivo de instrucciones");
        A("Remove all handoff rules","移除全部交接规则","移除全部交接規則","全ての引き継ぎ規則を削除","모든 전달 규칙 제거","Quitar todas las reglas");
        A("Receipt monitoring needs a refresh. Click Refresh to read saved receipts.","回执监听需要重新读取，请点击刷新以读取已保存回执。","回執監聽需要重新讀取，請點擊重新整理。","更新して保存済み応答を読み込んでください。","새로고침하여 저장된 회신을 읽으세요.","Actualiza para leer los recibos guardados.");
        A("Relay will append one small rule to this file. Detailed instructions load only when the Agent follows that rule. Removing Relay's ZIP folder cannot remove this rule; use Remove rules first.","将在此文件末尾添加一条短规则，详细内容仅在 Agent 遵循规则时读取。直接删除 ZIP 软件目录无法移除这条规则，删除软件前请先移除交接规则。","將在檔案末尾加入一條短規則，詳細內容按需讀取。直接刪除軟體目錄無法移除規則，請先移除交接規則。","短い規則を追記します。詳細は必要時に読みます。ソフトウェア削除前に規則を削除してください。","짧은 규칙을 추가합니다. 상세 내용은 필요할 때 읽습니다. 프로그램 폴더 삭제 전에 규칙을 제거하세요.","Se añadirá una regla breve. Las instrucciones se leen cuando hace falta. Quita las reglas antes de borrar el programa.");
        A("Remove Relay's marked rules from registered instruction files? Your other instructions will be kept. Do this before deleting the portable software folder.","从已登记的指令文件中移除 Relay 标记规则？其他内容保留。请在删除便携软件目录前执行。","從已登記指令檔移除 Relay 標記規則？其他內容保留。請於刪除軟體前執行。","登録ファイルから Relay 規則のみ削除しますか？他の指示は保持します。","등록 파일에서 Relay 규칙만 제거할까요? 다른 지침은 유지됩니다.","¿Quitar solo las reglas marcadas de Relay? Se conservarán las demás instrucciones.");
    }
}
