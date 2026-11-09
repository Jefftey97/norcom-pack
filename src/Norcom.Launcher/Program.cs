using System;
using System.Windows;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.Services;

namespace Norcom.Launcher;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if(!BridgeRuntime.EnterAsync(args).GetAwaiter().GetResult())return;
            var app=new App();app.InitializeComponent();app.Run();
        }
        catch(Exception ex)
        {
            MessageBox.Show("NORCOM could not open safely. Your recovery files were kept.\n\n"+Redactor.Clean(ex.Message),"NORCOM Launcher");
        }
    }
}
