// Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
// Licensed under the Apache License, Version 2.0.
using System.Threading;
using System.Windows.Forms;
using Microsoft.Tools.WindowsInstallerXml.Bootstrapper;

[assembly: BootstrapperApplication(typeof(SSMConnect.BA.SetupApp))]

namespace SSMConnect.BA
{
    /// <summary>Entry point of the SSMConnect setup window (WiX managed bootstrapper application).</summary>
    public class SetupApp : BootstrapperApplication
    {
        internal int ExitCode;

        protected override void Run()
        {
            var ui = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(this));
            });
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            ui.Join();
            Engine.Quit(ExitCode);
        }
    }
}
