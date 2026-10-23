using System.IO;
using System.Text.Json;
using System.Windows;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Usb;
namespace DiscproducerDiagnostics.App;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--worker"))
        {
            using var input = new StreamReader(Console.OpenStandardInput());
            using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            WorkerReply reply;
            try { reply = NativeWorker.Execute(JsonSerializer.Deserialize<WorkerRequest>(input.ReadLine() ?? "") ?? throw new InvalidDataException("Missing request"), t => output.WriteLine(JsonSerializer.Serialize(new WorkerReply(Progress: t)))); }
            catch (Exception e) { reply = new(Error: e.Message); }
            output.WriteLine(JsonSerializer.Serialize(reply));
            return;
        }
        if (args.Contains("--ui-self-test")) { UiSmokeTest.Run(); return; }
        var app = new Application();
        app.Run(new MainWindow());
    }
}
