using System.Text;

namespace AgentRelay.Core;

public sealed record CliHandoffRequest(string From,string To,string Prompt,string? OriginSessionId=null,
    string? TargetSessionId=null,bool EnableDirection=false,string? OriginCwd=null,string? TargetCwd=null,
    CollaborationOptions? Collaboration=null,bool AutoDeliver=false,int? TurnTimeoutMinutes=null,ImageInput[]? Images=null,
    RelayPlanOptions? RelayPlan=null);

public sealed partial class RelayService
{
    public TaskView CreateCliHandoff(CliHandoffRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Prompt)&&request.Images is not {Length:>0})
            throw new RelayException("cli_work_required","请填写要交给接收 Agent 执行的工作要求。");
        ValidateTaskImages(request.Images);
        if(request.From==request.To)throw new RelayException("manual_same_agent","发送方与接收方不能相同。");
        if(request.OriginSessionId is not null&&string.IsNullOrWhiteSpace(request.OriginSessionId)||
            request.TargetSessionId is not null&&string.IsNullOrWhiteSpace(request.TargetSessionId))
            throw new RelayException("session_id_required","请提供准确的会话 ID；新建目标会话请将 target_session_id 留为 null。");
        var agents=Agents();
        _=agents.SingleOrDefault(a=>a.AgentId==request.From)??throw new RelayException("agent_not_found","未找到发送 Agent。");
        _=agents.SingleOrDefault(a=>a.AgentId==request.To)??throw new RelayException("agent_not_found","未找到接收 Agent。");
        var options=new FileHandoffOptions(request.Collaboration,request.AutoDeliver,request.TurnTimeoutMinutes,RelayPlan:request.RelayPlan);
        ValidateFileHandoffOptions(options,request.From,request.To);
        // A recipient without a bridge still gets the task; its turns are copied manually.
        var target=CliExtension(request.To);
        if(request.TargetSessionId is not null&&target is not null&&CliRecipes.ResumeCommand(target.Recipe) is null)
            throw new RelayException("session_resume_unsupported","接收 Agent 的调用方案不支持续接已有会话，请选择新建会话或补充续接命令。");
        // Collaboration turns that return to the sender use CLI only when its exact conversation is bound; otherwise they are copied manually.
        var senderByCli=request.Collaboration is not null&&request.OriginSessionId is not null;
        if(senderByCli&&(CliExtension(request.From) is not {} senderExtension||CliRecipes.ResumeCommand(senderExtension.Recipe) is null))
            throw new RelayException("session_resume_unsupported","发送方需要配置准确会话续接命令，才能通过 CLI 把回稿送回原会话；也可以不选来源会话，改为手动传回。");
        foreach(var cwd in new[]{request.OriginCwd,request.TargetCwd})
            if(cwd is not null&&!Path.IsPathFullyQualified(cwd))throw new RelayException("session_cwd_invalid","会话项目目录需要完整路径。");
        var directions=new[]{(request.From,request.To)}.Where(_=>target is not null)
            .Concat(new[]{(request.To,request.From)}.Where(_=>senderByCli)).ToArray();
        var permissions=ConnectionPermissions();
        foreach(var (from,to) in directions)
            if(!permissions.Any(p=>p.From==from&&p.To==to&&p.Decision=="allow")&&!request.EnableDirection)
                throw new RelayException("cli_direction_disabled","此 CLI 任务需要的连接方向尚未启用。请在连接图启用，或明确勾选允许本次启用所需方向。");

        var drafts=Path.Combine(Home,".cli-drafts");Directory.CreateDirectory(drafts);
        var source=Path.Combine(drafts,"work-"+Guid.NewGuid().ToString("N")+".md");
        TaskView created;
        try
        {
            File.WriteAllText(source,string.IsNullOrWhiteSpace(request.Prompt)?"请查看附带的图片。":request.Prompt,new UTF8Encoding(false));
            created=CreateFileHandoff(new(request.From,request.To,source,"请执行工作要求文件中的任务，并按任务上下文写回执。"),null,true,request.Images);
        }
        finally{File.Delete(source);}
        var taskId=created.Task.TaskId;
        if(request.OriginSessionId is not null)BindManualTaskSession(taskId,request.From,request.OriginSessionId,request.OriginCwd);
        if(request.TargetSessionId is not null)BindManualTaskSession(taskId,request.To,request.TargetSessionId,request.TargetCwd);
        else UseNewTaskSession(taskId,request.To);
        ConfigureFileHandoff(taskId,options);
        foreach(var (from,to) in directions)
            if(request.EnableDirection)SetPermission(from,to,"allow");
        store.Write(s=>
        {
            var task=s.Task(taskId);task.Mode=target is null?"manual":"cli";task.AdapterId=target is null?"manual-file":"cli:"+request.To;
            task.CliNewSessionCwd=request.TargetSessionId is null?request.TargetCwd:null;
            s.Save(task);s.Event(task,"cli_handoff.created","operator",Now);
            return true;
        });
        return Status(taskId);
    }

    private const string ReceiptRules="Write the receipt JSON to receipt_file when finished; do not ask the user to import your reply or create another handoff JSON. "+
        "status: received/working/done/failed. Keep task_id, attempt_id, sender and turn unchanged. No greetings or repeated confirmations. "+
        "Save each reply as a new real file (reply_file is reserved for this turn). Permanent deliverables belong in the project. Publish one complete receipt or rename a temporary file.";

    public string FileHandoffPrompt(TaskRecord task)
    {
        if(!task.IsFileHandoff)throw new RelayException("file_handoff_required","此接口只用于文件或 CLI 工作交接。");
        RequireTaskImages(task);
        string Name(string id)=>Agents().SingleOrDefault(a=>a.AgentId==id)?.DisplayName??id;
        var receipt=ManualReceiptPath(task);
        var context=ManualContextPath(task);
        var current=task.AwaitingAgentId??task.RecipientId;
        var other=current==task.RequesterId?task.RecipientId:task.RequesterId;
        var reply=TurnReplyPath(task);
        var images=ImagesForTurn(task);
        // When the initiating Agent performs the first step itself, the original work came from the user.
        var firstStepIsRequester=task.RelaySteps is {Length:>0} firstPlan&&firstPlan[0].AgentId==task.RequesterId;
        var common=new
        {
            sender_name=task.ManualTurn==0?(firstStepIsRequester?"user":Name(task.RequesterId)):Name(other),recipient_name=Name(current),
            current_file=task.ManualCurrentFile??task.RequestPath,original_request_file=task.RequestPath,request=task.ManualNextAction,
            reply_file=reply,receipt_file=receipt
        };
        object document;
        if(task.Collaboration is {} c)
        {
            var allowed=AllowedOutcomes(c.Phase);
            var role=c.Phase=="criteria"?"initiator":current==c.AuthorId?"author":"reviewer";
            var duty=c.Phase switch
            {
                "criteria"=>"You are the initiating Agent. Read original_request_file and derive specific, verifiable acceptance criteria for the entire request. Write them as a concise string in acceptance_criteria in the receipt, set outcome=criteria_ready, and do not start the recipient's work or declare the task complete.",
                "implement"=>"You are the author. Do the work in original_request_file. Then save a real result file that states what you delivered and where it lives, and write a done receipt with outcome=submitted and result_file.",
                "revise"=>"You are the author. The reviewer requested changes: fix every item in open_items (details in current_file when the reviewer wrote notes). Then save a real result file describing the changes and write a done receipt with outcome=submitted and result_file.",
                "review"=>"You are the reviewer. Check submission_file and the project changes it describes against acceptance_criteria. If anything must change, set outcome=changes_requested and list each concrete unresolved issue in open_items; a notes file in result_file is optional. If it meets the criteria, set outcome=approved with no open_items. Never approve with open issues and never request changes without concrete items.",
                "accept"=>"You are the author. The reviewer approved submission_file. If you accept it as the final result, set outcome=accepted and put the final deliverable location or a short delivery summary in deliverable; this completes the task for the user. If you change anything, set outcome=submitted with a new result_file and it goes back to review.",
                _=>throw new ArgumentOutOfRangeException(nameof(task),c.Phase,"Unknown collaboration phase.")
            };
            document=new
            {
                common.sender_name,common.recipient_name,common.current_file,common.original_request_file,common.request,
                common.reply_file,common.receipt_file,images,
                collaboration=new
                {
                    role,phase=c.Phase,review_round=c.ReviewRound,max_review_rounds=c.MaxReviewRounds,
                    author=new{agent_id=c.AuthorId,name=Name(c.AuthorId)},reviewer=new{agent_id=c.ReviewerId,name=Name(c.ReviewerId)},
                    acceptance_criteria=c.Criteria,submission_file=c.SubmissionPath,open_items=c.OpenItems,allowed_outcomes=allowed
                },
                receipt_template=new ManualReceipt(task.TaskId,task.AttemptId,current,task.ManualTurn,"done",null,
                    c.Phase is "implement" or "revise"?reply:null,null,allowed[0],c.Phase=="review"?Array.Empty<string>():null,null,
                    AcceptanceCriteria:c.Phase=="criteria"?"Write the actual acceptance criteria here":null),
                instructions=ImageInstructions(task,images)+duty+(c.ReviewerFinal?" The task finishes only when the chosen reviewer approves with no unresolved items. ":" The task finishes only after the reviewer approves and the author accepts; finishing your part hands it on. ")+
                    (c.IndependentAcceptance&&c.Phase=="review"?"This review uses a session that was not used for the work it checks. A new session does not isolate the host's global rules or memory. ":"")+
                    "Use outcome, open_items and deliverable for the decision; needs_reply may be omitted. If you cannot do the work, write status=failed and explain in next_action. "+ReceiptRules
            };
        }
        else
        {
            var steps=Steps(task);
            var planned=steps.Length>0;
            var lastStep=planned&&task.ManualTurn+1>=steps.Length;
            var requiredNext=planned?lastStep?null:steps[task.ManualTurn+1].AgentId:task.HandoffPlan is {} plan&&task.ManualTurn+1<plan.Length?plan[task.ManualTurn+1]:null;
            var duty=planned?StepDuty(task,task.ManualTurn):"";
            int? remaining=task.ManualContinuous?null:planned?Math.Max(0,HandoffCount(steps.Length)-task.ManualTurn-1)
                :Math.Max(0,(task.MaxTurns??MaxManualTurns)-task.ManualTurn-1);
            var stepText=planned
                ? $"This is step {task.ManualTurn+1} of {steps.Length} in the user's relay plan ({HandoffCount(steps.Length)} handoffs). "
                : "";
            document=new
            {
                common.sender_name,common.recipient_name,common.current_file,common.original_request_file,common.request,
                common.reply_file,common.receipt_file,images,
                receipt_template=new ManualReceipt(task.TaskId,task.AttemptId,current,task.ManualTurn,"done",requiredNext is not null?true:task.ManualContinuous?false:null,TaskComplete:task.ManualContinuous?false:null),
                relay_plan=planned?new
                {
                    steps=steps.Select((step,index)=>new{step=index+1,agent_id=step.AgentId,name=Name(step.AgentId),role=step.Role.ToString().ToLowerInvariant(),duty=step.Duty}),
                    handoff_count=HandoffCount(steps.Length),current_step=task.ManualTurn+1,last_step=lastStep
                }:null,
                handoff_plan=task.HandoffPlan,required_next_agent=requiredNext,
                remaining_handoffs=remaining,
                instructions=ImageInstructions(task,images)+stepText+
                    (string.IsNullOrWhiteSpace(duty)?"":"This step's declared duty: "+duty+" ")+
                    "Read current_file and perform the request. "+
                    (requiredNext is not null?"The plan still requires required_next_agent: this task is not finished; save a real result_file, set needs_reply=true and give a concrete next_action for that Agent. ":"")+
                    (lastStep?"This is the last step of the plan: finish the work, set needs_reply=false and stop. Do not add an acceptance or delivery role that the plan did not give you. ":"")+
                    "Set needs_reply=true only when the other Agent must do concrete further work; then provide result_file and next_action. Set needs_reply=false when no required work remains, and never ask for further work in next_action at the same time. "+
                    (task.ManualContinuous?"This is a manual continuous transfer with no preset round limit. A done receipt completes only this turn. Set task_complete=true and needs_reply=false only after the whole original task has no work left for either Agent. Intermediate turns use needs_reply=true, task_complete=false, result_file and concrete next_action. ":$"At most {remaining} more handoffs are allowed before Relay pauses for the user. ")+ReceiptRules
            };
        }
        EnsureTurnControl(task);
        File.WriteAllText(context,RelayJson.Write(document));
        return "A file is waiting for you. Read "+context+", perform the request and write its receipt. No extra acknowledgement message is needed.";
    }
}
