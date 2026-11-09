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

public class App : Application
{
	private readonly SingleInstance _instance = new SingleInstance();

	private bool _contentLoaded;

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
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			CrashReportService.Delivery delivery = Deliver(CrashReportService.Queue(source, ex.Message, ex.StackTrace));
			LauncherLog.Write(source + ": " + ex.GetType().Name);
			MessageBox.Show(delivery.UserText, "NORCOM", (MessageBoxButton)0, (MessageBoxImage)48);
			return;
		}
		catch
		{
		}
		MessageBox.Show("A report was saved on this PC. Sending it to NORCOM failed. It will be tried again the next time NORCOM opens.", "NORCOM", (MessageBoxButton)0, (MessageBoxImage)48);
	}

	private static CrashReportService.Delivery Deliver(string report)
	{
		string path = Path.Combine(NorcomPaths.LauncherDir, "crash-webhook.url");
		string fileText = (File.Exists(path) ? File.ReadAllText(path) : null);
		LauncherSettings launcherSettings = LauncherSettings.Load(NorcomPaths.SettingsPath);
		string webhookUrl = CrashReportService.ResolveWebhook(Environment.GetEnvironmentVariable("NORCOM_CRASH_WEBHOOK"), launcherSettings.CrashWebhookUrl, fileText);
		string ingestUrl = CrashIngest.Resolve(Environment.GetEnvironmentVariable("NORCOM_CRASH_INGEST"), "https://skaahanzi.tailefd7c9.ts.net/v1/crash");
		using HttpClient http = new HttpClient(new HttpClientHandler
		{
			AllowAutoRedirect = false
		})
		{
			Timeout = TimeSpan.FromSeconds(12.0)
		};
		string text = RuntimeInformation.OSDescription;
		if (text.Length > 40)
		{
			text = text.Substring(0, 40);
		}
		return CrashReportService.TryDeliverAsync(http, report, webhookUrl, ingestUrl, text, CrashIngest.ReadLogTail(NorcomPaths.LogPath), CancellationToken.None).GetAwaiter().GetResult();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/NorcomLauncher;V2.0.1.0;component/app.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	public static void Main(string[] args)
	{
		try
		{
			if (!BridgeRuntime.EnterAsync(args).GetAwaiter().GetResult()) return;
		}
		catch (Exception ex)
		{
			MessageBox.Show("NORCOM could not open safely. Your recovery files were kept.\n\n" + Redactor.Clean(ex.Message), "NORCOM Launcher");
			return;
		}
		App app = new App();
		app.InitializeComponent();
		((Application)app).Run();
	}

	[CompilerGenerated]
	[DebuggerHidden]
	private void _003C_003En__0(StartupEventArgs e)
	{
		base.OnStartup(e);
	}
}
