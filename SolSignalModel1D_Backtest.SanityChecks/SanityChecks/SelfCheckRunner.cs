using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Micro;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Rows;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.SL;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Pnl;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
		/// </summary>
		public static Task<SelfCheckResult> RunAsync (
			SelfCheckContext ctx,
			CancellationToken cancellationToken = default )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			var results = new List<SelfCheckResult> ();

			// =====================================================================
			// 1) Daily: train/OOS + shuffle.
			// Важно: не передавать null в методы, которые ожидают non-null records.
			// =====================================================================
			if (ctx.Records != null && ctx.Records.Count > 0)
				{
				var boundary = new TrainBoundary (ctx.TrainUntilUtc, ctx.NyTz);

				results.Add (
					DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
						ctx.Records,
						boundary));

				// Доп. диагностика (лог в консоль): bare-PnL + shuffle.
				DailyBarePnlChecks.LogDailyBarePnlWithBaselinesAndShuffle (
					ctx.Records,
					ctx.TrainUntilUtc,
					ctx.NyTz,
					shuffleRuns: 20);
				}
			else
				{
				// Стабильное поведение: агрегатору есть что объединять, но проверки явно пропущены.
				results.Add (SelfCheckResult.Ok ("[daily] records отсутствуют — дневные проверки пропущены."));
				}

			// =====================================================================
			// 2) Micro-layer
			// =====================================================================
			if (ctx.Mornings != null
				&& ctx.Mornings.Count > 0
				&& ctx.Sol1m != null
				&& ctx.Sol1m.Count > 0)
				{
				results.Add (MicroLeakageChecks.CheckMicroLayer (ctx));
				}

			// =====================================================================
			// 3) SL-layer
			// =====================================================================
			if (ctx.Records != null
				&& ctx.Records.Count > 0
				&& ctx.SolAll6h != null
				&& ctx.SolAll6h.Count > 0
				&& ctx.SolAll1h != null
				&& ctx.SolAll1h.Count > 0)
				{
				results.Add (SlLeakageChecks.CheckSlLayer (ctx));
				}

			// =====================================================================
			// 4) Row features vs future-fields
			// =====================================================================
			results.Add (RowFeatureLeakageChecks.CheckRowFeaturesAgainstFuture (ctx));

			// =====================================================================
			// 5) Aggregation
			// =====================================================================
			var aggregate = SelfCheckResult.Aggregate (results);
			return Task.FromResult (aggregate);
			}
		}
	}
