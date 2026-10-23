using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using DiscproducerDiagnostics.Protocols;
using DiscproducerDiagnostics.Usb;
namespace DiscproducerDiagnostics.App;
public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WorkerClient worker = new(Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable"));
    private readonly DiagnosticSession session = new();
    private IDeviceTransport? transport;
    private CancellationTokenSource? operation;
    private bool busy, connected, simulation;
    private Device? selectedDevice;
    private string connection = "Disconnected • scan to discover Epson USB devices", notice = "Hardware compatibility must be verified on the connected PP-100. Enumeration is not proof of communication.";
    public ObservableCollection<Device> Devices { get; } = [];
    public ObservableCollection<Fact> Facts { get; } = [];
    public ObservableCollection<StatusResult> Statuses { get; } = [];
    public ObservableCollection<LogEntry> Logs { get; } = [];
    public MockScenario[] Scenarios => Enum.GetValues<MockScenario>();
    public MockScenario Scenario { get; set; }
    public string TimeoutSeconds { get; set; } = "5";
    public bool Redact { get; set; } = true;
    public LogEntry? SelectedLog { get; set; }
    public bool CanConfigure => !busy && !connected;
    public bool CanSelect => CanConfigure;
    public bool Simulation
    {
        get => simulation;
        set { if (!CanConfigure) return; simulation = value; Devices.Clear(); SelectedDevice = null; Facts.Clear(); Statuses.Clear(); OnChanged(); }
    }
    public Device? SelectedDevice
    {
        get => selectedDevice;
        set { selectedDevice = value; Facts.Clear(); Statuses.Clear(); if (value != null) AddMetadata(value); ResetInspection(); OnChanged(); CommandManager.InvalidateRequerySuggested(); }
    }
    public string Connection { get => connection; private set { connection = value; OnChanged(); } }
    public string Notice { get => notice; private set { notice = value; OnChanged(); } }
    public ICommand ScanCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ExportCommand { get; }
    public MainViewModel()
    {
        session.EntryAdded += e => Logs.Add(e);
        ScanCommand = new ActionCommand(() => _ = Run(Scan), () => CanConfigure);
        ConnectCommand = new ActionCommand(() => _ = Run(Connect), () => CanConfigure && SelectedDevice != null);
        DisconnectCommand = new ActionCommand(() => _ = Run(Disconnect), () => !busy && connected);
        RefreshCommand = new ActionCommand(() => _ = Run(Refresh), () => !busy && connected);
        CancelCommand = new ActionCommand(() => operation?.Cancel(), () => busy);
        CopyCommand = new ActionCommand(() => { try { if (SelectedLog != null) Clipboard.SetText(JsonSerializer.Serialize(SelectedLog, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception e) { Notice = e.Message; } }, () => SelectedLog != null);
        ExportCommand = new ActionCommand(() => _ = Run(ExportBundle), () => !busy);
        InitializeExploration();
        InitializeResearch();
        InitializeToolkit();
    }
    private async Task Run(Func<CancellationToken,Task> action)
    {
        if (busy) return;
        busy = true; ChangedState();
        using var cancellation = new CancellationTokenSource(); operation = cancellation;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { Step("Collection", "Cancelled", "Completed observations retained; export remains available."); Notice = "Operation cancelled. Any displayed results are timestamped earlier observations."; session.Event("cancel", Notice, false); }
        catch (Exception e) { var error=ErrorClassifier.Classify(e); Notice = $"{error.Category}: {error.Detail} (native {error.NativeCode}; HRESULT {error.HResult}) — {error.Recovery}"; session.Event("failure", Notice, false); }
        finally { busy = false; operation = null; ChangedState(); }
    }
    private void ChangedState() { OnChanged(nameof(CapabilityReport)); OnChanged(nameof(CanConfigure)); OnChanged(nameof(CanSelect)); CommandManager.InvalidateRequerySuggested(); }
    private async Task Scan(CancellationToken token)
    {
        Devices.Clear(); SelectedDevice = null; Statuses.Clear();
        session.Event("enumerate", Simulation ? "Scanning simulated device" : "SetupAPI / Configuration Manager discovery started");
        var d = Simulation ? new Discovery([MockTransport.Device], [], []) : await Discover(token);
        lastDiscovery=d;
        foreach (var device in d.Devices.OrderByDescending(d => d.InterfacePath != null)) Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(d => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(d.InstanceId))) == preferences.SelectedDeviceHash) ?? Devices.FirstOrDefault();
        Connection = $"Disconnected • {Devices.Count} Epson device node/interface(s) found";
        Notice = string.Join("; ", d.Warnings.DefaultIfEmpty("Select the USB printer interface when available. Other Epson nodes support metadata only."));
        session.Event("enumerate", $"Found {Devices.Count} Epson device nodes/interfaces. No USB query has been sent.");
    }
    private async Task<Discovery> Discover(CancellationToken token)
    {
        var r = await RecordedWorker(new("scan"), token);
        lastDiscovery = r.Discovery;
        return r.Discovery ?? throw new IOException($"Discovery unavailable: {r.Error} (code {r.ErrorCode})");
    }
    private async Task Connect(CancellationToken token)
    {
        await OpenTransport(token);
        await Refresh(token);
    }
    private async Task OpenTransport(CancellationToken token)
    {
        var d = SelectedDevice!;
        if (transport != null) { await transport.DisposeAsync(); transport = null; }
        if (d.Simulated) transport = new LoggedTransport(new MockTransport(Scenario), session, d);
        else if (d.InterfacePath != null) transport = new LoggedTransport(new UsbPrinterClassTransport(worker, d.InterfacePath), session, d);
        if (transport != null) await transport.ConnectAsync(token);
        connected = true;
        Connection = d.Simulated ? "SIMULATED connection • no hardware communication" : transport == null ? "Metadata only • USB printer interface unavailable" : "Interface accessible • awaiting request/response verification";
        Notice = transport == null ? "Installed driver exposes no supported USB printer query interface. No driver changes will be made." : "Only registered read-only identification/status IOCTLs are permitted.";
        Step("Interface access", transport?.Connected == true ? "Success" : "Unavailable", Connection);
    }
    private async Task Disconnect(CancellationToken token)
    {
        if (transport != null) { await transport.DisconnectAsync(); await transport.DisposeAsync(); transport = null; }
        connected = false; Connection = "Disconnected • prior observations retained with their timestamps";
    }
    private async Task Refresh(CancellationToken token)
    {
        if (!double.TryParse(TimeoutSeconds, out double seconds) || !double.IsFinite(seconds) || seconds < 0.1 || seconds > 60)
            throw new ArgumentException("Set query timeout between 0.1 and 60 seconds.");
        var d = SelectedDevice!;
        Statuses.Clear(); Facts.Clear(); AddMetadata(d);
        if (!d.Simulated)
        {
            Discovery discovery;
            try { discovery = await Discover(token); }
            catch (Exception e) when (e is IOException or TimeoutException)
            {
                Statuses.Add(new("Windows PnP", DateTimeOffset.Now, "", "Presence could not be checked", "Unavailable", e.Message));
                throw;
            }
            if (!discovery.Devices.Any(x => x.InstanceId == d.InstanceId))
            {
                await Disconnect(token); Connection = "Device disconnected / unavailable";
                Statuses.Add(new("Windows PnP", DateTimeOffset.Now, "Absent", "Device no longer enumerated", "Unavailable")); return;
            }
            foreach (var queue in discovery.Queues)
                Statuses.Add(new($"Spooler: {queue.Name} (NOT correlated to selected USB device)", DateTimeOffset.Now, $"0x{queue.RawStatus:X8}", StatusDecoder.Spooler(queue.RawStatus) + $"; {queue.Jobs} queued jobs", "Uncorrelated Epson candidate queue"));
            foreach (string warning in discovery.Warnings) session.Event("discovery-warning", warning, false);
        }
        if (transport == null) { Statuses.Add(new("USB printer class", DateTimeOffset.Now, "", "Unavailable", "Unsupported interface")); return; }
        var protocol = new PrinterProtocol(transport, TimeSpan.FromSeconds(seconds));
        foreach (var fact in await protocol.GetIdentityAsync(token))
        {
            Facts.Add(fact);
            if (fact.Name == "Identity parsing") session.Event("identity-parse", fact.Value, fact.Value == "Valid fields parsed");
        }
        if (!transport.Connected) { connected = false; Connection = "Device disconnected / unavailable"; return; }
        var status = await protocol.GetStatusAsync(token); Statuses.Add(status);
        connected = transport.Connected;
        bool validIdentity = Facts.Any(f => f.Name == "Model" && f.Source.Contains("IEEE 1284") && f.Value != "Unavailable" && f.Availability == "Reported");
        bool validStatus = status.Availability != "Unavailable";
        Connection = d.Simulated ? "SIMULATED results • not hardware evidence" : !connected ? "Device disconnected / unavailable" : validIdentity || validStatus ? "Driver request/response succeeded • USB wire traffic/caching not independently verified" : "Interface opened • no valid identification/status response obtained";
        if (!validIdentity && !validStatus) Notice = "Queries failed or returned malformed data. Inspect the log for Windows error codes; retain Epson's driver.";
    }
    private void AddMetadata(Device d)
    {
        string source = d.Simulated ? "SIMULATED Windows metadata" : "Windows PnP metadata";
        foreach (var (name,value) in new[] { ("Detected name",d.Name),("Manufacturer (metadata)",d.Manufacturer),("VID",d.Vid),("PID",d.Pid),("Device instance ID",d.InstanceId),("USB interface path",d.InterfacePath ?? "Unavailable"),("Interface number (PnP MI)",d.InterfaceNumber),("Hardware IDs",string.Join("; ",d.HardwareIds)),("Compatible IDs / class",string.Join("; ",d.CompatibleIds)),("Driver provider",d.DriverProvider),("Driver version",d.DriverVersion),("PnP problem code",d.ProblemCode.ToString()),("Parent chain",string.Join(" → ",d.Ancestors)),("Associated printer queue","See Windows Printer tab for association evidence") })
            Facts.Add(new(name, value, source, string.IsNullOrEmpty(value) || value.StartsWith("Unavailable") ? "Unavailable" : "Metadata"));
        Facts.Add(new("Firmware version", "Unavailable", "No supported query", "Unavailable"));
        Facts.Add(new("Serial number (before query)", "Unavailable", "PnP instance suffix is not a verified serial", "Unavailable"));
        Facts.Add(new("Raw USB descriptors", "Unavailable", "Collect through USB Interfaces / Windows hub API", "Unavailable"));
    }
    public void Dispose() { operation?.Cancel(); SavePreferences(); session.Dispose(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
public sealed class ActionCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
