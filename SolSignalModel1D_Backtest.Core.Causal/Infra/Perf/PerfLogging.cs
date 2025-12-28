using System.Diagnostics;

namespace SolSignalModel1D_Backtest.Core.Causal.Infra.Perf
	{
	/// <summary>
	/// Утилита для замера производительности:
	/// - общий таймер всего приложения (StartApp/StopAppAndPrintSummary),
	/// - измерение отдельных блоков (Measure / MeasureAsync / MeasureBlock),
	/// - аккуратная подрезка "пустого" скролла консоли в конце.
	/// </summary>
	public static class PerfLogging
		{
		// Таймер всего приложения. Запускается в StartApp и останавливается в StopAppAndPrintSummary.
		private static readonly Stopwatch AppStopwatch = new Stopwatch ();

		// Накопленная сумма времени всех блоков, измеренных через PerfLogging (Measure/MeasureAsync/MeasureBlock).
		private static double _blocksTotalSeconds;

		// Чтобы не стартовать общий таймер несколько раз подряд.
		private static bool _appStarted;

		/// <summary>
		/// Старт общего таймера приложения.
		/// Вызывать в самом начале Main, до основной логики.
		/// </summary>
		public static void StartApp ()
			{
			if (_appStarted)
				return;

			_appStarted = true;
			AppStopwatch.Restart ();
			}

		/// <summary>
		/// Остановить общий таймер и вывести сводку:
		/// - Σ блоков (по PerfLogging),
		/// - фактическое wall-clock время работы приложения,
		/// - разницу (overhead / параллелизм),
		/// а также поджать "пустой" скролл консоли.
		/// </summary>
		public static void StopAppAndPrintSummary ()
			{
			if (!_appStarted)
				return;

			AppStopwatch.Stop ();

			var appSeconds = AppStopwatch.Elapsed.TotalSeconds;

			Console.WriteLine ();
			Console.WriteLine ("========== PERF SUMMARY ==========");

			if (_blocksTotalSeconds > 0)
				{
				Console.WriteLine ($"Σ блоков (PerfLogging): {_blocksTotalSeconds:F3} s");
				Console.WriteLine ($"Фактическое время (StartApp → StopApp): {appSeconds:F3} s");
				Console.WriteLine ($"Разница (overhead / параллелизм): {appSeconds - _blocksTotalSeconds:F3} s");
				}
			else
				{
				// Случай, когда блоки не мерялись (но общий таймер всё равно полезен).
				Console.WriteLine ($"Фактическое время (StartApp → StopApp): {appSeconds:F3} s");
				Console.WriteLine ("Σ блоков: нет данных (ни один блок не был измерен через PerfLogging).");
				}

			Console.WriteLine ("==================================");

			// Пытаемся уменьшить пустой скролл снизу.
			TryShrinkConsoleToContent ();

#if DEBUG
			// Свой контролируемый "Press any key", вместо того, что добавляет VS при Ctrl+F5.
			Console.WriteLine ();
			Console.Write ("Нажмите любую клавишу для выхода...");
			Console.ReadKey (intercept: true);
#endif
			}

		/// <summary>
		/// Универсальный scope для измерения блока через using:
		/// using (PerfLogging.MeasureBlock("BlockName")) { ... }
		/// </summary>
		public static IDisposable MeasureBlock ( string blockName )
			{
			if (blockName == null)
				throw new ArgumentNullException (nameof (blockName));

			return new BlockScope (blockName);
			}

		/// <summary>
		/// Синхронный замер блока "вокруг" Action.
		/// Оборачивает вызов в MeasureBlock, чтобы:
		/// - обновить Σ блоков,
		/// - залогировать время этого блока.
		/// Используется в Program для RunSlModelOffline и RunBacktestAndReports.
		/// </summary>
		public static void Measure ( string blockName, Action action )
			{
			if (action == null)
				throw new ArgumentNullException (nameof (action));

			using (MeasureBlock (blockName))
				{
				action ();
				}
			}

		/// <summary>
		/// Синхронный замер блока с возвращаемым значением.
		/// </summary>
		public static T Measure<T> ( string blockName, Func<T> func )
			{
			if (func == null)
				throw new ArgumentNullException (nameof (func));

			using (MeasureBlock (blockName))
				{
				return func ();
				}
			}

		/// <summary>
		/// Асинхронный замер блока без результата.
		/// </summary>
		public static async Task MeasureAsync ( string blockName, Func<Task> func )
			{
			if (func == null)
				throw new ArgumentNullException (nameof (func));

			using (MeasureBlock (blockName))
				{
				// ConfigureAwait(false) — стандартная практика для библиотечного кода,
				// в консольном приложении это не критично, но безопасно.
				await func ().ConfigureAwait (false);
				}
			}

		/// <summary>
		/// Асинхронный замер блока с результатом (используется в Program.Main).
		/// </summary>
		public static async Task<T> MeasureAsync<T> ( string blockName, Func<Task<T>> func )
			{
			if (func == null)
				throw new ArgumentNullException (nameof (func));

			using (MeasureBlock (blockName))
				{
				return await func ().ConfigureAwait (false);
				}
			}

		/// <summary>
		/// Внутренний scope-объект для using-блоков (MeasureBlock).
		/// На вход принимает имя блока, на выходе пишет в консоль время и обновляет Σ блоков.
		/// </summary>
		private sealed class BlockScope : IDisposable
			{
			private readonly string _blockName;
			private readonly Stopwatch _stopwatch;
			private bool _disposed;

			public BlockScope ( string blockName )
				{
				_blockName = blockName;
				_stopwatch = Stopwatch.StartNew ();
				}

			public void Dispose ()
				{
				if (_disposed)
					return;

				_disposed = true;
				_stopwatch.Stop ();

				var seconds = _stopwatch.Elapsed.TotalSeconds;

				// Обновляем суммарное время блоков.
				_blocksTotalSeconds += seconds;

				// Локальный лог по блоку. При желании можно заменить на свой логгер.
				Console.WriteLine ($"{_blockName} loaded in {seconds:F3} s");
				}
			}

		/// <summary>
		/// Попытка "поджать" высоту окна и буфера консоли под реально использованные строки.
		/// Работает в терминологии строк, а не пикселей — другой модели .NET/Console не даёт.
		/// </summary>
		private static void TryShrinkConsoleToContent ()
			{
			try
				{
				// Текущая строка курсора (0-based) → сколько реально строк занято текстом.
				int usedLines = Console.CursorTop + 1;
				if (usedLines <= 0)
					return;

				// Максимально допустимая высота окна в строках.
				int maxWindowHeight = Console.LargestWindowHeight;

				// Целевая высота окна: не больше использованных строк и не больше максимума.
				int targetWindowHeight = Math.Min (usedLines, maxWindowHeight);
				if (targetWindowHeight < 1)
					targetWindowHeight = 1;

				// Сначала задаём высоту окна (иначе SetBufferSize может не пройти из-за WindowHeight).
				if (Console.WindowHeight != targetWindowHeight)
					{
#pragma warning disable CA1416 // Проверка совместимости платформы
					Console.WindowHeight = targetWindowHeight;
#pragma warning restore CA1416 // Проверка совместимости платформы
					}

				// Высота буфера должна быть >= высоты окна, иначе будет ArgumentOutOfRangeException.
				int targetBufferHeight = Math.Max (usedLines, targetWindowHeight);

				if (Console.BufferHeight != targetBufferHeight)
					{
#pragma warning disable CA1416 // Проверка совместимости платформы
					Console.SetBufferSize (Console.BufferWidth, targetBufferHeight);
#pragma warning restore CA1416 // Проверка совместимости платформы
					}
				}
			catch
				{
				// В хостах типа VS / Rider / некоторых терминалов управление размером
				// может быть частично запрещено или игнорироваться — в этом случае просто молча пропускаем.
				}
			}
		}
	}
