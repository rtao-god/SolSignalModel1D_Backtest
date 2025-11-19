// Core/Utils/ConsoleProgress.cs
using System;

namespace SolSignalModel1D_Backtest.Core.Utils
	{
	/// <summary>
	/// Утилита для вывода процента загрузки по шагам пайплайна.
	/// Используется ConsoleBlockTimer после завершения блока.
	/// </summary>
	public static class ConsoleProgress
		{
		private static readonly object ConsoleLock = new object ();

		/// <summary>
		/// Печатает строку вида:
		/// [ 25% загружено] candles: update SOL/BTC/PAXG (за 1.234s)
		/// Процент считается как stepIndex / totalSteps.
		/// </summary>
		public static void PrintStep ( string title, int stepIndex, int totalSteps, TimeSpan elapsed )
			{
			if (totalSteps <= 0) totalSteps = 1;
			if (stepIndex < 0) stepIndex = 0;
			if (stepIndex > totalSteps) stepIndex = totalSteps;

			int percent = (int) Math.Round (stepIndex * 100.0 / totalSteps);

			string formattedTime = elapsed.TotalSeconds >= 1.0
				? $"{elapsed.TotalSeconds:0.000}s"
				: $"{elapsed.TotalMilliseconds:0}ms";

			title ??= string.Empty;

			lock (ConsoleLock)
				{
				Console.WriteLine ($"\r[{percent,3}% загружено] {title} (за {formattedTime})          ");
				}
			}
		}
	}
