namespace AgentRelay.Desktop;

internal static class ParallelHandoffUiText
{
    public static void AddTo(Dictionary<string, string[]> entries)
    {
        void Add(string en, string cn, string tw, string ja, string ko, string es) => entries[en] = [en, cn, tw, ja, ko, es];
        Add("New parallel handoff", "新建并行分工", "新增平行分工", "並列引き継ぎを作成", "병렬 전달 만들기", "Nueva entrega en paralelo");
        Add("Create independent assignments and review their results together.", "创建独立分工，集中查看成果。", "建立獨立分工，集中檢視成果。", "独立した作業を作成し、結果をまとめて確認します。", "독립 작업을 만들고 결과를 함께 검토합니다.", "Crea asignaciones independientes y revisa sus resultados juntos.");
        Add("Task title", "任务标题", "任務標題", "タスクのタイトル", "작업 제목", "Título de la tarea");
        Add("Merge Agent", "汇合 Agent", "彙整 Agent", "統合担当 Agent", "통합 Agent", "Agente de integración");
        Add("Added", "新增", "新增", "追加", "추가", "Añadido");
        Add("Modified", "修改", "修改", "変更", "수정", "Modificado");
        Add("Deleted", "删除", "刪除", "削除", "삭제", "Eliminado");
        Add("Choose project folder", "选择项目文件夹", "選擇專案資料夾", "プロジェクトフォルダーを選択", "프로젝트 폴더 선택", "Elegir carpeta del proyecto");
        Add("Shared files (one project-relative path per line)", "共享文件（每行一个相对项目的路径）", "共用檔案（每行一個相對於專案的路徑）", "共有ファイル（プロジェクト相対パスを 1 行に 1 件）", "공유 파일(줄마다 프로젝트 상대 경로 하나)", "Archivos compartidos (una ruta relativa al proyecto por línea)");
        Add("Each assignment gets its own copies of these files. Make proposed changes only in its working folder.", "每份分工都有自己的文件副本。修改建议请写在各自的工作文件夹里。", "每份分工都有自己的檔案副本。請在各自的工作資料夾內提出修改。", "各作業にファイルのコピーが用意されます。変更案は各作業フォルダーに保存してください。", "각 작업에 파일 복사본을 제공합니다. 변경안은 각 작업 폴더에 저장하세요.", "Cada asignación recibe sus propias copias. Guarda los cambios propuestos en su carpeta de trabajo.");
        Add("Work assignment", "分工要求", "分工要求", "作業の指示", "작업 지시", "Instrucción de la asignación");
        Add("Add assignment", "添加分工", "新增分工", "作業を追加", "작업 추가", "Añadir asignación");
        Add("Merge instructions", "汇合要求", "彙整要求", "統合の指示", "통합 지시", "Instrucciones de integración");
        Add("Create assignments", "创建分工", "建立分工", "作業を作成", "작업 만들기", "Crear asignaciones");
        Add("Fill in the title, assignments and merge instructions.", "请填写标题、各项分工和汇合要求。", "請填寫標題、各項分工和彙整要求。", "タイトル、各作業、統合の指示を入力してください。", "제목, 각 작업, 통합 지시를 입력하세요.", "Completa el título, las asignaciones y las instrucciones de integración.");
        Add("Parallel results", "并行分工结果", "平行分工結果", "並列作業の結果", "병렬 작업 결과", "Resultados en paralelo");
        Add("Open working folder", "打开工作文件夹", "開啟工作資料夾", "作業フォルダーを開く", "작업 폴더 열기", "Abrir carpeta de trabajo");
        Add("Assignments, file ownership and conflicts", "分工、成果归属和冲突", "分工、成果歸屬和衝突", "作業、成果の帰属、競合", "작업, 결과 소유권 및 충돌", "Asignaciones, propiedad de archivos y conflictos");
        Add("Merge only the available results", "仅汇合可用成果", "僅彙整可用成果", "利用可能な結果だけを統合", "사용 가능한 결과만 통합", "Integrar solo los resultados disponibles");
        Add("Send conflicting versions to the merge Agent for review", "将冲突版本交给汇合 Agent 处理", "將衝突版本交給彙整 Agent 處理", "競合する版を統合担当 Agent に確認させる", "충돌 버전을 통합 Agent에 보내 검토", "Enviar las versiones en conflicto al agente de integración");
        Add("Create merge handoff", "创建汇合交接", "建立彙整交接", "統合の引き継ぎを作成", "통합 전달 만들기", "Crear entrega de integración");
        Add("Conflicting branch changes", "分工之间的文件冲突", "分工之間的檔案衝突", "作業間のファイル競合", "작업 간 파일 충돌", "Conflictos de archivos entre asignaciones");
        Add("Changed in the original project", "原项目中已变动的文件", "原專案中已變更的檔案", "元のプロジェクトで変更されたファイル", "원본 프로젝트에서 변경된 파일", "Archivos cambiados en el proyecto original");
        Add("Merge handoff", "汇合交接", "彙整交接", "統合の引き継ぎ", "통합 전달", "Entrega de integración");
        Add("Task removed", "任务已删除", "任務已刪除", "タスクは削除済み", "삭제된 작업", "Tarea eliminada");
        Add("Saved output is missing", "已保存的成果文件丢失", "已儲存的成果檔案遺失", "保存済みの成果が見つかりません", "저장된 결과 파일이 없습니다", "Falta el resultado guardado");
        Add("No accepted result", "没有已接收的成果", "沒有已接收的成果", "受領済みの成果がありません", "접수된 결과가 없습니다", "Sin resultado aceptado");
    }
}
