using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

internal sealed class ToastNotificationWindow : Window
{
    internal ToastNotificationWindow(Window owner,DesktopNotificationContent content,string channel,string closeLabel,
        string? actionLabel,Action? openTask)
    {
        Brush Brush(string key)=>(Brush)owner.FindResource(key);
        Name="InAppNotification";Title=channel;Width=410;SizeToContent=SizeToContent.Height;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        AllowsTransparency=true;Background=Brushes.Transparent;FontFamily=owner.FontFamily;FontSize=13;
        Resources=owner.Resources;UseLayoutRounding=true;
        AutomationProperties.SetAutomationId(this,Name);

        var card=new Border{Name="NotificationCard",Margin=new(10),Padding=new(16),CornerRadius=new(14),
            Background=Brush("SurfaceBrush"),BorderBrush=Brush("BorderBrush"),BorderThickness=new(1),
            Effect=new DropShadowEffect{Color=(Color)owner.FindResource("ShadowColor"),Opacity=(double)owner.FindResource("ShadowOpacity"),BlurRadius=20,ShadowDepth=4,Direction=270}};
        AutomationProperties.SetAutomationId(card,card.Name);Content=card;
        var body=new Grid();card.Child=body;
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});

        var header=new Grid{Name="NotificationHeader"};
        AutomationProperties.SetAutomationId(header,header.Name);body.Children.Add(header);
        header.ColumnDefinitions.Add(new(){Width=new(34)});
        header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new(){Width=new(26)});
        var icon=new Border{Width=30,Height=30,CornerRadius=new(10),Background=Brush("AccentSoftBrush"),VerticalAlignment=VerticalAlignment.Top};
        icon.Child=Icons.Create("Bell",size:17,brushKey:"AccentInkBrush");
        header.Children.Add(icon);
        var titles=new StackPanel{Margin=new(8,0,8,0)};Grid.SetColumn(titles,1);header.Children.Add(titles);
        titles.Children.Add(new TextBlock{Text="TaskBaton",FontSize=11.5,Foreground=Brush("FaintBrush"),Margin=new(0,0,0,2)});
        const string brand="TaskBaton · ";
        var title=new TextBlock{Name="NotificationTitle",Text=content.Title.StartsWith(brand,StringComparison.Ordinal)?content.Title[brand.Length..]:content.Title,
            FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Brush("TextBrush"),TextWrapping=TextWrapping.Wrap};
        AutomationProperties.SetAutomationId(title,title.Name);titles.Children.Add(title);
        var close=new Button{Name="CloseInAppNotification",Content=Icons.Create("Close",size:13),Width=26,Height=26,MinHeight=26,
            Padding=new(0),Margin=new(0),Foreground=Brush("MutedBrush"),
            Background=Brushes.Transparent,BorderThickness=new(0),VerticalAlignment=VerticalAlignment.Top,ToolTip=closeLabel};
        AutomationProperties.SetAutomationId(close,close.Name);AutomationProperties.SetName(close,closeLabel);
        close.Template=CloseTemplate(Brush("SurfaceAltBrush"),Brush("FocusBrush"));
        close.Click+=(_,_)=>Close();Grid.SetColumn(close,2);header.Children.Add(close);

        var message=new TextBlock{Name="NotificationMessage",Text=content.Message,TextWrapping=TextWrapping.Wrap,
            Foreground=Brush("TextBrush"),LineHeight=20};
        AutomationProperties.SetAutomationId(message,message.Name);
        var scroll=new ScrollViewer{Content=message,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new(0,12,0,0),Padding=new(0,0,3,0)};
        Grid.SetRow(scroll,1);body.Children.Add(scroll);

        var footer=new Grid{Margin=new(0,12,0,0)};Grid.SetRow(footer,2);body.Children.Add(footer);
        footer.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        footer.Children.Add(new TextBlock{Name="NotificationChannel",Text=channel,FontSize=10,Foreground=Brush("MutedBrush"),
            VerticalAlignment=VerticalAlignment.Center});
        if(openTask is not null)
        {
            var open=new Button{Name="OpenNotificationTask",Content=actionLabel,Style=(Style)owner.FindResource("PrimaryButtonStyle"),
                Margin=new(8,0,0,0),MinHeight=30,Padding=new(12,4,12,4),FontSize=12};
            AutomationProperties.SetAutomationId(open,open.Name);
            open.Click+=(_,_)=>{openTask();Close();};Grid.SetColumn(open,1);footer.Children.Add(open);
        }
        Loaded+=(_,_)=>{Left=SystemParameters.WorkArea.Right-ActualWidth-8;Top=SystemParameters.WorkArea.Bottom-ActualHeight-8;};
    }

    private static ControlTemplate CloseTemplate(Brush hover,Brush focus)
    {
        var frame=new FrameworkElementFactory(typeof(Border),"CloseFrame");
        frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(13));
        frame.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        frame.SetValue(Border.BorderBrushProperty,Brushes.Transparent);frame.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
        frame.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=frame};
        var mouse=new Trigger{Property=IsMouseOverProperty,Value=true};
        mouse.Setters.Add(new Setter(Border.BackgroundProperty,hover,"CloseFrame"));template.Triggers.Add(mouse);
        var keyboard=new Trigger{Property=IsKeyboardFocusedProperty,Value=true};
        keyboard.Setters.Add(new Setter(Border.BorderBrushProperty,focus,"CloseFrame"));template.Triggers.Add(keyboard);
        return template;
    }
}
