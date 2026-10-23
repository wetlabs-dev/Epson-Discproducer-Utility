using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiscproducerDiagnostics.Diagnostics;
namespace DiscproducerDiagnostics.App;
public sealed class TimelineStrip : FrameworkElement
{
    public static readonly DependencyProperty EventsProperty=DependencyProperty.Register(nameof(Events),typeof(IEnumerable<TimelineEvent>),typeof(TimelineStrip),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender,Changed));
    public static readonly DependencyProperty SelectedProperty=DependencyProperty.Register(nameof(Selected),typeof(TimelineEvent),typeof(TimelineStrip),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender|FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public IEnumerable<TimelineEvent>? Events {get=>(IEnumerable<TimelineEvent>?)GetValue(EventsProperty);set=>SetValue(EventsProperty,value);}
    public TimelineEvent? Selected {get=>(TimelineEvent?)GetValue(SelectedProperty);set=>SetValue(SelectedProperty,value);}
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {var control=(TimelineStrip)d;if(e.OldValue is INotifyCollectionChanged old)old.CollectionChanged-=control.CollectionChanged;if(e.NewValue is INotifyCollectionChanged next)next.CollectionChanged+=control.CollectionChanged;}
    private void CollectionChanged(object? sender,NotifyCollectionChangedEventArgs e)=>InvalidateVisual();
    private (TimelineEvent Event,double X)[] Points()
    {
        var events=(Events??[]).Where(e=>e.DisplayTime.HasValue).Take(10000).ToArray();if(events.Length==0)return [];
        double start=events.Min(e=>e.DisplayTime!.Value.UtcTicks),span=Math.Max(1,events.Max(e=>e.DisplayTime!.Value.UtcTicks)-start);
        return events.Select(e=>(e,12+(e.DisplayTime!.Value.UtcTicks-start)/span*Math.Max(1,ActualWidth-24))).ToArray();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.White,null,new Rect(RenderSize));dc.DrawLine(new Pen(Brushes.SlateGray,1),new(12,ActualHeight/2),new(Math.Max(12,ActualWidth-12),ActualHeight/2));
        foreach(var p in Points()){var brush=p.Event.Type.Contains("error",StringComparison.OrdinalIgnoreCase)||p.Event.Type=="Transfer failure"?Brushes.Firebrick:p.Event.Provenance.StartsWith("User")?Brushes.DarkOrange:Brushes.SteelBlue;dc.DrawEllipse(brush,p.Event==Selected?new Pen(Brushes.Black,2):null,new Point(p.X,ActualHeight/2),p.Event==Selected?6:3, p.Event==Selected?6:3);}
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);double x=e.GetPosition(this).X;var nearest=Points().OrderBy(p=>Math.Abs(p.X-x)).FirstOrDefault();if(nearest.Event!=null)SetCurrentValue(SelectedProperty,nearest.Event);
    }
}
