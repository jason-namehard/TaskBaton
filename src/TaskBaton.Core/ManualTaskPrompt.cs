namespace TaskBaton.Core;

public sealed partial class RelayService
{
    public string ManualTaskPrompt(string from,string to,string workRequest)
    {
        if(string.IsNullOrWhiteSpace(workRequest))
            throw new RelayException("manual_work_required","请先填写要交给 Agent 完成的工作正文。");
        var agents=Agents();
        AgentRecord Node(string id)=>agents.SingleOrDefault(a=>a.AgentId==id)
            ??throw new RelayException("manual_agent_unknown","请选择已登记的发送方和接收方。");
        var sender=Node(from);var recipient=Node(to);
        if(from==to)throw new RelayException("manual_same_agent","发送方与接收方不能相同。");
        return $"""
            请作为 {sender.DisplayName??from}（{from}）完成下面工作，并把交接文件交给 {recipient.DisplayName??to}（{to}）。

            工作正文：
            {workRequest.Trim()}

            完成后，把交接内容保存为真实存在的文件，只在对话中输出下面格式的 JSON，供用户复制回 TaskBaton 解析：
            {RelayJson.Write(new ManualTransferDraft(from,to,"文件的完整绝对路径","下一位的处理要求与最终完成条件摘要"))}

            用真实文件路径替换 file；message 简要说明下一位该做什么和何时才算整个任务完成，详细内容放交接文件，不另建保存 JSON 的文件。
            如果工作正文要求多轮往返，必须把往返要求、尚未完成的工作与最终完成条件写进交接文件，并在 message 中提醒下一位继续。当前一轮写完不代表整个任务结束，不能把仍需处理的交接说成全部完成。不要自行更改 from 和 to，也不要添加 JSON 外的寒暄。
            """;
    }
}
