using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class ManualContinuousTests
{
    [TestMethod]
    public void ManualTransferContinuesBeyondFourTurnsAndRequiresWholeTaskCompletion()
    {
        var root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        try
        {
            var service=new RelayService(root);service.Initialize();
            service.RegisterManual("a","manual","A");service.RegisterManual("b","manual","B");
            var file=Path.Combine(root,"work.md");File.WriteAllText(file,"Exchange revisions until the work is agreed.");
            var task=service.CreateFileHandoff(new("a","b",file)).Task;
            task=service.ConfigureFileHandoff(task.TaskId,new(ManualContinuous:true)).Task;
            Assert.IsFalse(service.CanSendFileHandoff(task));Assert.IsNull(service.FileHandoffProgress(task).MaxTurns);
            for(var turn=0;turn<5;turn++)
            {
                var reply=Path.Combine(root,"reply-"+turn+".md");File.WriteAllText(reply,"Revision "+turn);
                File.WriteAllText(service.ManualReceiptPath(task),RelayJson.Write(new ManualReceipt(task.TaskId,task.AttemptId,task.AwaitingAgentId!,turn,"done",true,reply,"Review this revision",TaskComplete:false)));
                Assert.IsEmpty(service.ImportManualReceipts().Errors);
                task=service.Status(task.TaskId).Task;
                Assert.AreEqual(TaskState.AwaitingReply,task.State);Assert.AreEqual(turn+1,task.ManualTurn);
            }
            File.WriteAllText(service.ManualReceiptPath(task),RelayJson.Write(new ManualReceipt(task.TaskId,task.AttemptId,task.AwaitingAgentId!,5,"done",false)));
            Assert.HasCount(1,service.ImportManualReceipts().Errors);
            Assert.AreEqual(TaskState.AwaitingReply,service.Status(task.TaskId).Task.State);
            File.WriteAllText(service.ManualReceiptPath(task),RelayJson.Write(new ManualReceipt(task.TaskId,task.AttemptId,task.AwaitingAgentId!,5,"done",false,TaskComplete:true)));
            Assert.IsEmpty(service.ImportManualReceipts().Errors);
            Assert.AreEqual(TaskState.Succeeded,service.Status(task.TaskId).Task.State);
        }
        finally {Directory.Delete(root,true);}
    }
}
