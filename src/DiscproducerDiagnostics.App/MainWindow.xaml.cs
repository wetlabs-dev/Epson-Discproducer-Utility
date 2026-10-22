using System.Windows;
namespace DiscproducerDiagnostics.App;
public partial class MainWindow : Window
{
    private readonly MainViewModel model = new();
    public MainWindow() { InitializeComponent(); DataContext = model; Closed += (_, _) => model.Dispose(); }
}
