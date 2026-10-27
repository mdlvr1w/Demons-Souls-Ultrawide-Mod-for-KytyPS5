using System.Windows;

namespace DemonsSoulsLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            try
            {
                Shutdown(SelfTest.Run());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Shutdown(1);
            }
            return;
        }
        var watch = Array.IndexOf(e.Args, "--watch");
        if (watch >= 0)
        {
            var known = watch + 1 < e.Args.Length ? e.Args[watch + 1] : "";
            try
            {
                LiveFix.Watch(known);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            Shutdown(0);
            return;
        }
        new MainWindow().Show();
    }
}
