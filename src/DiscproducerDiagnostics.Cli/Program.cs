using System.Globalization;
using DiscproducerDiagnostics.Cli;
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture=CultureInfo.InvariantCulture;
using var cancellation=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
return CliApplication.Run(args,Console.Out,Console.Error,cancellation.Token);
