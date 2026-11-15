using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public sealed class ConnectionGraph : Viewbox
{
    public ConnectionGraph(IReadOnlyList<AgentRecord> agents,IReadOnlyList<ConnectionView> edges,Action<ConnectionView> select,string language,Window owner,
        Func<string,string>? displayName=null,IReadOnlyDictionary<string,CliExtensionRecord>? extensions=null,Func<string,string>? avatarKey=null)
    {
        string Name(string id)=>displayName is null?id:displayName(id);
        Brush B(string key)=>(Brush)owner.FindResource(key);
        Brush Color(ConnectionView edge)=>edge.Display switch
        {
            "Available" or "Allowed"=>B("AllowedBrush"),"Unavailable"=>B("UnavailableBrush"),
            // Older ledgers may still carry Denied; the graph shows it as unavailable but never pauses the route.
            "Denied"=>B("UnavailableBrush"),
            _=>throw new ArgumentOutOfRangeException(nameof(edges),edge.Display,"Unknown connection display state.")
        };
        Stretch=Stretch.Uniform;StretchDirection=StretchDirection.Both;HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(this,"ConnectionGraph");
        var body=new StackPanel{Width=820};Child=body;
        var canvas=new Canvas{Width=820,Height=420,Background=Brushes.Transparent};body.Children.Add(canvas);
        var ordered=agents.OrderBy(a=>a.AgentId,StringComparer.Ordinal).ToArray();
        var directions=edges.ToDictionary(e=>(e.From,e.To));
        var positions=ordered.Select((a,i)=>(a.AgentId,Point:new Point(410+300*Math.Cos(-Math.PI/2+2*Math.PI*i/ordered.Length),210+165*Math.Sin(-Math.PI/2+2*Math.PI*i/ordered.Length))))
            .ToDictionary(p=>p.AgentId,p=>p.Point);
        if(ordered.Length==0)canvas.Children.Add(new TextBlock{Text=UiText.Translate("Register agents to view directed connections.",language),Margin=new(20),Foreground=B("MutedBrush")});
        for(var i=0;i<ordered.Length;i++)
        for(var j=i+1;j<ordered.Length;j++)
        {
            var from=ordered[i].AgentId;var to=ordered[j].AgentId;
            var forward=directions[(from,to)];var reverse=directions[(to,from)];
            var hasForward=forward.Display!="Unavailable";var hasReverse=reverse.Display!="Unavailable";
            var a=positions[from];var b=positions[to];var direction=b-a;direction.Normalize();
            var start=NodeBoundary(a,b-a);var end=NodeBoundary(b,a-b);
            var midpoint=new Point((start.X+end.X)/2,(start.Y+end.Y)/2);
            var tooltip=$"{Name(from)} → {Name(to)}: {GraphText.State(forward.Display,language)}\n{Name(to)} → {Name(from)}: {GraphText.State(reverse.Display,language)}";
            var hit=new System.Windows.Shapes.Path{Data=new LineGeometry(start,end),Stroke=Brushes.Transparent,StrokeThickness=18,
                Cursor=Cursors.Hand,ToolTip=tooltip,Tag=forward,Focusable=true};
            AutomationProperties.SetAutomationId(hit,$"ConnectionPair:{from}:{to}");
            AutomationProperties.SetName(hit,tooltip);
            hit.MouseLeftButtonDown+=(_,e)=>{select(forward);e.Handled=true;};
            hit.KeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){select(forward);e.Handled=true;}};
            canvas.Children.Add(hit);
            void Stroke(Point first,Point last,Brush color,string id,bool dashed=false)
            {
                var line=new System.Windows.Shapes.Path{Data=new LineGeometry(first,last),Stroke=color,StrokeThickness=2,IsHitTestVisible=false,
                    StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
                if(dashed)line.StrokeDashArray=new DoubleCollection([3,3]);
                AutomationProperties.SetAutomationId(line,id);canvas.Children.Add(line);
            }
            if(hasForward&&hasReverse)
            {
                Stroke(start,midpoint,Color(reverse),$"ConnectionStroke:{to}:{from}");
                Stroke(midpoint,end,Color(forward),$"ConnectionStroke:{from}:{to}");
            }
            else if(hasForward)Stroke(start,end,Color(forward),$"ConnectionStroke:{from}:{to}");
            else if(hasReverse)Stroke(start,end,Color(reverse),$"ConnectionStroke:{to}:{from}");
            else Stroke(start,end,B("UnavailableBrush"),$"ConnectionStroke:{from}:{to}",true);
            if(hasForward)AddArrow(canvas,end,direction,Color(forward),$"ConnectionArrow:{from}:{to}");
            if(hasReverse)AddArrow(canvas,start,-direction,Color(reverse),$"ConnectionArrow:{to}:{from}");
        }
        foreach(var agent in ordered)
        {
            var p=positions[agent.AgentId];var name=Name(agent.AgentId);
            var node=new Border{Width=164,Height=52,Padding=new(10,0,10,0),Background=B("SurfaceBrush"),
                BorderBrush=B("BorderStrongBrush"),BorderThickness=new(1),CornerRadius=new(12),ToolTip=$"{name}\n{agent.AgentId}"};
            AutomationProperties.SetAutomationId(node,$"ConnectionNode:{agent.AgentId}");
            var content=new DockPanel{VerticalAlignment=VerticalAlignment.Center};
            var avatar=new Border{Width=26,Height=26,CornerRadius=new(8),Background=B(avatarKey?.Invoke(agent.AgentId)??"Avatar1Brush"),Margin=new(0,0,9,0),
                Child=new TextBlock{Text=name.Length==0?"?":char.ToUpperInvariant(name.TrimStart()[0]).ToString(),FontSize=12,FontWeight=FontWeights.Bold,
                    Foreground=B("AvatarTextBrush"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            DockPanel.SetDock(avatar,Dock.Left);content.Children.Add(avatar);
            var cli=extensions?.GetValueOrDefault(agent.AgentId);
            var state=cli is null?"cli-missing":cli.CreateProbe is {MachinePassed:false}?"cli-failed":cli.CanCreate?"cli-ready":"cli-registered";
            var indicator=new Ellipse{Width=8,Height=8,Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,
                Fill=B(state=="cli-missing"?"UnavailableBrush":state=="cli-failed"?"DeniedBrush":"AllowedBrush"),ToolTip=GraphText.Legend(state,language)};
            AutomationProperties.SetAutomationId(indicator,$"CliIndicator:{agent.AgentId}");
            AutomationProperties.SetName(indicator,GraphText.Legend(state,language));
            DockPanel.SetDock(indicator,Dock.Right);content.Children.Add(indicator);
            var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            names.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=B("TextBrush")});
            names.Children.Add(new TextBlock{Text=GraphText.Legend("node-"+state,language),FontSize=11.5,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=B("FaintBrush")});
            content.Children.Add(names);
            node.Child=content;
            Canvas.SetLeft(node,p.X-82);Canvas.SetTop(node,p.Y-26);Panel.SetZIndex(node,2);canvas.Children.Add(node);
        }
        var legend=new WrapPanel{Margin=new(8,10,8,0),HorizontalAlignment=HorizontalAlignment.Center};
        AutomationProperties.SetAutomationId(legend,"ConnectionLegend");body.Children.Add(legend);
        foreach(var kind in new[]{"single","double","none"})
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,22,4)};
            AutomationProperties.SetAutomationId(row,"ConnectionLegend"+char.ToUpperInvariant(kind[0])+kind[1..]);
            var sample=new Canvas{Width=44,Height=20,Margin=new(0,0,8,0)};
            var color=kind=="none"?B("UnavailableBrush"):B("AllowedBrush");
            var line=new Line{X1=6,Y1=10,X2=38,Y2=10,Stroke=color,StrokeThickness=2};
            if(kind=="none")line.StrokeDashArray=new DoubleCollection([3,3]);sample.Children.Add(line);
            if(kind!="none")AddArrow(sample,new Point(38,10),new Vector(1,0),color);
            if(kind=="double")AddArrow(sample,new Point(6,10),new Vector(-1,0),color);
            row.Children.Add(sample);row.Children.Add(new TextBlock{Text=GraphText.Legend(kind,language),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Foreground=B("MutedBrush")});
            legend.Children.Add(row);
        }
    }

    private static void AddArrow(Canvas canvas,Point tip,Vector direction,Brush color,string? id=null)
    {
        var side=new Vector(-direction.Y,direction.X);
        var arrow=new Polygon{Points=new PointCollection([tip,tip-direction*10+side*4,tip-direction*10-side*4]),Fill=color,IsHitTestVisible=false,StrokeLineJoin=PenLineJoin.Round};
        if(id is not null)AutomationProperties.SetAutomationId(arrow,id);
        canvas.Children.Add(arrow);
    }

    private static Point NodeBoundary(Point center,Vector direction)
    {
        var scale=Math.Min(90/Math.Abs(direction.X),33/Math.Abs(direction.Y));
        return center+direction*scale;
    }
}
