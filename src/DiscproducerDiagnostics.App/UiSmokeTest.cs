using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace DiscproducerDiagnostics.App;
internal static class UiSmokeTest
{
    public static void Run()
    {
        var app=new Application();var window=new MainWindow();int exit=0;
        window.Loaded+=async (_,_)=>
        {
            try
            {
                var model=(MainViewModel)window.DataContext;await model.VerifySimulatedUiAsync();
                if(window.Content is Grid grid)
                {
                    var tabs=grid.Children.OfType<TabControl>().Single();
                    for(int i=0;i<tabs.Items.Count;i++){tabs.SelectedIndex=i;window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
                }
                using var output=new StreamWriter(Console.OpenStandardOutput()){AutoFlush=true};output.WriteLine("All tabs and simulated identification/status/inspection path passed; no hardware queried.");
            }
            catch(Exception e){exit=1;using var error=new StreamWriter(Console.OpenStandardError()){AutoFlush=true};error.WriteLine(e);}
            finally{window.Close();}
        };
        app.Run(window);Environment.ExitCode=exit;
    }
}
