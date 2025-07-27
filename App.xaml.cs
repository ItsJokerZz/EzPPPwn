using System.Windows;

namespace EzPPPwn;

public partial class App : Application
{
    private static Mutex? singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(true, "EzPPPwnSingleInstanceMutex", out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("EzPPPwn is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}
