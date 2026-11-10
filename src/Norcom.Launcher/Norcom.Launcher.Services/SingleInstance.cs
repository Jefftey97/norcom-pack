using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Norcom.Launcher.Services;

public sealed class SingleInstance : IDisposable
{
	private Mutex? _mutex;

	private readonly string _scope;
	private int _ownerThread;
	public SingleInstance() : this("NORCOM.Launcher") { }
	internal SingleInstance(string scope) => _scope = scope;

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
		if (TryMutex(_scope, out _mutex))
		{
			_ownerThread = Environment.CurrentManagedThreadId;
			_activate = CreateEvent(_scope + ".Activate");
			_ack = CreateEvent(_scope + ".Ack");
			_thread = new Thread(Listen)
			{
				IsBackground = true,
				Name = "NORCOM.Activate"
			};
			_thread.Start();
			return true;
		}
		Signal(_scope + ".Activate");
		if (Wait(_scope + ".Ack", 3000))
		{
			return false;
		}
		// A slow UI is not evidence that an arbitrary same-named process is stuck.
		return false;
	}

	public static bool ActivateExisting() => ActivateExisting("NORCOM.Launcher");
	internal static bool ActivateExisting(string scope)
	{
		if (TryMutex(scope, out var probe))
		{
			probe?.ReleaseMutex();
			probe?.Dispose();
			return false;
		}
		Signal(scope + ".Activate");
		Wait(scope + ".Ack", 3000);
		return true;
	}

	public void Dispose()
	{
		if (_mutex != null && _ownerThread != Environment.CurrentManagedThreadId)
			throw new InvalidOperationException("Release the launcher instance on its owning UI thread.");
		_run = false;
		try { _activate?.Set(); } catch (ObjectDisposedException) { }
		// The listener never synchronously waits on the UI dispatcher.
		if (_thread == null || _thread.Join(1500))
		{
			_activate?.Dispose(); _activate = null;
			_ack?.Dispose(); _ack = null;
		}
		if (_mutex != null) { _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null; }
	}

	private void Listen()
	{
		var activate = _activate;
		while (_run && activate != null)
		{
			try
			{
				if (!activate.WaitOne(500)) continue;
				if (!_run) break;
				_ack?.Reset();
				var current = Application.Current;
				if (current == null || current.Dispatcher.HasShutdownStarted || current.Dispatcher.HasShutdownFinished) continue;
				current.Dispatcher.BeginInvoke((Action)(() =>
				{
					if (!_run || current.Dispatcher.HasShutdownStarted) return;
					var window = current.MainWindow;
					if (window != null)
					{
						window.Show();
						if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
						window.Activate(); window.Topmost = true; window.Topmost = false;
					}
					try { _ack?.Set(); } catch (ObjectDisposedException) { }
				}));
			}
			catch (ObjectDisposedException) { break; }
			catch (InvalidOperationException) { if (!_run) break; }
		}
	}

	private static bool TryMutex(string scope, out Mutex? mutex)
	{
		string[] array = new string[2] { "Global\\" + scope + ".SingleInstance", scope + ".SingleInstance" };
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
				mutex = null;
				return false;
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
