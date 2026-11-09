namespace AgentRelay.Core;

public sealed partial class RelayService
{
    private static void ValidateTaskImages(ImageInput[]? images)
    {
        if (images is null) return;
        foreach (var image in images)
        {
            if (image is null || string.IsNullOrWhiteSpace(image.FileName) || image.Data is null)
                throw new RelayException("task_image_invalid", "图片必须包含文件名、媒体类型和图片内容。");
            var png = image.MediaType == "image/png" && image.Data.AsSpan().StartsWith(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
            var jpeg = image.MediaType == "image/jpeg" && image.Data.AsSpan().StartsWith(
                new byte[] { 0xff, 0xd8, 0xff });
            if (!png && !jpeg)
                throw new RelayException("task_image_invalid", "图片内容必须与 PNG 或 JPEG 媒体类型相符。");
        }
    }

    private TaskImage[] KeepTaskImages(StoreSession session, TaskRecord task, ImageInput[] images, DateTimeOffset now, int ttl)
    {
        var directory = workspace.RequireManaged(Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId), "images"));
        Directory.CreateDirectory(directory);
        var kept = new List<TaskImage>(images.Length);
        foreach (var image in images)
        {
            var id = Guid.NewGuid().ToString();
            var extension = image.MediaType == "image/png" ? ".png" : ".jpg";
            var path = workspace.RequireManaged(Path.Combine(directory, id + extension));
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(image.Data);
            AddArtifact(session, new ArtifactRecord(id, task.TaskId, task.AttemptId, "image", path, now, now.AddDays(ttl), "available"));
            kept.Add(new TaskImage(id, image.FileName, image.MediaType, path));
        }
        return kept.ToArray();
    }

    internal void RequireTaskImages(TaskRecord task)
    {
        if (task.Images is not { Length: > 0 }) return;
        store.Read(session =>
        {
            var artifacts = session.Query("SELECT json FROM artifacts WHERE task_id=$id AND attempt_id=$attempt",
                reader => RelayJson.Read<ArtifactRecord>(reader.GetString(0)), ("$id", task.TaskId), ("$attempt", task.AttemptId));
            foreach (var image in task.Images)
            {
                var artifact = artifacts.SingleOrDefault(item => item.ArtifactId == image.ImageId && item.Path == image.Path);
                if (artifact is not { Kind: "image", State: "available" } || artifact.ExpiresAt <= Now || !File.Exists(image.Path))
                    throw new RelayException("task_image_unavailable", "任务图片已过期或丢失，请重新创建任务并重新附加图片。");
            }
            return true;
        });
    }

    /// <summary>
    /// Images travel with the first turn only. A later turn receives images only when the user selected them for that
    /// exact turn; otherwise the Agent is told not to pretend it saw them.
    /// </summary>
    internal static TaskImage[] ImagesForTurn(TaskRecord task)
    {
        if (task.Images is not { Length: > 0 }) return [];
        if (task.ManualTurn == 0) return task.Images;
        if (task.ImageTurn != task.ManualTurn) return [];
        return task.ImageTurnIds is { Length: > 0 } ids
            ? task.Images.Where(image => ids.Contains(image.ImageId)).ToArray()
            : task.Images;
    }

    /// <summary>User choice: attach the selected images to one specific later turn, never to every turn.</summary>
    public TaskView AllowTurnImages(string taskId, int turn, string[]? imageIds = null) => store.Write(s =>
    {
        var task = s.Task(taskId);
        if (!task.IsFileHandoff) throw new RelayException("file_handoff_required", "此接口只用于文件或 CLI 工作交接。");
        if (Terminal(task.State)) throw new RelayException("session_task_finished", "已结束任务不能追加图片。");
        if (task.Images is not { Length: > 0 }) throw new RelayException("task_image_missing", "此任务没有保存的图片。");
        if (turn < 1) throw new RelayException("image_turn_invalid", "第一轮图片已自动携带；只能为后续轮次追加。");
        if (task.RelaySteps is { } steps && turn >= steps.Length)
            throw new RelayException("image_turn_invalid", $"接力计划的最后一轮为第 {steps.Length - 1} 轮，请选择计划内的轮次。");
        if (turn < task.ManualTurn || turn == task.ManualTurn && task.CliFileTurn == task.ManualTurn)
            throw new RelayException("image_turn_started", "该轮已开始投递，不能事后追加图片；请选择尚未开始的轮次。");
        var ids = (imageIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        foreach (var id in ids)
            if (task.Images.All(image => image.ImageId != id))
                throw new RelayException("task_image_missing", "所选图片不属于此任务：" + id);
        task.ImageTurn = turn; task.ImageTurnIds = ids.Length == 0 ? null : ids;
        s.Save(task);
        s.Event(task, "task.images_turn", "operator", Now,
            "turn=" + turn + " images=" + (ids.Length == 0 ? "all" : string.Join(",", ids)));
        return ManualView(task);
    });

    private static string ImageInstructions(TaskRecord task, TaskImage[] images)
    {
        if (images.Length > 0)
            return "Inspect every attached image in images before performing this turn; open each image by its path. ";
        // Originals stay viewable for the user, but a later turn must not be told to read them again.
        return task.Images is { Length: > 0 }
            ? "No images are attached to this turn. Do not claim to have inspected the original images; use current_file and the recorded results instead. "
                + "If this is a continued host conversation, earlier images may still be in its history; Relay cannot remove them. "
            : "";
    }
}
