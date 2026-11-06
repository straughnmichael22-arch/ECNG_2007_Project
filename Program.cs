
using System;
using System.Windows.Forms;

namespace EDAEnvironmentAudit
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Launch the startup window
            Application.Run(new StartupWindow());
        }
    }
}
