using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
	{
	/// <summary>
	/// Единая точка входа для self-check'ов.
	/// Никаких зависимостей от консоли/UI — только расчёты и флаги.
	/// </summary>
	public static class SelfCheckRunner
		{
		/// <summary>
		/// Запускает набор sanity-проверок на уже собранных артефактах пайплайна.
		/// Пока все проверки синхронные, но оставлен async-API для расширения.
		/// </summary>
		public static Task<SelfCheckResult> RunAsync (
			SelfCheckContext ctx,
			CancellationToken cancellationToken = default )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			var results = new List<SelfCheckResult> ();

			// === 1. Дневная модель + OOS / shuffle ===
			results.Add (
				DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
					ctx.Records,
					ctx.TrainUntilUtc));

			// === 2. SL / micro / окна / shuffle ===
			// Здесь позже добавляются:
			// - SlLeakageChecks.Check(...);
			// - MicroLeakageChecks.Check(...);
			// - WindowingChecks.Check(...);
			// - BacktestLeakageChecks.Check(...).

			var aggregate = SelfCheckResult.Aggregate (results);
			return Task.FromResult (aggregate);
			}
		}
	}
