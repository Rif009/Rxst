using Avalonia.Controls;
using Avalonia.Interactivity;
namespace XstReader.App;
public partial class AttachmentCard : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> SaveRequestedEvent = RoutedEvent.Register<AttachmentCard, RoutedEventArgs>(nameof(SaveRequested), RoutingStrategies.Bubble);
    public event EventHandler<RoutedEventArgs> SaveRequested { add => AddHandler(SaveRequestedEvent, value); remove => RemoveHandler(SaveRequestedEvent, value); }
    public AttachmentCard() => InitializeComponent();
    private void SaveClicked(object? sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(SaveRequestedEvent));
}
