namespace AgentRelay.Desktop;

internal static class GraphText
{
    public static string State(string display,string language)=>Legend(display,language);
    public static string Legend(string key,string language)
    {
        var index=language switch{"en"=>0,"zh-CN"=>1,"zh-TW"=>2,"ja"=>3,"ko"=>4,"es"=>5,_=>throw new ArgumentOutOfRangeException(nameof(language))};
        return Labels[key][index];
    }
    private static readonly IReadOnlyDictionary<string,string[]> Labels=new Dictionary<string,string[]>
    {
        ["cli-missing"]=["No CLI calling method registered.","尚未登记 CLI 调用方案。","尚未登記 CLI 呼叫方案。","CLI 呼び出し方法未登録。","CLI 호출 설정이 없습니다.","Sin método CLI registrado."],
        ["cli-failed"]=["CLI method registered; the latest probe failed. Inspect the test result.","已登记 CLI 调用方案，最近一次探针失败；请查看测试结果。","已登記 CLI 呼叫方案，最近一次探針失敗；請查看測試結果。","CLI 登録済み。直近のテスト失敗。","CLI 설정됨. 최근 테스트 실패.","CLI registrado; falló la última prueba."],
        ["cli-registered"]=["CLI method registered but not yet verified. You can send by hand; automatic turns start after a probe or one real handoff succeeds.","CLI 调用方案已登记，尚未验证。可手动发送；探针通过或一次真实交接成功后才会自动投递。","CLI 呼叫方案已登記，尚未驗證。可手動傳送；探針通過或一次真實交接成功後才會自動投遞。","CLI 登録済み・未検証。手動送信は可能で、プローブか実際の引き継ぎ 1 回の成功後に自動送信します。","CLI 등록됨, 미검증. 수동 전송은 가능하며 프로브나 실제 전달 1회 성공 후 자동 전송합니다.","CLI registrado sin verificar. Puedes enviar a mano; los turnos automáticos empiezan tras una prueba o un envío real correcto."],
        ["cli-ready"]=["CLI method verified and session visibility confirmed. Relay uses it automatically only when the task opts in; otherwise copy the prompt.","CLI 调用已验证且会话可见性已确认；任务选择自动投递时才会自动发送，否则复制提示词。","CLI 呼叫已驗證且工作階段可見性已確認；任務選擇自動投遞時才會自動傳送，否則複製提示詞。","CLI 検証・表示確認済み。タスクが自動送信を選んだときだけ使い、それ以外はプロンプトをコピーします。","CLI 검증 및 세션 확인됨. 작업이 자동 전송을 선택한 경우에만 사용하며, 아니면 프롬프트를 복사합니다.","CLI verificado y sesión confirmada; solo se usa automáticamente si la tarea lo permite; si no, copia el prompt."],
        ["node-cli-missing"]=["No bridge","无桥接","無橋接","ブリッジなし","브리지 없음","Sin puente"],
        ["node-cli-failed"]=["Last test failed","最近测试失败","最近測試失敗","直近のテスト失敗","최근 테스트 실패","Falló la última prueba"],
        ["node-cli-registered"]=["Not yet verified","尚未验证","尚未驗證","未検証","미검증","Sin verificar"],
        ["node-cli-ready"]=["Verified","已验证","已驗證","検証済み","검증됨","Verificado"],
        ["single"]=["One arrow: one-way calling recipe","单箭头：有单向调用方案","單箭頭：有單向呼叫方案","片矢印：一方向の呼び出し方法あり","단일 화살표: 단방향 호출 설정","Una flecha: método en un sentido"],
        ["double"]=["Two arrows: recipes in both directions","双箭头：有双向调用方案","雙箭頭：有雙向呼叫方案","両矢印：双方向の呼び出し方法あり","양쪽 화살표: 양방향 호출 설정","Dos flechas: métodos en ambos sentidos"],
        ["none"]=["Gray dashed: no recipe in either direction","灰色虚线：双方均无调用方案","灰色虛線：雙方均無呼叫方案","灰色破線：両方向とも呼び出し方法なし","회색 점선: 양방향 호출 설정 없음","Gris discontinua: sin método en ningún sentido"],
        ["colors"]=["Green: calling method registered; gray: no calling method. This graph is read-only: Relay decides automatic delivery from the recipient's verified bridge, and manual copy always works.","绿色：已登记调用方案；灰色：缺少调用方案。连接图只读：自动投递取决于接收方已验证的桥接，手动复制始终可用。","綠色：已登記呼叫方案；灰色：缺少呼叫方案。連線圖唯讀：自動投遞取決於接收方已驗證的橋接，手動複製始終可用。","緑：呼び出し方法あり、灰：なし。この図は読み取り専用で、自動送信は受信側の検証済みブリッジに従います。手動コピーは常に可能です。","초록: 호출 설정 있음, 회색: 없음. 이 그래프는 읽기 전용이며 자동 전송은 수신자의 검증된 브리지에 따릅니다. 수동 복사는 항상 가능합니다.","Verde: método de llamada registrado; gris: sin método. El gráfico es de solo lectura: el envío automático depende del puente verificado del receptor y la copia manual siempre funciona."],
        ["Available"]=["Available","可用","可用","利用可能","사용 가능","Disponible"],
        ["Allowed"]=["Enabled","已启用","已啟用","有効","사용 중","Activa"],
        ["Denied"]=["Paused or not enabled","已暂停或尚未启用","已暫停或尚未啟用","一時停止または未有効","일시 중지 또는 미활성","En pausa o sin activar"],
        ["Unavailable"]=["No calling recipe","缺少调用方案","缺少呼叫方案","呼び出し方法なし","호출 설정 없음","Sin método de llamada"]
    };
}
