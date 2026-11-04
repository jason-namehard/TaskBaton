namespace AgentRelay.Desktop;

internal static class CliTaskUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("New CLI task","新建 CLI 任务","新增 CLI 任務","CLI タスクを作成","새 CLI 작업","Nueva tarea CLI");
        Add("Work request","工作要求","工作要求","作業指示","작업 요청","Solicitud de trabajo");
        Add("Planned steps","交接计划","交接計畫","手順","계획 단계","Pasos previstos");
        Add("Current step","当前步骤","目前步驟","現在の手順","현재 단계","Paso actual");
        Add("New conversation folder (optional)","新会话工作目录（选填，留空使用调用方案）","新會話工作目錄（選填，留空使用呼叫方案）","新規会話の作業フォルダー（任意）","새 대화 작업 폴더(선택)","Carpeta de la nueva conversación (opcional)");
        Add("Create and send through CLI","创建并通过 CLI 发送","建立並透過 CLI 傳送","作成して CLI で送信","만들고 CLI로 전송","Crear y enviar por CLI");
        Add("Write the work request, choose the conversations and send it through CLI. A real call may incur model charges.","填写工作要求、选择双方会话，软件将直接通过 CLI 安排执行。真实调用可能产生模型费用。","填寫工作要求、選擇雙方會話，軟體將直接透過 CLI 執行；真實呼叫可能收費。","指示と会話を指定して CLI で実行します。モデル料金が発生する場合があります。","요청과 대화를 선택해 CLI로 실행합니다. 모델 요금이 발생할 수 있습니다.","Escribe la solicitud y elige conversaciones. La llamada CLI puede generar cargos.");
        Add("Choose the source conversation for exact replies.","请选择来源会话，回稿才能准确续接。","請選擇來源會話，回稿才能準確續接。","正確に返信する元の会話を選択。","정확한 회신을 위해 원본 대화를 선택하세요.","Elige la conversación de origen para responder con precisión.");
        Add("Allow enabling the connection directions required by this task","启用本任务需要的已暂停或未授权方向（此后也生效）","啟用本任務需要的已暫停或未授權方向（此後也生效）","必要な方向を今後も有効化する","필요한 연결 방향을 이후에도 활성화","Permitir activar las direcciones necesarias también para futuras tareas");
        Add("Sender and recipient must be different.","发送方与接收方不能相同。","發送方與接收方不能相同。","送信者と受信者は別にしてください。","발신자와 수신자가 같을 수 없습니다.","Emisor y receptor deben ser distintos.");
        Add("Enter the work request.","请填写工作要求。","請填寫工作要求。","作業指示を入力。","작업 요청을 입력하세요.","Escribe la solicitud.");
        Add("Paste or drop images into the work request.","可在工作要求中粘贴或拖入图片。","可在工作要求中貼上或拖入圖片。","作業指示に画像を貼り付けるかドラッグできます。","작업 요청에 이미지를 붙여 넣거나 끌어다 놓을 수 있습니다.","Pega o arrastra imágenes a la solicitud de trabajo.");
        Add("Only images can be attached here. For PDF, video, audio, or other files, paste the full file path into your work request.","此处只能附加图片。PDF、视频、音频或其他文件，请将完整文件路径粘贴到工作要求中。","此處只能附加圖片。PDF、影片、音訊或其他檔案，請將完整檔案路徑貼到工作要求中。","ここには画像のみ添付できます。PDF、動画、音声などのファイルは、完全なファイルパスを作業指示に貼り付けてください。","여기에는 이미지만 첨부할 수 있습니다. PDF, 동영상, 오디오 및 기타 파일은 전체 파일 경로를 작업 요청에 붙여 넣으세요.","Aquí solo se pueden adjuntar imágenes. Para PDF, vídeo, audio u otros archivos, pega la ruta completa en la solicitud de trabajo.");
        Add("The image could not be opened. Check the file and try again.","无法打开图片，请检查文件后重试。","無法開啟圖片，請檢查檔案後重試。","画像を開けませんでした。ファイルを確認して再試行してください。","이미지를 열 수 없습니다. 파일을 확인한 후 다시 시도하세요.","No se pudo abrir la imagen. Comprueba el archivo e inténtalo de nuevo.");
        Add("Cannot attach file","无法附加文件","無法附加檔案","ファイルを添付できません","파일을 첨부할 수 없음","No se puede adjuntar el archivo");
        Add("Attached images","附加图片","附加圖片","添付画像","첨부 이미지","Imágenes adjuntas");
        Add("Image preview unavailable. The task image may have expired or been removed.","无法预览图片，任务图片可能已过期或被移除。","無法預覽圖片，任務圖片可能已過期或遭移除。","画像をプレビューできません。タスクの画像が期限切れか削除された可能性があります。","이미지를 미리 볼 수 없습니다. 작업 이미지가 만료되었거나 삭제되었을 수 있습니다.","La vista previa no está disponible. La imagen de la tarea puede haber caducado o haberse eliminado.");
        Add("Preview image: {0}","预览图片：{0}","預覽圖片：{0}","画像をプレビュー：{0}","이미지 미리 보기: {0}","Vista previa de la imagen: {0}");
        Add("Remove image: {0}","移除图片：{0}","移除圖片：{0}","画像を削除：{0}","이미지 삭제: {0}","Quitar imagen: {0}");
        Add("Image preview","图片预览","圖片預覽","画像プレビュー","이미지 미리 보기","Vista previa de la imagen");
        Add("Close image preview","关闭图片预览","關閉圖片預覽","画像プレビューを閉じる","이미지 미리 보기 닫기","Cerrar la vista previa");
        Add("Scroll to zoom · Drag to pan","滚轮缩放 · 按住拖动图片","滾輪縮放 · 按住拖曳圖片","スクロールで拡大・縮小 · ドラッグで移動","휠로 확대·축소 · 끌어서 이동","Rueda para ampliar · Arrastra para mover");
        Add("Zoom in","放大","放大","拡大","확대","Ampliar");
        Add("Zoom out","缩小","縮小","縮小","축소","Reducir");
        Add("Images are saved with the task. The recipient Agent needs image-reading support.","图片随任务保存，接收 Agent 需要具备读取图片的能力。","圖片隨任務儲存，接收 Agent 需要具備讀取圖片的能力。","画像はタスクと共に保存されます。受信 Agent には画像を読み取る機能が必要です。","이미지는 작업과 함께 저장됩니다. 수신 Agent에 이미지 읽기 기능이 필요합니다.","Las imágenes se guardan con la tarea. El Agent receptor debe poder leer imágenes.");
        Add("Images are saved with the task. This Agent receives image paths and must be able to open images.","图片随任务保存。此 Agent 会收到图片路径，需要能够打开图片。","圖片隨任務儲存。此 Agent 會收到圖片路徑，需要能夠開啟圖片。","画像はタスクと共に保存されます。この Agent は画像のパスを受け取り、画像を開ける必要があります。","이미지는 작업과 함께 저장됩니다. 이 Agent는 이미지 경로를 받으며 이미지를 열 수 있어야 합니다.","Las imágenes se guardan con la tarea. Este Agent recibe sus rutas y debe poder abrirlas.");
        Add("Images are saved with the task. This Agent must report image support and the model must be able to read images.","图片随任务保存。此 Agent 必须声明支持图片，模型也需要具备读图能力。","圖片隨任務儲存。此 Agent 必須宣告支援圖片，模型也需要具備讀圖能力。","画像はタスクと共に保存されます。この Agent が画像対応を報告し、モデルも画像を読み取れる必要があります。","이미지는 작업과 함께 저장됩니다. 이 Agent가 이미지 지원을 알리고 모델도 이미지를 읽을 수 있어야 합니다.","Las imágenes se guardan con la tarea. Este Agent debe indicar que admite imágenes y el modelo debe poder leerlas.");
        Add("Images are saved with the task. Images are sent directly; the model must be able to read them.","图片随任务保存并直接发送；模型需要具备读图能力。","圖片隨任務儲存並直接傳送；模型需要具備讀圖能力。","画像はタスクと共に保存され、直接送信されます。モデルには画像を読み取る機能が必要です。","이미지는 작업과 함께 저장되어 직접 전송됩니다. 모델이 이미지를 읽을 수 있어야 합니다.","Las imágenes se guardan con la tarea y se envían directamente; el modelo debe poder leerlas.");
        Add("The recipient has no CLI calling method. Configure it in Agents first.","接收方尚未配置 CLI 调用方案，请先到 Agent 管理配置。","接收方尚未設定 CLI，請先到 Agent 管理設定。","受信者の CLI を Agent 管理で設定してください。","Agent 관리에서 수신자의 CLI를 설정하세요.","Configura el CLI del receptor en Agentes.");
    }
}
