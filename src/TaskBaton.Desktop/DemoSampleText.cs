namespace TaskBaton.Desktop;

/// <summary>Display translations for the fixed samples created in a new demo workspace.</summary>
internal static class DemoSampleText
{
    private static readonly Dictionary<string, string[]> Samples = new(StringComparer.Ordinal)
    {
        ["# Demo: Result Ready\n\n计算 18 × 3。此任务和结果均为本地演示样本，没有调用模型。"] =
        ["# Demo: Result Ready\n\nCalculate 18 × 3. This task and result are local demo samples; no model was called.",
         "# 演示：结果已就绪\n\n计算 18 × 3。此任务和结果均为本地演示样本，没有调用模型。",
         "# 示範：結果已就緒\n\n計算 18 × 3。此任務和結果均為本機示範樣本，沒有呼叫模型。",
         "# デモ：結果を準備\n\n18 × 3 を計算します。このタスクと結果はローカルのデモで、モデルは呼び出していません。",
         "# 데모: 결과 준비\n\n18 × 3을 계산합니다. 이 작업과 결과는 로컬 데모이며 모델을 호출하지 않았습니다.",
         "# Demo: resultado listo\n\nCalcula 18 × 3. Esta tarea y su resultado son muestras locales; no se llamó a ningún modelo."],
        ["# Demo Result\n\n18 × 3 = 54。\n\n这是预设演示结果，不代表真实 Agent 执行。\n"] =
        ["# Demo result\n\n18 × 3 = 54.\n\nThis preset result does not represent work by a real Agent.\n",
         "# 演示结果\n\n18 × 3 = 54。\n\n这是预设演示结果，不代表真实 Agent 执行。\n",
         "# 示範結果\n\n18 × 3 = 54。\n\n這是預設示範結果，不代表真實 Agent 執行。\n",
         "# デモ結果\n\n18 × 3 = 54。\n\nこれは用意された結果であり、実際の Agent の実行ではありません。\n",
         "# 데모 결과\n\n18 × 3 = 54.\n\n미리 준비된 결과이며 실제 Agent의 실행이 아닙니다.\n",
         "# Resultado de demo\n\n18 × 3 = 54.\n\nEste resultado preparado no representa el trabajo de un Agent real.\n"],
        ["# Demo: Explicit Failure\n\n演示如何保留接收方失败报告；未调用模型。"] =
        ["# Demo: Explicit Failure\n\nShows how a recipient's failure is recorded; no model was called.",
         "# 演示：明确的失败\n\n演示如何保留接收方失败报告；未调用模型。",
         "# 示範：明確的失敗\n\n示範如何保留接收方失敗報告；未呼叫模型。",
         "# デモ：明示的な失敗\n\n受信側の失敗報告を記録する例です。モデルは呼び出していません。",
         "# 데모: 명시적 실패\n\n수신자의 실패 보고를 보존하는 예입니다. 모델을 호출하지 않았습니다.",
         "# Demo: fallo explícito\n\nMuestra cómo registrar un fallo del receptor; no se llamó a ningún modelo."],
        ["# Demo Failure\n\n示例输入文件缺失。此错误是预设演示数据。\n"] =
        ["# Demo failure\n\nThe sample input file is missing. This error is preset demo data.\n",
         "# 演示失败\n\n示例输入文件缺失。此错误是预设演示数据。\n",
         "# 示範失敗\n\n示例輸入檔案遺失。此錯誤是預設示範資料。\n",
         "# デモの失敗\n\nサンプル入力ファイルがありません。これは用意されたデモのエラーです。\n",
         "# 데모 실패\n\n샘플 입력 파일이 없습니다. 미리 준비된 데모 오류입니다.\n",
         "# Fallo de demo\n\nFalta el archivo de entrada de ejemplo. Este error es un dato preparado.\n"],
        ["演示：输入文件缺失。"] =
        ["Demo: input file missing.", "演示：输入文件缺失。", "示範：輸入檔案遺失。", "デモ：入力ファイルがありません。", "데모: 입력 파일이 없습니다.", "Demo: falta el archivo de entrada."],
        ["# Demo: Manual Handoff\n\n复制此请求提示词，查看显式 ack/complete 协议。未启动 Agent。"] =
        ["# Demo: Manual Handoff\n\nCopy this request prompt to see the explicit ack/complete protocol. No Agent was started.",
         "# 演示：手动交接\n\n复制此请求提示词，查看显式 ack/complete 协议。未启动 Agent。",
         "# 示範：手動交接\n\n複製此請求提示詞，查看明確的 ack/complete 協定。未啟動 Agent。",
         "# デモ：手動引き継ぎ\n\nこの依頼をコピーして明示的な ack/complete プロトコルを確認します。Agent は起動していません。",
         "# 데모: 수동 전달\n\n이 요청을 복사해 명시적인 ack/complete 프로토콜을 확인합니다. Agent는 시작되지 않았습니다.",
         "# Demo: entrega manual\n\nCopia esta solicitud para ver el protocolo explícito ack/complete. No se inició ningún Agent."]
    };

    public static string Translate(bool isSample, string text, string language)
    {
        if (!isSample) return text;
        var body = text;
        var prefix = "";
        // Relay's generated request contains a metadata header; keep it intact and translate only an exact sample body.
        if (text.StartsWith("---\nrelay_version: 1\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n---\n\n", StringComparison.Ordinal);
            if (end >= 0) { prefix = text[..(end + 6)]; body = text[(end + 6)..]; }
        }
        else if (text.StartsWith("---\r\nrelay_version: 1\r\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\r\n---\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) { prefix = text[..(end + 9)]; body = text[(end + 9)..]; }
        }
        if (!Samples.TryGetValue(body, out var translations)) return text;
        var index = language switch
        {
            "en" => 0, "zh-CN" => 1, "zh-TW" => 2, "ja" => 3, "ko" => 4, "es" => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported UI language.")
        };
        return prefix + translations[index];
    }
}
