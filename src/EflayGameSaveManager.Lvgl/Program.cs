using LVGLSharp.Forms;

namespace EflayGameSaveManager.Lvgl;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "lvgl-startup-error.log"),
                ex.ToString());
            throw;
        }
    }
}
