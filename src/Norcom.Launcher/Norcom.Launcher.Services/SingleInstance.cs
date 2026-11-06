using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Norcom.Launcher.Services;

public sealed class SingleInstance : IDisposable
{
	private Mutex? _mutex;

	private EventWaitHandle? _activate;

	private EventWaitHandle? _ack;

	private Thread? _thread;

	private volatile bool _run = true;

	public bool Acquire()
	{
		if (!OperatingSystem.IsWindows())
		{
			return true;
		}
		if (TryMutex(out _mutex))
		{
			_activate = CreateEvent("NORCOM.Launcher.Activate");
			_ack = CreateEvent("NORCOM.Launcher.Ack");
			_thread = new Thread(Listen)
			{
				IsBackground = true,
				Name = "NORCOM.Activate"
			};
			_thread.Start();
			return true;
		}
		Signal("NORCOM.Launcher.Activate");
		if (Wait("NORCOM.Launcher.Ack", 3000))
		{
			return false;
		}
		Process[] processesByName = Process.GetProcessesByName("NorcomLauncher");
		foreach (Process process in processesByName)
		{
			if (process.Id != Environment.ProcessId)
			{
				try
				{
					process.Kill(entireProcessTree: false);
				}
				catch
				{
				}
			}
		}
		Thread.Sleep(400);
		if (!TryMutex(out _mutex))
		{
			return false;
		}
		_activate = CreateEvent("NORCOM.Launcher.Activate");
		_ack = CreateEvent("NORCOM.Launcher.Ack");
		_thread = new Thread(Listen)
		{
			IsBackground = true,
			Name = "NORCOM.Activate"
		};
		_thread.Start();
		return true;
	}

	public void Dispose()
	{
		_run = false;
		try
		{
			_activate?.Set();
		}
		catch
		{
		}
		_mutex?.Dispose();
		_activate?.Dispose();
		_ack?.Dispose();
	}

	private void Listen()
	{
		while (_run && _activate != null)
		{
			if (!_activate.WaitOne(500))
			{
				continue;
			}
			if (!_run)
			{
				break;
			}
			try
			{
				_ack?.Reset();
			}
			catch
			{
			}
			Application current = Application.Current;
			if (current == null)
			{
				continue;
			}
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				//IL_0015: Unknown result type (might be due to invalid IL or missing references)
				//IL_001b: Invalid comparison between Unknown and I4
				Window mainWindow = Application.Current.MainWindow;
				if (mainWindow != null)
				{
					mainWindow.Show();
					if ((int)mainWindow.WindowState == 1)
					{
						mainWindow.WindowState = (WindowState)0;
					}
					mainWindow.Activate();
					mainWindow.Topmost = true;
					mainWindow.Topmost = false;
				}
				try
				{
					_ack?.Set();
				}
				catch
				{
				}
			}, Array.Empty<object>());
		}
	}

	private static bool TryMutex(out Mutex? mutex)
	{
		string[] array = new string[2] { "Global\\NORCOM.Launcher.SingleInstance", "NORCOM.Launcher.SingleInstance" };
		foreach (string name in array)
		{
			try
			{
				mutex = new Mutex(initiallyOwned: false, name);
				try
				{
					if (mutex.WaitOne(0))
					{
						return true;
					}
				}
				catch (AbandonedMutexException)
				{
					return true;
				}
				mutex.Dispose();
			}
			catch
			{
			}
		}
		mutex = null;
		return false;
	}

	private static EventWaitHandle CreateEvent(string name)
	{
		try
		{
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Global\\" + name);
		}
		catch
		{
			return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
		}
	}

	private static void Signal(string name)
	{
		try
		{
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Global\\" + name);
			eventWaitHandle.Set();
		}
		catch
		{
			try
			{
				using EventWaitHandle eventWaitHandle2 = EventWaitHandle.OpenExisting(name);
				eventWaitHandle2.Set();
			}
			catch
			{
			}
		}
	}

	private static bool Wait(string name, int ms)
	{
		try
		{
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Global\\" + name);
			return eventWaitHandle.WaitOne(ms);
		}
		catch
		{
			try
			{
				using EventWaitHandle eventWaitHandle2 = EventWaitHandle.OpenExisting(name);
				return eventWaitHandle2.WaitOne(ms);
			}
			catch
			{
				return false;
			}
		}
	}
}
