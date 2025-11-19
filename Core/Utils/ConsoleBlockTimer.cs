// Core/Utils/ConsoleBlockTimer.cs
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Utils
	{
	/// <summary>
	/// Помощник для обёртывания крупных блоков:
	/// рисует текстовую "анимацию" в консоли и печатает время выполнения.
	/// Может опционально сообщать шаг/процент через ConsoleProgress.
	/// </summary>
	public static class ConsoleBlockTimer
		{
		private static readonly object ConsoleLock = new object ();

		/// <summary>
		/// Асинхронный блок с анимацией и измерением времени
		/// без информации о прогрессе (процентах).
		/// </summary>
		public static Task RunAsync ( string title, Func<Task> action )
			{
			return RunCoreAsync (title, 0, 0, action);
			}

		/// <summary>
		/// Асинхронный блок с анимацией и измерением времени
		/// с учётом шага/общего числа шагов (для вывода процентов).
		/// </summary>
		public static Task RunAsync ( string title, int stepIndex, int totalSteps, Func<Task> action )
			{
			return RunCoreAsync (title, stepIndex, totalSteps, action);
			}

		/// <summary>
		/// Обёртка для синхронных блоков без процентов.
		/// </summary>
		public static Task RunAsync ( string title, Action action )
			{
			if (action == null)
				throw new ArgumentNullException (nameof (action));

			return RunCoreAsync (
				title,
				0,
				0,
				() =>
				{
					action ();
					return Task.CompletedTask;
				});
			}

		/// <summary>
		/// Обёртка для синхронных блоков с шагом/процентами.
		/// </summary>
		public static Task RunAsync ( string title, int stepIndex, int totalSteps, Action action )
			{
			if (action == null)
				throw new ArgumentNullException (nameof (action));

			return RunCoreAsync (
				title,
				stepIndex,
				totalSteps,
				() =>
				{
					action ();
					return Task.CompletedTask;
				});
			}

		private static async Task RunCoreAsync ( string title, int stepIndex, int totalSteps, Func<Task> action )
			{
			if (action == null)
				throw new ArgumentNullException (nameof (action));

			title ??= string.Empty;

			var sw = Stopwatch.StartNew ();
			using var cts = new CancellationTokenSource ();

			// Анимация на фоновой задаче.
			var spinnerTask = Task.Run (() => SpinnerLoop (title, cts.Token));

			try
				{
				await action ().ConfigureAwait (false);
				}
			finally
				{
				cts.Cancel ();

				try
					{
					await spinnerTask.ConfigureAwait (false);
					}
				catch
					{
					// Ошибка в анимации не должна ломать основной пайплайн.
					}

				sw.Stop ();

				// Если известен шаг/общее количество шагов — отдаём вывод ConsoleProgress.
				if (stepIndex > 0 && totalSteps > 0)
					{
					ConsoleProgress.PrintStep (title, stepIndex, totalSteps, sw.Elapsed);
					}
				else
					{
					// Fallback-режим: просто лог времени блока.
					var elapsed = sw.Elapsed;
					string formatted = elapsed.TotalSeconds >= 1.0
						? $"{elapsed.TotalSeconds:0.000}s"
						: $"{elapsed.TotalMilliseconds:0}ms";

					lock (ConsoleLock)
						{
						Console.WriteLine ($"\r[{title}] done in {formatted}          ");
						}
					}
				}
			}

		private static void SpinnerLoop ( string title, CancellationToken token )
			{
			var frames = new[] { "/", "-", "\\", "*" };
			int idx = 0;

			while (!token.IsCancellationRequested)
				{
				string frame = frames[idx++ % frames.Length];

				lock (ConsoleLock)
					{
					Console.Write ($"\r[{title}] {frame}");
					}

				try
					{
					// Частота обновления анимации (~8 кадров/сек).
					Thread.Sleep (120);
					}
				catch (ThreadInterruptedException)
					{
					break;
					}
				}
			}
		}
	}
