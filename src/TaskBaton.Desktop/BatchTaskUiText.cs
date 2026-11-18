namespace TaskBaton.Desktop;

internal static class BatchTaskUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Select","选择","選擇","選択","선택","Elegir");
        Add("Select all tasks","全选","全選","すべて選択","모두 선택","Seleccionar todas");
        Add("Selected: {0}","已选择：{0} 项","已選擇：{0} 項","選択中：{0} 件","선택: {0}개","Seleccionadas: {0}");
        Add("Mark selected complete","标记所选为完成","標記所選為完成","選択を完了にする","선택 항목 완료","Completar seleccionadas");
        Add("Delete selected tasks","删除所选任务","刪除所選任務","選択タスクを削除","선택 작업 삭제","Eliminar seleccionadas");
        Add("Clear selection","取消全选","取消全選","選択を解除","선택 해제","Quitar selección");
        Add("Select this task for batch actions","选择此任务进行批量操作","選擇此任務進行批次操作","一括操作するタスクを選択","일괄 작업할 항목 선택","Seleccionar para acciones en grupo");
        Add("Select task: {0}","选择任务：{0}","選擇任務：{0}","タスクを選択：{0}","작업 선택: {0}","Elegir tarea: {0}");
        Add("Delete these {0} selected tasks?","删除这 {0} 个已选任务？","刪除這 {0} 個已選任務？","選択した {0} 件を削除しますか？","선택한 작업 {0}개를 삭제할까요?","¿Eliminar estas {0} tareas seleccionadas?");
        Add("Mark these {0} selected tasks complete?","将这 {0} 个已选任务标记为完成？","將這 {0} 個已選任務標記為完成？","選択した {0} 件を完了にしますか？","선택한 작업 {0}개를 완료로 표시할까요?","¿Completar estas {0} tareas seleccionadas?");
    }
}
