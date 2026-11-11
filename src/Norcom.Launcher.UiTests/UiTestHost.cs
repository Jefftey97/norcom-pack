using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Norcom.Launcher.UiTests;

internal static class UiTestHost
{
	private static readonly object Gate = new object();
	private static Dispatcher? dispatcher;

	public static void Run(Action action)
	{
		Ensure();
		if (Dispatcher.CurrentDispatcher == dispatcher)
		{
			action();
			return;
		}
		Exception? error = null;
		dispatcher!.Invoke(() =>
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				error = ex;
			}
		});
		if (error != null)
		{
			ExceptionDispatchInfo.Capture(error).Throw();
		}
	}

	private static void Ensure()
	{
		lock (Gate)
		{
			if (dispatcher != null)
			{
				return;
			}
			using ManualResetEventSlim ready = new ManualResetEventSlim(false);
			Exception? error = null;
			Thread thread = new Thread(() =>
			{
				try
				{
					Application app = Application.Current ?? new Norcom.Launcher.App();
					if (app.Resources == null || app.Resources.MergedDictionaries.Count == 0)
					{
						((Norcom.Launcher.App)app).InitializeComponent();
					}
					app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
					dispatcher = app.Dispatcher;
				}
				catch (Exception ex)
				{
					error = ex;
				}
				finally
				{
					ready.Set();
				}
				if (error == null)
				{
					Dispatcher.Run();
				}
			});
			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			if (!ready.Wait(TimeSpan.FromSeconds(15)))
			{
				throw new TimeoutException("UI test dispatcher did not start.");
			}
			if (error != null)
			{
				ExceptionDispatchInfo.Capture(error).Throw();
			}
		}
	}
}
