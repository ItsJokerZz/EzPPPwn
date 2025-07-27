using Chapter.Net.WPF.SystemTray;
using Dark.Net;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace EzPPPwn;

public partial class MainWindow : Window
{
    #region Private Fields
    private TrayIcon _icon = new();
    private MenuItem startStopMenuItem = new();

    private string ethAdapterName = string.Empty;
    private string firmware = string.Empty;
    private string stage2Path = string.Empty;

    private bool autoRetry = true;
    private bool useBetaPPPwn = false;
    private bool useGoldHEN = true;
    private bool isRunning = false;
    #endregion

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        SetupEventHandlers();
        InitAdapters();
        InitializeUI();
        BeginTasks();
    }

    #region Initialization Methods
    private void LoadSettings()
    {
        ethAdapterName = Properties.Settings.Default.Adapter ?? string.Empty;
        firmware = Properties.Settings.Default.Firmware ?? "11.00";
        autoRetry = Properties.Settings.Default.AutoRetry;
        useBetaPPPwn = Properties.Settings.Default.PPPwnBeta;
        useGoldHEN = Properties.Settings.Default.GoldHEN;
        stage2Path = Properties.Settings.Default.Stage2;
    }

    private void InitializeSysTray()
    {
        _icon = new TrayIcon("EzPPPwn.exe", this)
        {
            ToolTip = "EzPPPwn",
            ContextMenu = new ContextMenu()
        };

        startStopMenuItem = new MenuItem { Header = "Start" };
        var quitMenuItem = new MenuItem { Header = "Exit" };
        var restartMenuItem = new MenuItem { Header = "Restart" };

        _icon.ContextMenu.Items.Add(startStopMenuItem);
        _icon.ContextMenu.Items.Add(quitMenuItem);
        _icon.ContextMenu.Items.Add(restartMenuItem);

        startStopMenuItem.Click += (s, e) =>
        {
            ProcessButton_Click(this, new RoutedEventArgs());
            UpdateRunningStateUI();
        };

        quitMenuItem.Click += async (s, e) =>
        {
            await KillPppwnProcess();
            Environment.Exit(0);
        };

        restartMenuItem.Click += (s, e) =>
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });

            Environment.Exit(0);
        };

        _icon.Show();

        UpdateRunningStateUI();
    }

    private void SetupEventHandlers()
    {
        FirmwareComboBox.SelectionChanged += OnFirmware_Changed;
        NetworkAdapterComboBox.SelectionChanged += OnAdapter_Changed;
        AutoRetryCheckBox.Checked += AutoRetryCheckBox_Checked;
        AutoRetryCheckBox.Unchecked += AutoRetryCheckBox_Checked;
        UseBetaCheckBox.Checked += UseBetaCheckBox_Checked;
        UseBetaCheckBox.Unchecked += UseBetaCheckBox_Checked;
        VtxCheckBox.Checked += VtxCheckBox_Checked;
        VtxCheckBox.Unchecked += VtxCheckBox_Checked;
        Closing += MainWindow_Closing;
    }

    private void InitAdapters()
    {
        NetworkAdapterComboBox.Items.Clear();

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
                continue;

            if (adapter.OperationalStatus != OperationalStatus.Up)
                continue;

            if (adapter.Description.Contains("virtual", StringComparison.CurrentCultureIgnoreCase) ||
                adapter.Description.Contains("vethernet", StringComparison.CurrentCultureIgnoreCase) ||
                adapter.Description.Contains("hyper-v", StringComparison.CurrentCultureIgnoreCase) ||
                adapter.Name.Contains("vethernet", StringComparison.CurrentCultureIgnoreCase))
                continue;

            var ipProps = adapter.GetIPProperties();
            bool hasIpv4 = ipProps.UnicastAddresses
                .Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork);

            if (!hasIpv4) continue;

            NetworkAdapterComboBox.Items.Add(new ComboBoxItem { Content = adapter.Name });
        }
    }

    private void InitializeUI()
    {
        DarkNet.Instance.SetCurrentProcessTheme(Theme.Dark);
        DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

        InitializeSysTray();

        SetSelectedItem(FirmwareComboBox, Properties.Settings.Default.Firmware ?? "11.00", 22);
        SetSelectedItem(NetworkAdapterComboBox, ethAdapterName, 0);
        UseBetaCheckBox.IsChecked = useBetaPPPwn;
        AutoRetryCheckBox.IsChecked = autoRetry;
        VtxCheckBox.IsChecked = !useGoldHEN;

        if (stage2Path != string.Empty)
            OverrideStage2Button.Content = "Use Default Stage2 Payload";
    }

    private void BeginTasks()
    {
        _ = ExtractPPPwn();
        _ = KillPppwnProcess();
        _ = InstallNpcap();
    }
    #endregion

    #region UI Helper Methods
    private static void SetSelectedItem(ComboBox comboBox, string? value, int fallbackIndex = -1)
    {
        var item = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (i.Content as string) == value);

        comboBox.SelectedItem = item ?? (fallbackIndex >= 0 && fallbackIndex < comboBox.Items.Count
            ? comboBox.Items[fallbackIndex] : null);
    }

    private void SetProgress(double value)
    {
        ProgressBar.Value = value;
        ProgressPercentLabel.Text = $"{value:0}%";
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        LogBox.AppendText(text + "\n");

        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.Focus();
        LogBox.ScrollToEnd();

        LogBox.Dispatcher.Invoke(() =>
        {
            LogBox.UpdateLayout();
            LogBox.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void UpdateRunningStateUI()
    {
        startStopMenuItem.Header = isRunning ? "Stop" : "Start";
        ProcessButton.Content = isRunning ? "Stop Process" : "Start Process";
    }
    #endregion

    #region Event Handlers
    private static void SaveSetting(string settingName, object value)
    {
        var property = typeof(Properties.Settings).GetProperty(settingName);
        property?.SetValue(Properties.Settings.Default, value);
        Properties.Settings.Default.Save();
    }

    private void AutoRetryCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        autoRetry = AutoRetryCheckBox.IsChecked == true;
        SaveSetting(nameof(Properties.Settings.Default.AutoRetry), autoRetry);
    }

    private void UseBetaCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        useBetaPPPwn = UseBetaCheckBox.IsChecked == true;
        SaveSetting(nameof(Properties.Settings.Default.PPPwnBeta), useBetaPPPwn);
    }

    private void VtxCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        useGoldHEN = VtxCheckBox.IsChecked == false;
        SaveSetting(nameof(Properties.Settings.Default.GoldHEN), useGoldHEN);
    }

    private void OnFirmware_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FirmwareComboBox.SelectedItem is ComboBoxItem item && item.Content is string value)
        {
            firmware = value.Replace(".", string.Empty);
            SaveSetting(nameof(Properties.Settings.Default.Firmware), value);
        }
    }

    private void OnAdapter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (NetworkAdapterComboBox.SelectedItem is ComboBoxItem item && item.Content is string value)
        {
            ethAdapterName = value;
            SaveSetting(nameof(Properties.Settings.Default.Adapter), value);
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isRunning)
        {
            var result = MessageBox.Show("PPPwn is currently running, are you sure you want to exit?", "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        await KillPppwnProcess();
    }
    #endregion

    private async Task ExtractPPPwn()
    {
        var targetDir = AppDomain.CurrentDomain.BaseDirectory;
        var extractDir = Path.Combine(targetDir, "PPPwn");

        if (Directory.Exists(extractDir))
            return;

        var tempZipPath = Path.Combine(Path.GetTempPath(), "PPPwn.zip");

        try
        {
            await File.WriteAllBytesAsync(tempZipPath, Properties.Resources.PPPwn);
            System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, targetDir);
            File.Delete(tempZipPath);
        }
        catch (Exception ex)
        {
            AppendLog($"PPPwn extraction failed: {ex.Message}");
        }
    }

    private async Task KillPppwnProcess()
    {
        try
        {
            var processes = Process.GetProcessesByName(useBetaPPPwn ? "pppwn-beta" : "pppwn-stable");
            foreach (var process in processes)
            {
                process.Kill();
                await Task.Delay(1000);
                process.Dispose();
            }
        }
        catch { }
    }

    private async Task InstallNpcap()
    {
        if (Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Npcap", false) != null) return;
        if (Directory.Exists("C:\\Program Files\\Npcap")) return; // ^ didnt work but should

        var tempPath = Path.Combine(Path.GetTempPath(), "npcap-1.79.exe");

        try
        {
            await File.WriteAllBytesAsync(tempPath, Properties.Resources.npcap_1_79);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            if (process != null)
                await Task.Run(process.WaitForExit);

            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            AppendLog($"Npcap installation failed: {ex.Message}");
        }
    }

    private void ToggleAdapter(string interfaceName, bool enable)
    {
        var action = enable ? "enable" : "disable";
        var arguments = $"/c netsh interface set interface \"{interfaceName}\" admin={action}";

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        if (process == null) return;

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (!string.IsNullOrEmpty(output)) AppendLog(output);
        if (!string.IsNullOrEmpty(error)) AppendLog(error);
    }

    private void OverrideStage2Button_Click(object sender, RoutedEventArgs e)
    {
        if ((string)OverrideStage2Button.Content == "Use Default Stage2 Payload")
        {
            stage2Path = string.Empty;
            OverrideStage2Button.Content = "Override Stage2 Payload";
        }
        else
        {
            var openFile = new OpenFileDialog
            {
                Filter = "BIN files (*.bin)|*.bin",
                CheckFileExists = true,
                Multiselect = false
            };

            if (openFile.ShowDialog() == true)
            {
                stage2Path = openFile.FileName;
                OverrideStage2Button.Content = "Use Default Stage2 Payload";
            }
        }

        SaveSetting("Stage2", stage2Path);
    }

    private async void ProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRunning)
        {
            await KillPppwnProcess();
            LogBox.Text = string.Empty;
            SetProgress(0);
            isRunning = false;
            UpdateRunningStateUI();
            return;
        }

        isRunning = true;
        UpdateRunningStateUI();

        SetProgress(0);
        LogBox.Text = string.Empty;
        ToggleAdapter(ethAdapterName, false);
        ToggleAdapter(ethAdapterName, true);

        var ni = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == ethAdapterName);
        var ethInterface = ni != null ? $"\\Device\\NPF_{ni.Id}" : string.Empty;
        var fw = firmware.Replace(".", "");
        var stage2Folder = useGoldHEN ? "GoldHEN" : "PS4-HEN (VTX)";
        var stage2File = $"stage2_{Properties.Settings.Default.Firmware}.bin";
        var stage1Path = $".\\PPPwn\\Stages\\1\\{fw}\\stage1.bin";

        if (string.IsNullOrEmpty(stage2Path))
            stage2Path = $".\\PPPwn\\Stages\\2\\{stage2Folder}\\{stage2File}";

        var args = $"--interface {ethInterface} --fw {fw} --stage1 \"{stage1Path}\" --stage2 \"{stage2Path}\" {(autoRetry ? "-a" : "")}";

        var process = new Process
        {
            EnableRaisingEvents = true,
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PPPwn", useBetaPPPwn ? "pppwn-beta.exe" : "pppwn-stable.exe"),
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        async void OutputHandler(object _, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            await Dispatcher.InvokeAsync(() =>
            {
                var progress = e.Data.Contains("STAGE 0") ? 5 :
                               e.Data.Contains("STAGE 1") ? 20 :
                               e.Data.Contains("STAGE 2") ? 45 :
                               e.Data.Contains("STAGE 3") ? 70 :
                               e.Data.Contains("STAGE 4") ? 85 :
                               e.Data.Contains("Done!") ? 100 : -1;

                if (progress >= 0) SetProgress(progress);

                AppendLog($"{DateTime.Now:HH:mm:ss}: {e.Data}");

                if (e.Data.Contains("SYNOPSIS"))
                    if (!process.HasExited)
                        process.Kill();
            });
        }

        process.OutputDataReceived += OutputHandler;
        process.ErrorDataReceived += OutputHandler;

        var tcs = new TaskCompletionSource<object?>();

        process.Exited += (_, __) =>
        {
            Dispatcher.Invoke(() =>
            {
                isRunning = false;
                SetProgress(0);
                UpdateRunningStateUI();
            });

            tcs.SetResult(null);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await tcs.Task;
    }

}
