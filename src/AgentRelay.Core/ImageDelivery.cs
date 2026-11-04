namespace AgentRelay.Core;

internal sealed record PreparedTaskImage(TaskImage Image, byte[] Data);

internal static class ImageDelivery
{
    // Read before claiming a delivery so an unavailable image never leaves it in an uncertain submitting state.
    public static async Task<IReadOnlyList<PreparedTaskImage>> PrepareAsync(IReadOnlyList<TaskImage>? images, CancellationToken token)
    {
        if (images is null || images.Count == 0) return [];
        var result = new List<PreparedTaskImage>(images.Count);
        foreach (var image in images)
        {
            try { result.Add(new(image, await File.ReadAllBytesAsync(image.Path, token))); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new RelayException("task_image_unavailable", "无法读取任务图片“" + image.FileName + "”：" + error.Message);
            }
        }
        return result;
    }

    public static string AddFileInstructions(string prompt, IReadOnlyList<PreparedTaskImage> images) => images.Count == 0 ? prompt :
        prompt + "\n\nThe task includes image files. Open and inspect each image with your image-reading tool before performing the task:\n" +
        string.Join("\n", images.Select(image => "- " + image.Image.Path));
}
