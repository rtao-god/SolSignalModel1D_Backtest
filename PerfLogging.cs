using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		// Простой измеритель для синхронных блоков.
		// Зачем: один раз оборачивать блоки кода и видеть их длительность в логах.
		// Альтернатива: вручную писать Stopwatch в каждом месте (труднее поддерживать).
		private static void Measure ( string name, Action action )
			{
			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {name}... start");
			try
				{
				action ();
				}
			finally
				{
				sw.Stop ();
				Console.WriteLine ($"[perf] {name} done in {sw.Elapsed.TotalSeconds:F1}s");
				}
			}

		// Вариант с возвращаемым значением.
		private static T Measure<T> ( string name, Func<T> func )
			{
			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {name}... start");
			try
				{
				return func ();
				}
			finally
				{
				sw.Stop ();
				Console.WriteLine ($"[perf] {name} done in {sw.Elapsed.TotalSeconds:F1}s");
				}
			}

		// Асинхронный вариант без результата.
		private static async Task MeasureAsync ( string name, Func<Task> func )
			{
			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {name}... start");
			try
				{
				await func ();
				}
			finally
				{
				sw.Stop ();
				Console.WriteLine ($"[perf] {name} done in {sw.Elapsed.TotalSeconds:F1}s");
				}
			}

		// Асинхронный вариант с результатом.
		private static async Task<T> MeasureAsync<T> ( string name, Func<Task<T>> func )
			{
			var sw = Stopwatch.StartNew ();
			Console.WriteLine ($"[perf] {name}... start");
			try
				{
				return await func ();
				}
			finally
				{
				sw.Stop ();
				Console.WriteLine ($"[perf] {name} done in {sw.Elapsed.TotalSeconds:F1}s");
				}
			}
		}
	}
