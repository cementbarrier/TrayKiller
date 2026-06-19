namespace TrayKiller;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // 防止多实例运行
        using var mutex = new Mutex(true, "TrayKiller_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TrayKiller 已经在运行中。", "TrayKiller",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new AppContext());
    }
}
