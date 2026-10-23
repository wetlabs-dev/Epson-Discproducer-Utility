using System.Text.Json;
// Isolated process fixture: no native APIs or hardware access.
using var request = JsonDocument.Parse(Console.ReadLine() ?? "{}");
string operation = request.RootElement.GetProperty("Operation").GetString() ?? "";
if(operation == "partial")
{
    Console.WriteLine("{\"Progress\":{\"Id\":\"12345678-1234-1234-1234-123456789012\",\"TimestampUtc\":\"2026-01-01T00:00:00Z\",\"DeviceId\":\"fixture\",\"Transport\":\"fixture\",\"Interface\":\"fixture\",\"Endpoint\":\"fixture\",\"Command\":\"completed-step\",\"Request\":\"\",\"Response\":\"AQID\",\"Outcome\":\"Success\",\"Interpretation\":\"fixture\",\"Provenance\":\"test\"}}");
    await Console.Out.FlushAsync(); await Task.Delay(Timeout.InfiniteTimeSpan);
}
else if(operation == "malformed") Console.WriteLine("not json");
else if(operation == "duplicate-final") {Console.WriteLine("{}");Console.WriteLine("{}");}
else if (operation == "hang") await Task.Delay(Timeout.InfiniteTimeSpan);
else if (operation == "crash") Environment.Exit(7);
else Console.WriteLine("{}");
