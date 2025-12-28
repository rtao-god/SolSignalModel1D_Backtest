namespace SolSignalModel1D_Backtest.Core.Causal.Utils
	{
	/// <summary>
	/// LEGACY-хелпер: минимальная обёртка вокруг блока,
	/// которая просто крутит "анимацию" в заголовке окна (Console.Title)
	/// на время выполнения action.
	/// </summary>
	public static class ConsoleBlockTimer
		{
		/// <summary>
		/// Асинхронный блок с анимацией только в заголовке окна.
		/// </summary>
		public static Task RunAsync ( string title, Func<Task> action )
			{
			return RunCoreAsync (title, action);
			}

		/// <summary>
		/// Синхронный блок с анимацией только в заголовке окна.
		/// </summary>
		public static Task RunAsync ( string title, Action action )
			{
			if (action == null)
				throw new ArgumentNullException (nameof (action));

			return RunCoreAsync (
				title,
				() =>
				{
					action ();
					return Task.CompletedTask;
				});
			}

		/// <summary>
		/// Общая реализация:
		/// - запускает фоновой спиннер, который крутит / - \ * в заголовке;
		/// - выполняет action;
		/// - останавливает спиннер и возвращает исходный заголовок.
		/// </summary>
		private static async Task RunCoreAsync ( string title, Func<Task> action )
			{
			if (action == null)
				throw new ArgumentNullException (nameof (action));

			title ??= string.Empty;

			using var cts = new CancellationTokenSource ();

			string? originalTitle = null;
			Task? spinnerTask = null;

			try
				{
				// Пытаемся сохранить исходный заголовок консоли.
				try
					{
					originalTitle = Console.Title;
					}
				catch
					{
					// Если консоль не поддерживает Title — работаем без восстановления.
					originalTitle = null;
					}

				// Запускаем фоновую "анимацию" в заголовке окна (НЕ пишет в текст консоли).
				spinnerTask = Task.Run (() => SpinnerLoop (title, originalTitle, cts.Token));

				// Основная работа блока.
				await action ().ConfigureAwait (false);
				}
			finally
				{
				// Останавливаем спиннер.
				cts.Cancel ();

				if (spinnerTask != null)
					{
					try
						{
						await spinnerTask.ConfigureAwait (false);
						}
					catch
						{
						// Любые ошибки анимации игнорируем.
						}
					}

				// Пытаемся вернуть исходный заголовок.
				if (originalTitle != null)
					{
					try
						{
						Console.Title = originalTitle;
						}
					catch
						{
						// Если заголовок поменять нельзя — молча игнорируем.
						}
					}
				}
			}

		/// <summary>
		/// Фоновая "анимация": крутит / - \ * в заголовке окна.
		/// </summary>
		private static void SpinnerLoop ( string title, string? originalTitle, CancellationToken token )
			{
			var frames = new[] { "/", "-", "\\", "*" };
			int idx = 0;

			// Базовый текст заголовка для анимации.
			string baseTitle;

			if (!string.IsNullOrEmpty (title))
				{
				baseTitle = title;
				}
			else if (!string.IsNullOrEmpty (originalTitle))
				{
				baseTitle = originalTitle;
				}
			else
				{
				baseTitle = "SolSignalModel1D_Backtest";
				}

			while (!token.IsCancellationRequested)
				{
				var frame = frames[idx++ % frames.Length];

				try
					{
					Console.Title = $"{baseTitle} {frame}";
					}
				catch
					{
					// Если заголовок поменять нельзя (редирект и т.п.) — выходим.
					return;
					}

				try
					{
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
