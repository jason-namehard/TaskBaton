namespace AgentRelay.Desktop;

internal static class ProbeVisibilityUiText
{
    public static void AddTo(Dictionary<string,string[]> entries)
    {
        void Add(string en,string cn,string tw,string ja,string ko,string es)=>entries[en]=[en,cn,tw,ja,ko,es];
        Add("Clean up probe sessions and files","清理探针会话和文件","清理探針會話及檔案","テストの会話とファイルを整理","프로브 대화 및 파일 정리","Limpiar sesiones y archivos de prueba");
        Add("Clean up this Agent's recorded probe sessions and files? Verification stays enabled. Sessions used by real tasks, items whose ownership cannot be confirmed, and unsupported items are kept and reported. Some hosts can archive a session but cannot delete it. Once a probe session is removed, a new-session probe is needed to test resume again.",
            "清理此 Agent 已记录的探针会话和文件？已通过的验证会保留。真实任务使用的会话、无法确认归属的项目及宿主不支持清理的项目会保留并在结果中列出。部分宿主只能归档会话，不能删除。清理探针会话后，若要再测试续接，需要重新运行新会话探针。",
            "清理此 Agent 已記錄的探針會話及檔案？已通過的驗證會保留。真實任務使用的會話、無法確認歸屬的項目及宿主不支援清理的項目會保留並列於結果。部分宿主只能封存會話，不能刪除。清理探針會話後，若要再測試續接，需要重新執行新會話探針。",
            "この Agent の記録されたテスト会話とファイルを整理しますか？確認済みの状態は維持されます。実際のタスクで使う会話、所有を確認できない項目、ホストが対応しない項目は残して報告します。ホストによっては削除せずアーカイブします。会話を削除した後に再開をテストするには、新規会話テストが必要です。",
            "이 Agent에 기록된 프로브 대화와 파일을 정리할까요? 검증 상태는 유지됩니다. 실제 작업에서 사용하는 대화, 소유권을 확인할 수 없는 항목, 호스트가 지원하지 않는 항목은 보관하고 보고합니다. 일부 호스트는 삭제 대신 보관만 할 수 있습니다. 프로브 대화를 삭제한 후 재개를 테스트하려면 새 대화 프로브가 필요합니다.",
            "¿Limpiar las sesiones y los archivos de prueba registrados de este agente? La verificación se conserva. Se mantienen y notifican las sesiones usadas por tareas reales, los elementos cuya propiedad no puede confirmarse y los no compatibles. Algunos hosts solo permiten archivar sesiones. Tras eliminar una sesión de prueba, se necesita otra prueba de sesión nueva para volver a probar la reanudación.");
        Add("The probe session was cleaned up. Run a new-session probe before testing resume again.","探针会话已清理。如需再次测试续接，请先运行新会话探针。","探針會話已清理。如需再次測試續接，請先執行新會話探針。","テスト会話は整理済みです。再開をテストするには新規会話テストを実行してください。","프로브 대화가 정리되었습니다. 재개를 테스트하려면 새 대화 프로브를 먼저 실행하세요.","La sesión de prueba se ha limpiado. Ejecuta una prueba de sesión nueva antes de probar la reanudación.");
        Add("Demo mode does not clean real host sessions.","演示模式不会清理真实宿主会话。","示範模式不會清理真實宿主會話。","デモモードでは実際のホスト会話を整理しません。","데모 모드는 실제 호스트 대화를 정리하지 않습니다.","El modo de demostración no limpia sesiones reales del host.");
        Add("Cleaning up probe sessions…","正在清理探针会话…","正在清理探針會話…","テスト会話を整理中…","프로브 대화 정리 중…","Limpiando sesiones de prueba…");
        Add("Probe cleanup cancelled","已取消探针清理","已取消探針清理","テストの整理をキャンセル","프로브 정리 취소됨","Limpieza de prueba cancelada");
        Add("Probe cleanup completed.","探针清理完成。","探針清理完成。","テストの整理が完了しました。","프로브 정리가 완료되었습니다.","Limpieza de prueba completada.");
        Add("Probe cleanup finished with retained or failed items.","探针清理结束，部分项目已保留或清理失败。","探針清理結束，部分項目已保留或清理失敗。","テストの整理が終わりました。一部の項目は保持または失敗しました。","프로브 정리가 끝났지만 일부 항목이 보관되었거나 실패했습니다.","La limpieza terminó con elementos conservados o fallidos.");
        Add("No recorded probe items are eligible for cleanup.","没有可清理的已记录探针项目。","沒有可清理的已記錄探針項目。","整理対象の記録済みテスト項目はありません。","정리할 수 있는 기록된 프로브 항목이 없습니다.","No hay elementos de prueba registrados para limpiar.");
        Add("Probe cleanup result","探针清理结果","探針清理結果","テスト整理の結果","프로브 정리 결과","Resultado de limpieza de prueba");
        Add("Probe session","探针会话","探針會話","テスト会話","프로브 대화","Sesión de prueba");
        Add("Probe file","探针文件","探針檔案","テストファイル","프로브 파일","Archivo de prueba");
        Add("Host session index","宿主会话索引","宿主會話索引","ホストの会話索引","호스트 대화 인덱스","Índice de sesiones del host");
        Add("Removed","已删除","已刪除","削除済み","삭제됨","Eliminado");
        Add("Already absent","原本不存在","原本不存在","既に存在しません","이미 없음","Ya no existe");
        Add("Index updated","索引已更新","索引已更新","索引を更新済み","인덱스 업데이트됨","Índice actualizado");
        Add("Not supported","不支持","不支援","非対応","지원하지 않음","No compatible");
        Add("Ownership unverified","归属未确认","歸屬未確認","所有未確認","소유권 미확인","Propiedad sin confirmar");
        Add("Used by a task","被任务使用","被任務使用","タスクが使用中","작업에서 사용 중","Usado por una tarea");
        Add("Cleanup failed","清理失败","清理失敗","整理に失敗","정리 실패","Limpieza fallida");
        Add("Cleanup pending","等待清理","等待清理","整理待ち","정리 대기 중","Limpieza pendiente");
        Add("Path","路径","路徑","パス","경로","Ruta");
        Add("Backup path","备份路径","備份路徑","バックアップ先","백업 경로","Ruta de copia");
        Add("Host visibility", "宿主可见性", "宿主可見性", "ホストでの表示", "호스트 표시", "Visibilidad en el host");
        Add("Sync new conversations to the host sidebar", "把新会话同步到宿主侧栏", "將新會話同步至宿主側欄", "新しい会話をホストのサイドバーに同期", "새 대화를 호스트 사이드바에 동기화", "Sincronizar conversaciones nuevas con la barra lateral del host");
        Add("Relay syncs eligible CLI conversations to Zcode's sidebar index after each turn. Bring Zcode to the foreground to refresh the sidebar.",
            "Relay 在每轮结束后把符合条件的 CLI 会话同步到 Zcode 的侧栏索引。切到 Zcode 前台可刷新侧栏。",
            "Relay 在每輪結束後將符合條件的 CLI 會話同步到 Zcode 的側欄索引。切到 Zcode 前景可重新整理側欄。",
            "Relay は各ターンの後、対象の CLI 会話を Zcode のサイドバー索引に同期します。Zcode を前面にするとサイドバーが更新されます。",
            "Relay는 각 턴 후 대상 CLI 대화를 Zcode 사이드바 인덱스에 동기화합니다. Zcode를 앞으로 가져오면 사이드바가 새로 고쳐집니다.",
            "Relay sincroniza las conversaciones CLI aptas con el índice lateral de Zcode después de cada turno. Pon Zcode en primer plano para actualizar la barra lateral.");
        Add("Bring the host window to the front after syncing", "同步后把宿主窗口拉到前台", "同步後將宿主視窗帶到前景", "同期後にホストのウィンドウを前面に表示", "동기화 후 호스트 창을 앞으로 가져오기", "Traer la ventana del host al frente tras sincronizar");
        Add("Only needed when you are already watching Zcode. It interrupts whatever window you are using.",
            "只有你正盯着 Zcode 时才需要。会打断你当前正在用的窗口。", "只有你正在查看 Zcode 時才需要。這會打斷你目前使用的視窗。",
            "Zcode を見ているときにのみ必要です。現在使用中のウィンドウからフォーカスが移ります。", "Zcode를 보고 있을 때만 필요합니다. 현재 사용 중인 창의 작업을 방해합니다.",
            "Solo es necesario si estás pendiente de Zcode. Interrumpe la ventana que estés usando.");
        Add("Host visibility sync is off for this Agent.", "此 Agent 的宿主可见性同步已关闭。", "此 Agent 的宿主可見性同步已關閉。", "この Agent のホスト表示の同期はオフです。", "이 Agent의 호스트 표시 동기화가 꺼져 있습니다.", "La sincronización de visibilidad está desactivada para este agente.");
        Add("Host visibility settings saved", "宿主可见性设置已保存", "宿主可見性設定已儲存", "ホスト表示の設定を保存しました", "호스트 표시 설정이 저장되었습니다", "Configuración de visibilidad guardada");
        Add("Relay syncs eligible Zcode CLI conversations to the sidebar index after each turn. Bring Zcode to the foreground to refresh the sidebar. If syncing is off or fails, save your work and fully quit and restart Zcode if needed. If a conversation shows old content, switch conversations and reopen it. Do not repeat a paid probe just to refresh the view.",
            "Relay 在每轮结束后把符合条件的 Zcode CLI 会话同步到侧栏索引。切到 Zcode 前台可刷新侧栏。如果同步关闭或失败，必要时请保存工作并完全退出后重启 Zcode。如果会话显示旧内容，请切换会话后重新打开。不要为了刷新界面而重复运行付费探针。",
            "Relay 在每輪結束後將符合條件的 Zcode CLI 會話同步到側欄索引。切到 Zcode 前景可重新整理側欄。如果同步關閉或失敗，必要時請儲存工作並完全結束後重啟 Zcode。如果會話顯示舊內容，請切換會話後重新開啟。請勿為了更新畫面而重複執行付費探針。",
            "Relay は各ターンの後、対象の Zcode CLI 会話をサイドバー索引に同期します。Zcode を前面にするとサイドバーが更新されます。同期がオフまたは失敗した場合は、必要に応じて作業を保存し、Zcode を完全に終了して再起動してください。会話に古い内容が表示される場合は、別の会話に切り替えてから開き直してください。表示更新のために有料テストを繰り返さないでください。",
            "Relay는 각 턴 후 대상 Zcode CLI 대화를 사이드바 인덱스에 동기화합니다. Zcode를 앞으로 가져오면 사이드바가 새로 고쳐집니다. 동기화가 꺼져 있거나 실패하면 필요에 따라 작업을 저장하고 Zcode를 완전히 종료한 후 다시 시작하세요. 대화에 이전 내용이 표시되면 다른 대화로 전환한 후 다시 여세요. 화면을 갱신하려고 유료 프로브를 반복하지 마세요.",
            "Relay sincroniza las conversaciones CLI aptas de Zcode con el índice lateral después de cada turno. Pon Zcode en primer plano para actualizar la barra lateral. Si la sincronización está desactivada o falla, guarda tu trabajo y cierra Zcode por completo antes de reiniciarlo si hace falta. Si una conversación muestra contenido antiguo, cambia de conversación y vuelve a abrirla. No repitas una prueba de pago solo para actualizar la vista.");
        Add("The sidebar index was synced for this probe. Bring Zcode to the foreground to refresh the sidebar.",
            "此探针的侧栏索引已同步。切到 Zcode 前台可刷新侧栏。", "此探針的側欄索引已同步。切到 Zcode 前景可重新整理側欄。",
            "このテストのサイドバー索引を同期しました。Zcode を前面にするとサイドバーが更新されます。", "이 프로브의 사이드바 인덱스가 동기화되었습니다. Zcode를 앞으로 가져오면 사이드바가 새로 고쳐집니다.",
            "El índice lateral se sincronizó para esta prueba. Pon Zcode en primer plano para actualizar la barra lateral.");
        Add("Sidebar sync was off for this probe. The session may not appear in Zcode's sidebar.",
            "此探针运行时侧栏同步已关闭，会话可能不会显示在 Zcode 侧栏中。", "此探針執行時側欄同步已關閉，會話可能不會顯示在 Zcode 側欄中。",
            "このテストではサイドバー同期がオフでした。会話が Zcode のサイドバーに表示されない場合があります。", "이 프로브에서는 사이드바 동기화가 꺼져 있었습니다. 대화가 Zcode 사이드바에 표시되지 않을 수 있습니다.",
            "La sincronización lateral estaba desactivada para esta prueba. Es posible que la conversación no aparezca en la barra lateral de Zcode.");
        Add("The sidebar index was not updated for this probe. The session may already be managed by Zcode or may not be eligible for sync.",
            "此探针没有更新侧栏索引。该会话可能已由 Zcode 管理，或不符合侧栏同步条件。", "此探針沒有更新側欄索引。該會話可能已由 Zcode 管理，或不符合側欄同步條件。",
            "このテストではサイドバー索引を更新しませんでした。会話がすでに Zcode で管理されているか、同期対象外の可能性があります。", "이 프로브는 사이드바 인덱스를 갱신하지 않았습니다. 대화가 이미 Zcode에서 관리되거나 동기화 대상이 아닐 수 있습니다.",
            "No se actualizó el índice lateral para esta prueba. Puede que Zcode ya gestione la conversación o que no sea apta para sincronizarse.");
        Add("Sidebar sync failed for this probe. The CLI call result is unchanged.",
            "此探针的侧栏同步失败，CLI 调用结果不受影响。", "此探針的側欄同步失敗，CLI 呼叫結果不受影響。", "このテストのサイドバー同期に失敗しました。CLI 呼び出しの結果には影響しません。", "이 프로브의 사이드바 동기화에 실패했습니다. CLI 호출 결과는 변경되지 않습니다.",
            "Falló la sincronización lateral de esta prueba. El resultado de la llamada CLI no cambia.");
        Add("Zcode could not be brought to the foreground. Switch to Zcode to refresh the sidebar.",
            "无法将 Zcode 窗口拉到前台。请切到 Zcode 以刷新侧栏。", "無法將 Zcode 視窗帶到前景。請切到 Zcode 以重新整理側欄。", "Zcode を前面に表示できませんでした。Zcode に切り替えてサイドバーを更新してください。", "Zcode를 앞으로 가져오지 못했습니다. Zcode로 전환하여 사이드바를 새로 고치세요.",
            "No se pudo traer Zcode al frente. Cambia a Zcode para actualizar la barra lateral.");
        Add("The DSH desktop bridge runs inside the open DSH client, so new sessions and continued turns normally appear without restarting. Check the returned session ID and refresh or reopen that conversation if needed. Do not restart DSH while a turn is running.",
            "DSH 桌面桥接由已打开的 DSH 执行，新建会话和续接内容通常无需重启即可显示。请核对返回的会话 ID，必要时刷新或重新打开该会话。执行期间不要重启 DSH。",
            "DSH 桌面橋接由已開啟的 DSH 執行，新建會話及續接內容通常無需重啟即可顯示。請核對傳回的會話 ID，必要時重新整理或開啟該會話。執行期間請勿重啟 DSH。",
            "DSH デスクトップブリッジは起動中の DSH 内で実行するため、通常は再起動せずに新規セッションと続行内容が表示されます。返されたセッション ID を確認し、必要なら会話を更新・開き直してください。実行中は DSH を再起動しないでください。",
            "DSH 데스크톱 브리지는 열린 DSH 안에서 실행되므로 보통 재시작 없이 새 세션과 이어진 내용이 표시됩니다. 반환된 세션 ID를 확인하고 필요하면 대화를 새로 고치거나 다시 여세요. 실행 중에는 DSH를 재시작하지 마세요.",
            "El puente de escritorio ejecuta los turnos dentro de DSH abierto, por lo que normalmente aparecen sin reiniciar. Comprueba el ID devuelto y actualiza o vuelve a abrir esa conversación si hace falta. No reinicies DSH durante un turno.");
        Add("A successful machine receipt does not confirm that the host's desktop has refreshed. For new and continued sessions, refresh the session list or reopen the conversation; if needed, save your work and restart that client. A missing marker in a stale view is not a reason to run the probe again.",
            "机器回执成功不代表宿主界面已刷新。新建会话和续接内容均可先刷新列表或重新打开会话；必要时保存工作并重启该客户端。界面未刷新导致看不到标记时，不要重复运行探针。",
            "機器回執成功不代表宿主介面已更新。新建會話和續接內容均可先重新整理列表或開啟會話；必要時儲存工作並重啟用戶端。因介面未更新而看不到標記時，請勿重跑探針。",
            "マシン応答の成功はデスクトップ表示の更新を保証しません。新規・続行とも一覧を更新するか会話を開き直し、必要なら作業を保存してクライアントを再起動してください。古い表示にマーカーがなくても再実行しないでください。",
            "기계 응답 성공이 데스크톱 화면 갱신을 보장하지는 않습니다. 새 세션과 이어진 내용은 목록을 새로 고치거나 대화를 다시 열고, 필요하면 작업을 저장한 후 클라이언트를 재시작하세요. 오래된 화면에 표식이 없다는 이유로 탐침을 반복하지 마세요.",
            "Un recibo correcto no confirma que el escritorio se haya actualizado. Actualiza la lista o vuelve a abrir la conversación nueva o continuada; si hace falta, guarda el trabajo y reinicia el cliente. No repitas la prueba porque una vista desactualizada no muestre el marcador.");
        Add("The call succeeded. Confirm visibility only after you see this marker in the correct conversation. If the desktop has not refreshed, leave the confirmation unchecked; it is not a failed call.",
            "调用已成功。只有在正确会话里看到本次标记后才确认可见；宿主界面尚未刷新时，请保持未勾选，这不代表调用失败。",
            "呼叫已成功。只有在正確會話裡看到本次標記後才確認可見；宿主介面尚未更新時，請保持未勾選，這不代表呼叫失敗。",
            "呼び出しは成功しました。正しい会話で今回のマーカーを確認してから可視性を承認してください。表示が未更新なら未選択のままにしてください。呼び出し失敗ではありません。",
            "호출은 성공했습니다. 올바른 대화에서 이번 표식을 본 후에만 표시 여부를 확인하세요. 데스크톱이 아직 갱신되지 않았으면 선택하지 마세요. 호출 실패는 아닙니다.",
            "La llamada tuvo éxito. Confirma la visibilidad solo tras ver este marcador en la conversación correcta. Si el escritorio no se ha actualizado, deja la casilla sin marcar; la llamada no ha fallado.");
    }
}
