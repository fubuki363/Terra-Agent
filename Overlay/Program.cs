using System.Threading;

namespace TerraWikiOverlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex singleInstance = new(true, "Local\\TerraWikiOverlay-6F6E3F6C", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Terra Agent 悬浮助手已经在运行。", "Terra Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
        {
            AppDiagnostics.Record(args.Exception);
            MessageBox.Show("界面操作遇到错误，诊断信息已保存在本地 errors.log。请重新打开窗口后重试。", "Terra Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { AppDiagnostics.Record(args.Exception); args.SetObserved(); };
        try { Application.Run(new FloatingAssistantForm()); }
        catch (Exception exception)
        {
            AppDiagnostics.Record(exception);
            MessageBox.Show("Terra Agent 启动失败：" + exception.GetBaseException().Message, "Terra Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
