namespace TaskBaton.Desktop;

internal static class CompactTaskUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Review round {0}/{1}","第 {0}/{1} 轮验收","第 {0}/{1} 輪驗收","レビュー {0}/{1} 回目","검토 {0}/{1}회","Revisión {0}/{1}");
        Add("Completed steps: {0} · Completed reviews: {1} (limit: {2})","已完成 {0} 次处理 · 其中验收 {1} 次（最多 {2} 次）","已完成 {0} 次處理 · 其中驗收 {1} 次（最多 {2} 次）","処理済み {0} 回 · レビュー済み {1} 回（上限 {2} 回）","처리 완료 {0}회 · 검토 완료 {1}회 (최대 {2}회)","{0} pasos completados · {1} revisiones completadas (límite {2})");
        Add("Completed steps: {0} · Completed reviews: {1} (no limit)","已完成 {0} 次处理 · 其中验收 {1} 次（不设上限）","已完成 {0} 次處理 · 其中驗收 {1} 次（不設上限）","処理済み {0} 回 · レビュー済み {1} 回（上限なし）","처리 완료 {0}회 · 검토 완료 {1}회 (상한 없음)","{0} pasos completados · {1} revisiones completadas (sin límite)");
        Add("Next recipient: {0}","接下来交给：{0}","接下來交給：{0}","次の担当：{0}","다음 담당: {0}","Siguiente destinatario: {0}");
        Add("File contents","文件内容","檔案內容","ファイル内容","파일 내용","Contenido del archivo");
        Add("Instructions for this turn","本轮处理要求","本輪處理要求","今回の作業指示","이번 차례의 요청","Instrucciones de este turno");
        Add("Current file text","当前文件正文","目前檔案正文","現在のファイル本文","현재 파일 본문","Texto del archivo actual");
        Add("Content preview is unavailable. Open the task folder to read the file.","无法预览此文件正文，请打开任务文件夹查看。","無法預覽此檔案正文，請開啟任務資料夾查看。","本文をプレビューできません。タスクフォルダーからファイルを開いてください。","파일 본문을 미리 볼 수 없습니다. 작업 폴더에서 파일을 여세요.","No se puede mostrar el texto. Abre la carpeta de la tarea para consultar el archivo.");
    }
}
