using System.Text.Json;
// Isolated process fixture: no native APIs or hardware access.
using var request = JsonDocument.Parse(Console.ReadLine() ?? "{}");
string operation = request.RootElement.GetProperty("Operation").GetString() ?? "";
if (operation == "hang") await Task.Delay(Timeout.InfiniteTimeSpan);
else if (operation == "crash") Environment.Exit(7);
else Console.WriteLine("{}");
