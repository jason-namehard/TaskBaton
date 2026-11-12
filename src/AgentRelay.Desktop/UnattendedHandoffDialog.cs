using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public sealed class UnattendedHandoffDialog : Window
{
    public (int Sends, int Minutes) Limits { get; private set; }

    public UnattendedHandoffDialog(Window owner, string language, int sendLimit)
    {
        var ui = new EntryDialogLayout(this, owner, UiText.Translate("Start unattended run", language), language);
        Width = 540;
        Height = 470;
        ui.Body.Children.Add(ui.Note("Enable Minimize to tray when closing and keep the PC awake. Relay can send only while it is running and Windows is awake."));
        ui.Body.Children.Add(ui.Note("The limits apply to new automatic sends. A turn already sent keeps its own deadline. Balance, charges and quota are unknown."));
        var sends = ui.Field(ui.Body, "Maximum automatic sends", "UnattendedSendLimit", sendLimit.ToString(CultureInfo.InvariantCulture));
        var duration = ui.Field(ui.Body, "Run duration (minutes)", "UnattendedMinutes", "480");

        int ReadLimit(TextBox field, int maximum) => int.TryParse(field.Text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) && value >= 1 && value <= maximum ? value :
            throw new RelayException("unattended_limits", string.Format(CultureInfo.CurrentUICulture,
                ui.T("Enter a whole number from {0} to {1}."), 1, maximum));

        ui.Button(ui.Actions, "Start unattended run", "ConfirmUnattendedRun", () =>
        {
            Limits = (ReadLimit(sends, RelayService.MaxUnattendedSends), ReadLimit(duration, RelayService.MaxUnattendedMinutes));
            DialogResult = true;
        }, primary: true);
        ui.Button(ui.Actions, "Cancel", "CancelUnattendedRun", () => DialogResult = false).IsCancel = true;
    }
}
