using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HackRfIqDataTest.HackRf;
using HackRfIqDataTest.Storage;
using HackRfIqDataTest.ViewModels;
using HackRfIqDataTest.Views;

namespace HackRfIqDataTest;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var userLog = new UserLog();
        var recordingsDirectory = RecordingLibrary.DefaultDirectory;
        var analysis = new AnalysisViewModel(recordingsDirectory);
        _ = analysis.RefreshAsync();
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(userLog, new HackRfRadio(), recordingsDirectory, analysis),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}