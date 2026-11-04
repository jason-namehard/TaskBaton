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

    private static string ImageInstructions(TaskRecord task) => task.Images is { Length: > 0 }
        ? "Inspect every attached image in images before performing this turn; open each image by its path. "
        : "";
}
