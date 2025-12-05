using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Micro;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Rows;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.SL;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Pnl;

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
		/// В прод-запуске (Program.Main) обычно выполняются:
		/// - дневная утечка (daily);
		/// - микро-слой;
		/// - SL-слой,
		/// если для них есть достаточно данных в контексте.
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

			// Дополнительная диагностика: bare-PnL по PredictionRecord + shuffle.
			// Не влияет на SelfCheckResult, только пишет подробный лог в консоль.
			if (ctx.Records != null && ctx.Records.Count > 0)
				{
				DailyBarePnlChecks.LogDailyBarePnlWithBaselinesAndShuffle (
					ctx.Records,
					ctx.TrainUntilUtc,
					shuffleRuns: 20);
				}

			// === 2. Микро-слой (flat-модель) ===
			// Запускаем только если в контексте реально есть данные,
			// чтобы не ломать SelfCheckRunnerTests, где заполнены только Records/TrainUntilUtc.
			if (ctx.Mornings != null
				&& ctx.Mornings.Count > 0
				&& ctx.Sol1m != null
				&& ctx.Sol1m.Count > 0)
				{
				results.Add (MicroLeakageChecks.CheckMicroLayer (ctx));
				}

			// === 3. SL-слой (риск "SL-first" по path-based исходам) ===
			if (ctx.Records != null
				&& ctx.Records.Count > 0
				&& ctx.SolAll6h != null
				&& ctx.SolAll6h.Count > 0
				&& ctx.SolAll1h != null
				&& ctx.SolAll1h.Count > 0)
				{
				results.Add (SlLeakageChecks.CheckSlLayer (ctx));
				}

			// === 1. Дневная модель + OOS / shuffle ===
			results.Add (
				DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle (
					ctx.Records,
					ctx.TrainUntilUtc));

			// === 1a. Фичи RowBuilder против future-полей (SolFwd1, MaxHigh24 и т.п.) ===
			results.Add (
				RowFeatureLeakageChecks.CheckRowFeaturesAgainstFuture (ctx));


			// === 4. Агрегация ===
			// SelfCheckResult.Aggregate уже знает, как объединять Success/Errors/Warnings/Metrics.
			var aggregate = SelfCheckResult.Aggregate (results);
			return Task.FromResult (aggregate);
			}
		}
	}
