using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.Services;
using Norcom.Launcher.ViewModels;
using Norcom.Launcher.Views;

namespace Norcom.Launcher;

public partial class App : Application
{
	private readonly SingleInstance _instance = new SingleInstance();


	protected override async void OnStartup(StartupEventArgs e)
	{
		HardwareProbe.Reader = () => new WmiPcProbe().TryRead();
		_003C_003En__0(e);
		((Application)this).DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(OnUiException);
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs args)
		{
			if (args.ExceptionObject is Exception ex3)
			{
				Report(ex3, "unhandled");
			}
		};
		if (!_instance.Acquire())
		{
			((Application)this).Shutdown(0);
			return;
		}
		SplashWindow splash = new SplashWindow();
		((Window)splash).Show();
		await ((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
		{
		}, (DispatcherPriority)7);
		ShellViewModel vm = new ShellViewModel();
		ShellWindow shellWindow = new ShellWindow();
		if (QaGates.OfflineCheckRequested()) shellWindow.Title = "NORCOM Launcher — offline verification";
		((FrameworkElement)shellWindow).DataContext = vm;
		ShellWindow shellWindow2 = (ShellWindow)(object)(((Application)this).MainWindow = (Window)(object)shellWindow);
		shellWindow2.IsEnabled = false;
		bool readyHandled = false;
		shellWindow2.ContentRendered += async (_, _) =>
		{
			if (readyHandled) return;
			readyHandled = true;
			try
			{
				await BridgeRuntime.MarkShellReadyAsync();
				shellWindow2.IsEnabled = true;
				if (!string.IsNullOrWhiteSpace(BridgeRuntime.RecoveryMessage))
					vm.StatusLine = BridgeRuntime.RecoveryMessage;
				else if (await vm.ApplyUpdateIfNeededAsync((_, _) => { }, CancellationToken.None))
					Shutdown(0);
			}
			catch (Exception)
			{
				MessageBox.Show("The launcher could not confirm its startup. Reopen NORCOM to recover the previous version.", "NORCOM");
				Shutdown(1);
			}
		};
		((Window)shellWindow2).Show();
		((Window)splash).Close();
		((Window)shellWindow2).Activate();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_instance.Dispose();
		base.OnExit(e);
	}

	private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		Report(e.Exception, "ui");
		e.Handled = true;
	}

	private static void Report(Exception ex, string source)
	{
		if (QaGates.OfflineCheckRequested())
		{
			LauncherLog.Write(source + ": " + ex.GetType().Name);
			MessageBox.Show("Offline verification error: " + Redactor.Clean(ex.Message), "NORCOM Launcher");
			return;
		}
        try { CrashReportService.Queue(source,ex.Message,ex.StackTrace); }
        catch { LauncherLog.Write("local report unavailable"); }
        MessageBox.Show("A launcher error was saved locally. You can choose to send diagnostics from Settings.","NORCOM Launcher");
    }

	[CompilerGenerated]
    private void _003C_003En__0(StartupEventArgs e) => base.OnStartup(e);
}
