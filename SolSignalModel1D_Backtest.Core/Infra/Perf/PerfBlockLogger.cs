using System.Diagnostics;

namespace SolSignalModel1D_Backtest.Core.Infra.Perf
	{
	/// <summary>
	/// Локальные [perf]-логгеры блоков:
	/// - логируют "[perf] name... start/done in Xs";
	/// - НЕ знают про глобальную сводку (её делает PerfLogging).
	/// </summary>
	public static class PerfBlockLogger
		{
		/// <summary>
		/// Синхронный замер блока без возвращаемого значения.
		/// Формат логов сохраняет старое поведение:
		/// [perf] name... start
		/// [perf] name done in 68,9s
		/// </summary>
		public static void Measure ( string blockName, Action action )
			{
			if (blockName == null) throw new ArgumentNullException (nameof (blockName));
			if (action == null) throw new ArgumentNullException (nameof (action));

			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {blockName}... start");

			action ();

			sw.Stop ();
			Console.WriteLine ($"[perf] {blockName} done in {sw.Elapsed.TotalSeconds:F1}s");
			}

		/// <summary>
		/// Синхронный замер блока с возвращаемым значением.
		/// </summary>
		public static T Measure<T> ( string blockName, Func<T> func )
			{
			if (blockName == null) throw new ArgumentNullException (nameof (blockName));
			if (func == null) throw new ArgumentNullException (nameof (func));

			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {blockName}... start");

			var result = func ();

			sw.Stop ();
			Console.WriteLine ($"[perf] {blockName} done in {sw.Elapsed.TotalSeconds:F1}s");

			return result;
			}

		/// <summary>
		/// Асинхронный замер блока без результата.
		/// </summary>
		public static async Task MeasureAsync ( string blockName, Func<Task> func )
			{
			if (blockName == null) throw new ArgumentNullException (nameof (blockName));
			if (func == null) throw new ArgumentNullException (nameof (func));

			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {blockName}... start");

			await func ().ConfigureAwait (false);

			sw.Stop ();
			Console.WriteLine ($"[perf] {blockName} done in {sw.Elapsed.TotalSeconds:F1}s");
			}

		/// <summary>
		/// Асинхронный замер блока с результатом.
		/// </summary>
		public static async Task<T> MeasureAsync<T> ( string blockName, Func<Task<T>> func )
			{
			if (blockName == null) throw new ArgumentNullException (nameof (blockName));
			if (func == null) throw new ArgumentNullException (nameof (func));

			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {blockName}... start");

			var result = await func ().ConfigureAwait (false);

			sw.Stop ();
			Console.WriteLine ($"[perf] {blockName} done in {sw.Elapsed.TotalSeconds:F1}s");

			return result;
			}
		}
	}
