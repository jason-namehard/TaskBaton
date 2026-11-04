using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

/// <summary>A work request with in-memory image attachments.</summary>
public sealed class ImageComposer : Border
{
    private const string Unsupported = "Only images can be attached here. For PDF, video, audio, or other files, paste the full file path into your work request.";
    private const string Unreadable = "The image could not be opened. Check the file and try again.";
    private readonly Window owner;
    private readonly string language;
    private readonly Action<string> reportError;
    private readonly StackPanel thumbnails;
    private readonly ScrollViewer thumbnailRow;
    private readonly List<Attachment> attachments = [];
    private int nextImageId;

    public TextBox PromptTextBox { get; }
    public ImageInput[] Images => attachments.Select(item => item.Input).ToArray();
    public event EventHandler? AttachmentsChanged;

    public ImageComposer(Window owner, string language, Action<string> reportError)
    {
        this.owner = owner;
        this.language = language;
        this.reportError = reportError;
        Name = "CliTaskImageComposer";
        AutomationProperties.SetAutomationId(this, Name);
        CornerRadius = new(7);
        BorderThickness = new(1);
        Margin = new(0, 3, 0, 10);
        AllowDrop = true;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(BorderBrushProperty, "BorderStrongBrush");
        IsKeyboardFocusWithinChanged += (_, _) => SetResourceReference(BorderBrushProperty,
            IsKeyboardFocusWithin ? "FocusBrush" : "BorderStrongBrush");

        var body = new DockPanel();
        thumbnails = new StackPanel { Orientation = Orientation.Horizontal };
        thumbnailRow = new ScrollViewer
        {
            Name = "CliTaskImageRow", Content = thumbnails, Visibility = Visibility.Collapsed,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new(8, 8, 8, 2), MaxHeight = 100
        };
        AutomationProperties.SetAutomationId(thumbnailRow, thumbnailRow.Name);
        DockPanel.SetDock(thumbnailRow, Dock.Top);
        body.Children.Add(thumbnailRow);

        PromptTextBox = EntryDialogLayout.Input("CliTaskPrompt", multiline: true);
        PromptTextBox.Height = 160;
        PromptTextBox.Margin = new(0);
        PromptTextBox.BorderThickness = new(0);
        PromptTextBox.Background = Brushes.Transparent;
        PromptTextBox.AllowDrop = true;
        body.Children.Add(PromptTextBox);
        Child = body;

        // A TextBox normally disables Paste when the clipboard has an image but no text.
        PromptTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) =>
        {
            PasteClipboard();
            e.Handled = true;
        }, (_, e) =>
        {
            e.CanExecute = PromptTextBox.IsEnabled && !PromptTextBox.IsReadOnly;
            e.Handled = true;
        }));
        PromptTextBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0 ||
                (Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;
            PasteClipboard();
            e.Handled = true;
        };
        DataObject.AddPastingHandler(PromptTextBox, (_, e) =>
        {
            if (ProcessData(e.DataObject)) e.CancelCommand();
        });
        PreviewDragOver += (_, e) =>
        {
            if (HasTextOnly(e.Data)) return;
            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewDrop += (_, e) =>
        {
            if (ProcessData(e.Data)) e.Handled = true;
        };
    }

    private string T(string text) => UiText.Translate(text, language);

    private void PasteClipboard()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is not null && !ProcessData(data)) PromptTextBox.Paste();
        }
        catch (ExternalException) { reportError(T("Clipboard is unavailable. Try again.")); }
    }

    /// <summary>Imports an image or reports an unsupported input; returns false for ordinary text.</summary>
    public bool ProcessData(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths || paths.Any(Directory.Exists))
                {
                    reportError(T(Unsupported));
                    return true;
                }
                var pending = new List<(ImageInput Input, BitmapSource Preview)>();
                foreach (var path in paths)
                {
                    try
                    {
                        using var file = File.OpenRead(path);
                        var bitmap = Decode(file);
                        pending.Add(MakeAttachment(Path.GetFileNameWithoutExtension(path) + ".png", bitmap));
                    }
                    catch (NotSupportedException)
                    {
                        reportError(T(Unsupported));
                        return true;
                    }
                    catch (FileFormatException)
                    {
                        reportError(T(IsImageExtension(path) ? Unreadable : Unsupported));
                        return true;
                    }
                }
                AddAttachments(pending);
                return true;
            }

            foreach (var format in new[] { "PNG", "image/png" })
            {
                if (!data.GetDataPresent(format, false)) continue;
                var raw = data.GetData(format, false);
                var bytes = raw switch
                {
                    byte[] array => array,
                    MemoryStream stream => stream.ToArray(),
                    _ => throw new FileFormatException()
                };
                AddAttachments([MakeAttachment("image.png", DecodeImage(bytes))]);
                return true;
            }
            if (data.GetDataPresent(DataFormats.Bitmap))
            {
                if (data.GetData(DataFormats.Bitmap) is not BitmapSource bitmap) throw new FileFormatException();
                AddAttachments([MakeAttachment("image.png", bitmap)]);
                return true;
            }
            if (HasTextOnly(data)) return false;
            reportError(T(Unsupported));
            return true;
        }
        catch (ExternalException) { reportError(T("Clipboard is unavailable. Try again.")); }
        catch (FileFormatException) { reportError(T(Unreadable)); }
        catch (NotSupportedException) { reportError(T(Unreadable)); }
        catch (IOException) { reportError(T(Unreadable)); }
        catch (UnauthorizedAccessException) { reportError(T(Unreadable)); }
        return true;
    }

    private static bool HasTextOnly(IDataObject data) =>
        !data.GetDataPresent(DataFormats.FileDrop) && !data.GetDataPresent(DataFormats.Bitmap) &&
        !data.GetDataPresent("PNG", false) && !data.GetDataPresent("image/png", false) &&
        (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text));

    private static bool IsImageExtension(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".ico" or ".wdp" or ".jxr" or ".hdp";

    private static (ImageInput Input, BitmapSource Preview) MakeAttachment(string fileName, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        var data = output.ToArray();
        return (new ImageInput(fileName, "image/png", data), DecodeImage(data));
    }

    /// <summary>Loads an image without retaining the supplied buffer or a file handle.</summary>
    public static BitmapSource DecodeImage(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        return Decode(input);
    }

    private static BitmapSource Decode(Stream input)
    {
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private void AddAttachments(IEnumerable<(ImageInput Input, BitmapSource Preview)> incoming)
    {
        foreach (var (input, preview) in incoming)
        {
            var item = new Attachment(++nextImageId, input, preview);
            attachments.Add(item);
            thumbnails.Children.Add(CreateThumbnail(item));
        }
        thumbnailRow.Visibility = attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttachmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private FrameworkElement CreateThumbnail(Attachment item)
    {
        var tile = new Grid { Width = 80, Height = 80, Margin = new(0, 0, 8, 0) };
        var preview = new Button
        {
            Name = "CliTaskImagePreview_" + item.Id, Width = 72, Height = 72, MinHeight = 0,
            Padding = new(0), Margin = new(0, 6, 6, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new Image { Source = item.Preview, Stretch = Stretch.UniformToFill, Width = 70, Height = 70 }
        };
        var previewName = string.Format(T("Preview image: {0}"), item.Input.FileName);
        AutomationProperties.SetAutomationId(preview, preview.Name);
        AutomationProperties.SetName(preview, previewName);
        preview.ToolTip = previewName;
        preview.Click += (_, _) => new ImagePreviewWindow(owner, item.Preview, language).ShowDialog();
        tile.Children.Add(preview);

        var remove = new Button
        {
            Name = "CliTaskImageRemove_" + item.Id, Width = 23, Height = 23, MinHeight = 0,
            Padding = new(0), Margin = new(0), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
            Content = Icons.Create("Close", size: 12)
        };
        remove.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        remove.SetResourceReference(BorderBrushProperty, "BorderStrongBrush");
        var removeName = string.Format(T("Remove image: {0}"), item.Input.FileName);
        AutomationProperties.SetAutomationId(remove, remove.Name);
        AutomationProperties.SetName(remove, removeName);
        remove.ToolTip = removeName;
        remove.Click += (_, _) =>
        {
            attachments.Remove(item);
            thumbnails.Children.Remove(tile);
            thumbnailRow.Visibility = attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AttachmentsChanged?.Invoke(this, EventArgs.Empty);
        };
        tile.Children.Add(remove);
        void UpdateRemove() => remove.Visibility = tile.IsMouseOver || tile.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
        tile.MouseEnter += (_, _) => UpdateRemove();
        tile.MouseLeave += (_, _) => UpdateRemove();
        tile.IsKeyboardFocusWithinChanged += (_, _) => UpdateRemove();
        return tile;
    }

    private sealed record Attachment(int Id, ImageInput Input, BitmapSource Preview);
}
