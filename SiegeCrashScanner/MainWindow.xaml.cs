using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;

namespace SiegeCrashScanner;

public partial class MainWindow : Window
{
    private readonly ScanService _scanner = new();
    private ScanReport? _report;
    private SystemSnapshot? _systemSnapshot;
    private AppUpdateInfo? _availableUpdate;
    private bool _systemLoaded;

    public ObservableCollection<SystemItemView> SystemItems { get; } = [];
    public ObservableCollection<FindingView> FindingItems { get; } = [];
    public ObservableCollection<CrashView> CrashItems { get; } = [];
    public ObservableCollection<EventView> EventItems { get; } = [];
    public ObservableCollection<CauseView> CauseItems { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ShowLoadingSystemItems();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_systemLoaded) return;
        _systemLoaded = true;
        try
        {
            var snapshot = await _scanner.ReadSystemInfoAsync();
            ShowSystem(snapshot);
            SystemStatusText.Text = "System information ready. Run a scan for crash evidence.";
        }
        catch (Exception ex)
        {
            SystemStatusText.Text = "Some system information was unavailable: " + ex.Message;
        }

        await CheckForAppUpdateAsync();
    }

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            _availableUpdate = await AppUpdateService.CheckAsync();
            if (_availableUpdate is null) return;
            AppUpdateStatusText.Text = $"Version {_availableUpdate.Version} is available. The download will be SHA-256 verified before installation.";
            AppUpdatePanel.Visibility = Visibility.Visible;
        }
        catch
        {
            // Being offline or GitHub being unavailable must never block diagnostics.
        }
    }

    private async void InstallAppUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        var answer = MessageBox.Show(
            this,
            $"Install Siege Crash Scanner {_availableUpdate.Version}?\n\nThe app will download the GitHub release, verify its checksum, request administrator approval, replace itself, and reopen.",
            "Install verified update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        InstallAppUpdateButton.IsEnabled = false;
        InstallAppUpdateButton.Content = "DOWNLOADING…";
        var progress = new Progress<string>(text => AppUpdateStatusText.Text = text);
        try
        {
            await AppUpdateService.DownloadVerifyAndRestartAsync(_availableUpdate, progress);
            AppUpdateStatusText.Text = "Update verified. Closing the app to finish installation…";
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppUpdateStatusText.Text = "Administrator approval was canceled. The app was not changed.";
            InstallAppUpdateButton.Content = "INSTALL UPDATE";
            InstallAppUpdateButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppUpdateStatusText.Text = "Update failed: " + ex.Message;
            InstallAppUpdateButton.Content = "TRY AGAIN";
            InstallAppUpdateButton.IsEnabled = true;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanButton.Content = "SCANNING…";
        ProgressPanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
        var progress = new Progress<string>(text => ProgressText.Text = text);
        try
        {
            _report = await _scanner.ScanAsync(progress);
            ShowSystem(_report.System);
            ShowReport(_report);
            ResultsPanel.Visibility = Visibility.Visible;
            ResultsPanel.BringIntoView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The scan could not finish.\n\n" + ex.Message, "Siege Crash Scanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ScanButton.Content = "SCAN AGAIN";
            ScanButton.IsEnabled = true;
        }
    }

    private void ShowLoadingSystemItems()
    {
        SystemItems.Clear();
        foreach (var label in new[] { "CPU", "GPU", "RAM", "RAM SPEED", "WINDOWS", "GPU DRIVER", "MOTHERBOARD", "BIOS", "SIEGE INSTALLATION" })
            SystemItems.Add(new SystemItemView(label, "Detecting…"));
    }

    private void ShowSystem(SystemSnapshot system)
    {
        _systemSnapshot = system;
        SystemItems.Clear();
        SystemItems.Add(new("CPU", system.Cpu));
        SystemItems.Add(new("GPU", system.Gpu));
        SystemItems.Add(new("RAM", Format.Bytes(system.TotalRamBytes)));
        SystemItems.Add(new("RAM SPEED", system.RamSpeed));
        SystemItems.Add(new("WINDOWS", system.WindowsVersion));
        SystemItems.Add(new("GPU DRIVER", $"{system.GpuDriverVersion} · {system.GpuDriverDate}"));
        SystemItems.Add(new("MOTHERBOARD", system.Motherboard));
        SystemItems.Add(new("BIOS", system.BiosVersion));
        SystemItems.Add(new("SIEGE INSTALLATION", system.SiegeDetected ? "Detected · " + system.SiegeInstallDirectory : "Not detected"));
    }

    private async void DriverUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_systemSnapshot is null)
        {
            MessageBox.Show(this, "Please wait for system detection to finish.", "Siege Crash Scanner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Windows will search for and install applicable signed GPU and platform-driver updates for this PC.\n\n" +
            "Administrator approval is required. Keep the PC powered on. This does not update the BIOS. Continue?",
            "Update GPU & platform drivers",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        DriverUpdateButton.IsEnabled = false;
        DriverUpdateButton.Content = "UPDATING…";
        DriverOutputPanel.Visibility = Visibility.Visible;
        DriverOutput.Text = "Preparing administrator request…";
        var progress = new Progress<string>(text => DriverOutput.Text = text);
        try
        {
            DriverOutput.Text = await DriverUpdateService.InstallApplicableUpdatesAsync(_systemSnapshot, progress);
            DriverOutput.ScrollToEnd();
        }
        catch (Exception ex)
        {
            DriverOutput.Text = "Driver update could not finish.\n\n" + ex.Message;
        }
        finally
        {
            DriverUpdateButton.Content = "CHECK AGAIN";
            DriverUpdateButton.IsEnabled = true;
        }
    }

    private void ShowReport(ScanReport report)
    {
        ResultsSubtitle.Text = $"Completed {report.GeneratedAt:g} · Evidence from the last 30 days";
        FindingItems.Clear();
        foreach (var item in report.Findings) FindingItems.Add(new FindingView(item));

        CrashItems.Clear();
        if (report.SiegeCrashes.Count == 0) CrashItems.Add(CrashView.Empty());
        else foreach (var crash in report.SiegeCrashes) CrashItems.Add(new CrashView(crash));

        EventItems.Clear();
        foreach (var item in report.WheaEvents) EventItems.Add(new EventView(item));
        foreach (var item in report.GpuEvents) EventItems.Add(new EventView(item));
        if (EventItems.Count == 0) EventItems.Add(new EventView("No WHEA or graphics-driver errors were found in the last 30 days.", string.Empty));

        CauseItems.Clear();
        for (var i = 0; i < report.LikelyCauses.Count; i++) CauseItems.Add(new CauseView(i + 1, report.LikelyCauses[i]));
        if (CauseItems.Count == 0) CauseItems.Add(new CauseView(0, "No specific cause established", "Not ranked", "The available Windows evidence did not support a specific cause."));
        RecommendationText.Text = report.RecommendedNextStep;
    }

    private async void SfcButton_Click(object sender, RoutedEventArgs e) => await RunRepairAsync("sfc.exe /scannow");
    private async void DismButton_Click(object sender, RoutedEventArgs e) => await RunRepairAsync("dism.exe /Online /Cleanup-Image /RestoreHealth");

    private async Task RunRepairAsync(string command)
    {
        SfcButton.IsEnabled = DismButton.IsEnabled = false;
        CommandOutput.Text = "> " + command + "\n\nPreparing administrator request…";
        var progress = new Progress<string>(text => CommandOutput.Text = "> " + command + "\n\n" + text);
        try
        {
            CommandOutput.Text = await ProcessTools.RunElevatedRepairAsync(command, progress);
            CommandOutput.ScrollToEnd();
        }
        catch (Exception ex)
        {
            CommandOutput.Text = "> " + command + "\n\nCould not run command: " + ex.Message;
        }
        finally
        {
            SfcButton.IsEnabled = DismButton.IsEnabled = true;
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export Siege diagnostic report",
            Filter = "Text report (*.txt)|*.txt",
            FileName = $"Siege-Diagnostic-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, ReportFormatter.Create(_report));
            MessageBox.Show(this, "Diagnostic report exported successfully.", "Siege Crash Scanner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The report could not be saved.\n\n" + ex.Message, "Siege Crash Scanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public sealed record SystemItemView(string Label, string Value);

public sealed class FindingView
{
    public string Title { get; }
    public string Detail { get; }
    public string Status { get; }
    public string Color { get; }
    public FindingView(Finding finding)
    {
        Title = finding.Title;
        Detail = finding.Detail;
        Status = finding.Status.ToString().ToUpperInvariant();
        Color = finding.Status switch { FindingStatus.Pass => "#39D98A", FindingStatus.Warning => "#F9B84A", FindingStatus.Fail => "#FF6577", _ => "#72A7FF" };
    }
}

public sealed class CrashView
{
    public string Time { get; }
    public string Application { get; }
    public string Summary { get; }
    public string Explanation { get; }
    public CrashView(CrashEvent item)
    {
        Time = item.Time.ToString("g");
        Application = item.Application;
        Summary = $"Module: {item.FaultingModule} · Code: {item.ExceptionCode} · Offset: {item.FaultOffset}";
        Explanation = item.Explanation;
    }
    private CrashView(string time, string app, string summary, string explanation) { Time = time; Application = app; Summary = summary; Explanation = explanation; }
    public static CrashView Empty() => new("No events", "", "No Siege application crashes were found in the last 30 days.", "Reproduce a crash and scan again to enable time correlation.");
}

public sealed class EventView
{
    public string Heading { get; }
    public string Message { get; }
    public EventView(DiagnosticEvent item) { Heading = $"{item.Time:g} · {item.Provider} · Event {item.EventId} · {item.Category}"; Message = item.Message; }
    public EventView(string heading, string message) { Heading = heading; Message = message; }
}

public sealed class CauseView
{
    public string Rank { get; }
    public string Name { get; }
    public string Likelihood { get; }
    public string Evidence { get; }
    public CauseView(int rank, LikelyCause cause) : this(rank, cause.Name, cause.Likelihood + " likelihood", cause.Evidence) { }
    public CauseView(int rank, string name, string likelihood, string evidence) { Rank = rank == 0 ? "—" : rank + "."; Name = name; Likelihood = likelihood; Evidence = evidence; }
}
