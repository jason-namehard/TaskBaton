using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public partial class MainWindow
{
    private void AddTaskImages(StackPanel card, TaskRecord task)
    {
        if (task.Images is not { Length: > 0 } images) return;
        var section = DetailSection(card);
        Caption(section, "Attached images");
        var thumbnails = new WrapPanel { Name = "TaskImageThumbnails" };
        section.Children.Add(thumbnails);
        foreach (var image in images)
        {
            try
            {
                var source = ImageComposer.DecodeImage(File.ReadAllBytes(image.Path));
                var content = new StackPanel();
                content.Children.Add(new Image { Source = source, Height = 76, Width = 100, Stretch = Stretch.Uniform });
                content.Children.Add(new TextBlock { Text = image.FileName, MaxWidth = 100,
                    TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Margin = new(0, 4, 0, 0) });
                var button = new Button { Name = "PreviewTaskImage", Content = content, Padding = new(6),
                    Margin = new(0, 0, 8, 8), ToolTip = image.FileName };
                button.SetResourceReference(StyleProperty, "GhostButtonStyle");
                AutomationProperties.SetAutomationId(button, "TaskImagePreview-" + image.ImageId);
                AutomationProperties.SetName(button, string.Format(T("Preview image: {0}"), image.FileName));
                button.Click += (_, _) => new ImagePreviewWindow(this, source, preferences.Language).ShowDialog();
                thumbnails.Children.Add(button);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FileFormatException or NotSupportedException or ArgumentException)
            {
                // Task images can expire or be removed outside Relay; keep the task readable and expose the missing preview.
                var note = Note(image.FileName + "\n" + T("Image preview unavailable. The task image may have expired or been removed.") + "\n" + error.Message);
                note.Name = "TaskImageUnavailable";
                section.Children.Add(note);
            }
        }
    }
}
