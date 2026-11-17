using System.Windows;
using HolyBeat.LiveGuard.Core.Config;

namespace HolyBeat.LiveGuard.App;

public partial class App : System.Windows.Application
{
    /// <summary>Application wide configuration, loaded once at startup.</summary>
    public static AppConfig Config { get; private set; } = ConfigStore.CreateDefault();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Config = ConfigStore.Load();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
