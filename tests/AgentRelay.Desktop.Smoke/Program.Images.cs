using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentRelay.Core;
using AgentRelay.Desktop;

namespace AgentRelay.Desktop.Smoke;

internal static partial class Program
{
    private static BitmapSource ImageFixture(byte red)
    {
        const int width = 384, height = 256;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 70; pixels[i + 1] = (byte)(i / 4 % width / 2); pixels[i + 2] = red; pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task ExerciseImagePreview(Window owner, Action<ImagePreviewWindow> exercise)
    {
        ImagePreviewWindow? preview = null;
        await Wait(() => (preview = Application.Current.Windows.OfType<ImagePreviewWindow>()
            .FirstOrDefault(window => window.Owner == owner && window.IsVisible)) is not null);
        try { exercise(preview!); }
        catch { preview!.Close(); throw; }
    }

    private static async Task CheckTaskImageInput(MainWindow owner, string root)
    {
        if (!ClipboardReady()) { Skip("Clipboard image input checks skipped: the desktop clipboard is locked (workstation locked or another app holds it)."); return; }
        var station = new RelayService(Path.Combine(root, "image-input")); station.Initialize();
        station.RegisterManual("image-a", "fixture", "Image A"); station.RegisterManual("image-b", "fixture", "Image B");
        Appearance.Save(station.Home, new DesktopPreferences("en", NotificationSound: "none"));
        var bitmap = ImageFixture(160);
        CliHandoffRequest? request = null;
        await Show(new CliHandoffDialog(owner, station, "en"), async dialog =>
        {
            var composer = Find<ImageComposer>(dialog, "CliTaskImageComposer");
            var text = Find<TextBox>(dialog, "CliTaskPrompt");
            text.Text = "Inspect both diagrams and explain the difference."; text.Focus();
            Clipboard.SetImage(bitmap);
            Check(ApplicationCommands.Paste.CanExecute(null, text), "A bitmap-only clipboard enables Paste in the work request");
            ApplicationCommands.Paste.Execute(null, text);
            await dialog.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(composer.Images.Count() == 1 && text.Text == "Inspect both diagrams and explain the difference.",
                "Pasting a clipboard image keeps the existing work request and adds a thumbnail");

            composer.ProcessData(new DataObject(DataFormats.Bitmap, ImageFixture(40)));
            Check(composer.Images.Count() == 2 && FindAll<Button>(dialog).Count(button => button.Name.StartsWith("CliTaskImagePreview_")) == 2,
                "Multiple images remain together above the work request");
            Check(composer.Images.All(image => image.MediaType == "image/png" && image.Data.Length > 8),
                "Clipboard images are captured as complete PNG snapshots");

            var opened = ExerciseImagePreview(dialog, preview =>
            {
                var before = preview.ImageTransform;
                var focalPoint = new Point(140, 100);
                var inverse = before; inverse.Invert();
                var imagePoint = inverse.Transform(focalPoint);
                preview.ZoomAt(focalPoint, 2);
                var after = preview.ImageTransform;
                var anchored = after.Transform(imagePoint);
                Check(after.M11 > before.M11 && (anchored - focalPoint).Length < 0.01,
                    "Preview zoom keeps the image point under the mouse fixed");
                preview.BeginPan(new Point(20, 30)); preview.PanTo(new Point(65, 80)); preview.EndPan();
                var released = preview.ImageTransform;
                preview.PanTo(new Point(120, 140));
                Check(Math.Abs(released.OffsetX - after.OffsetX - 45) < 0.01 &&
                    Math.Abs(released.OffsetY - after.OffsetY - 50) < 0.01 && preview.ImageTransform == released,
                    "Dragging pans the preview and releasing leaves the image fixed");
                var source = PresentationSource.FromVisual(preview)!;
                preview.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                if (preview.IsVisible) preview.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                Check(!preview.IsVisible && dialog.IsVisible, "Escape closes only the image preview and preserves the task draft");
            });
            Press(dialog, "CliTaskImagePreview_1"); await opened;
            Find<Button>(dialog, "CliTaskImagePreview_2").Focus();
            await dialog.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(Find<Button>(dialog, "CliTaskImageRemove_2").IsVisible,
                "The thumbnail delete action is accessible when the image receives keyboard focus");
            Press(dialog, "CliTaskImageRemove_2");
            Check(composer.Images.Count() == 1 && text.Text.StartsWith("Inspect both diagrams"),
                "Removing a thumbnail removes only that image and keeps the text");
            composer.ProcessData(new DataObject(DataFormats.Bitmap, ImageFixture(80)));
            var pathData = new DataObject(DataFormats.UnicodeText, Path.Combine(root, "reference.pdf"));
            Check(!composer.ProcessData(pathData), "Plain-text PDF paths retain normal text paste behavior");
            var originalText = text.Text;
            text.Select(text.Text.Length, 0); Clipboard.SetText(" " + Path.Combine(root, "reference.pdf"));
            ApplicationCommands.Paste.Execute(null, text);
            Check(text.Text == originalText + " " + Path.Combine(root, "reference.pdf"),
                "The custom image paste command still inserts ordinary text and file paths");
            text.Text = originalText;
            var pdf = Path.Combine(root, "unsupported.pdf"); File.WriteAllText(pdf, "%PDF-fixture");
            var answer = AnswerMessage(dialog, true);
            composer.ProcessData(new DataObject(DataFormats.FileDrop, new[] { pdf }));
            await answer;
            Check(composer.Images.Count() == 2 && text.Text.StartsWith("Inspect both diagrams"),
                "Unsupported document input opens a themed notice and preserves the full draft");
            var screenshot = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            screenshot.Render(dialog);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using (var stream = File.Create(Path.Combine(root, "image-composer.png"))) encoder.Save(stream);
            Press(dialog, "SendCliTask"); request = dialog.Result;
        });
        Check(request is { Images.Length: 2 } && request.Prompt == "Inspect both diagrams and explain the difference.",
            "The task dialog submits the text and both remaining images as one request");
        var created = station.CreateCliHandoff(request!).Task;
        Check(created.Images is { Length: 2 } && created.Images.All(image => File.Exists(image.Path)),
            "Creating the task stores every image beside the task artifacts");
        var window = new MainWindow(station) { Owner = owner }; window.Show();
        await Wait(() => ((TextBlock)window.FindName("StatusText")).Text != "Working…");
        SelectTask(window, created.TaskId);
        Check(FindAll<Button>(window).Count(button => button.Name == "PreviewTaskImage") == 2,
            "Task details keep both saved images available after the composer closes");
        var taskPreview = ExerciseImagePreview(window, preview =>
        {
            Press(preview, "ImagePreviewClose");
            Check(!preview.IsVisible && window.IsVisible, "The preview close button returns to the task details");
        });
        FindAll<Button>(window).First(button => button.Name == "PreviewTaskImage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await taskPreview;
        File.Delete(created.Images![0].Path);
        await Click(window, "Refresh"); SelectTask(window, created.TaskId);
        Check(FindAll<TextBlock>(window).Any(block => block.Name == "TaskImageUnavailable") &&
            FindAll<Button>(window).Count(button => button.Name == "PreviewTaskImage") == 1,
            "A missing image is reported without hiding the task or its remaining image");
        window.RequestExit();

        var errors = new List<string>();
        var standalone = new ImageComposer(owner, "en", errors.Add);
        standalone.PromptTextBox.Text = "Keep this request";
        standalone.ProcessData(new DataObject(DataFormats.Bitmap, bitmap));
        var mixedPng = Path.Combine(root, "mixed-image.png");
        File.WriteAllBytes(mixedPng, standalone.Images.First().Data);
        standalone.ProcessData(new DataObject(DataFormats.FileDrop, new[] { mixedPng, Path.Combine(root, "unsupported.pdf") }));
        Check(errors.Count == 1 && standalone.Images.Count() == 1 && standalone.PromptTextBox.Text == "Keep this request",
            "Mixed image and document input is rejected atomically without losing the earlier draft");
        var corrupt = Path.Combine(root, "broken.png"); File.WriteAllText(corrupt, "not an image");
        standalone.ProcessData(new DataObject(DataFormats.FileDrop, new[] { corrupt }));
        Check(errors.Count == 2 && standalone.Images.Count() == 1,
            "An unreadable image produces a visible input error without a partial attachment");
    }
}
