using System;
using System.Threading;
using System.Windows.Forms;

namespace PortsInUse
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }

        static void Report(Exception ex)
        {
            if (ex == null) return;
            MessageBox.Show(ex.ToString(), "Ports In Use — unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
