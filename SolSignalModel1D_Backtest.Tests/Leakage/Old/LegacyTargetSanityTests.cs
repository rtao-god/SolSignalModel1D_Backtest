using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;
using AppProgram = SolSignalModel1D_Backtest.Program;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Old
	{
	/// <summary>
	/// Диагностические sanity-тесты:
	/// 1) path-based таргет vs старый 24h close;
	/// 2) влияние MinMove (новый адаптивный vs старый рецепт).
	/// Хранятся в Old, чтобы можно было легко удалить.
	/// </summary>
	public class LegacyTargetSanityTests
		{
		[Fact]
		public async Task PathBasedVsLegacy24hTarget_Sanity ()
			{
			// Один Bootstrap: сначала гоняем path-based, потом на тех же рядах пересчитываем 24h-лейблы
			var (allRows, mornings, solAll6h, _, _) =
				await BootstrapRowsAndCandlesAsync ();

			// --- прогон №1: текущий path-based Label (как в проде) ---
			var pathRun = await RunDailyModelAsync (
				allRows,
				mornings,
				solAll6h);

			var accPath = ComputeOosAccuracy (pathRun.Records, pathRun.TrainUntilUtc);

			// --- прогон №2: 24h-таргет по close с тем же MinMove (r.MinMove) ---
			ApplyLegacy24hLabelsUsingCurrentMinMove (
				allRows,
				mornings,
				solAll6h);

			var legacyRun = await RunDailyModelAsync (
				allRows,
				mornings,
				solAll6h);

			var accLegacy = ComputeOosAccuracy (legacyRun.Records, legacyRun.TrainUntilUtc);

			Console.WriteLine (
				$"[legacy-test] OOS Accuracy path-based label (prod) = {accPath:0.000}");
			Console.WriteLine (
				$"[legacy-test] OOS Accuracy 24h-close label (MinMove=new) = {accLegacy:0.000}");

			Assert.True (!double.IsNaN (accPath), "Path-based OOS Accuracy is NaN.");
			Assert.True (!double.IsNaN (accLegacy), "Legacy-24h OOS Accuracy is NaN.");
			}

		[Fact]
		public async Task LegacyMinMoveVsAdaptiveMinMoveOn24hTarget_Sanity ()
			{
			// Один Bootstrap: на одних и тех же рядах сначала считаем 24h+новый MinMove,
			// затем поверх них пересчитываем 24h+старый MinMove.
			var (allRows, mornings, solAll6h, _, _) =
				await BootstrapRowsAndCandlesAsync ();

			// --- прогон №1: 24h + текущий MinMove (адаптивный) ---
			ApplyLegacy24hLabelsUsingCurrentMinMove (
				allRows,
				mornings,
				solAll6h);

			var adaptiveRun = await RunDailyModelAsync (
				allRows,
				mornings,
				solAll6h);

			var accAdaptive = ComputeOosAccuracy (
				adaptiveRun.Records,
				adaptiveRun.TrainUntilUtc);

			// --- прогон №2: 24h + "старый" MinMove (формула ATR+dynVol+месяц) ---
			ApplyLegacy24hLabelsUsingLegacyMinMove (
				allRows,
				mornings,
				solAll6h);

			var legacyRun = await RunDailyModelAsync (
				allRows,
				mornings,
				solAll6h);

			var accLegacy = ComputeOosAccuracy (
				legacyRun.Records,
				legacyRun.TrainUntilUtc);

			Console.WriteLine (
				$"[minmove-test] OOS Accuracy 24h-close, MinMove=new  = {accAdaptive:0.000}");
			Console.WriteLine (
				$"[minmove-test] OOS Accuracy 24h-close, MinMove=old  = {accLegacy:0.000}");

			Assert.True (!double.IsNaN (accAdaptive), "Adaptive-MinMove OOS Accuracy is NaN.");
			Assert.True (!double.IsNaN (accLegacy), "Legacy-MinMove OOS Accuracy is NaN.");
			}

		// ---------------------------------------------------------------------
		// Вспомогательные методы
		// ---------------------------------------------------------------------

		/// <summary>
		/// Использует debug-фасад Program.DebugBootstrapRowsAndCandlesAsync
		/// вместо рефлексии.
		/// </summary>
		internal static async Task<(
			List<DataRow> AllRows,
			List<DataRow> Mornings,
			List<Candle6h> SolAll6h,
			List<Candle1h> SolAll1h,
			List<Candle1m> Sol1m)> BootstrapRowsAndCandlesAsync ()
			{
			// В тестах отключаем любые обновления свечей, чтобы:
			// - не ходить в сеть;
			// - не ловить ошибки диапазона (toUtc < fromUtc) из CandleDailyUpdater.
			AppProgram.DebugSkipCandleUpdatesForTests = true;
			try
				{
				return await AppProgram.DebugBootstrapRowsAndCandlesAsync ();
				}
			finally
				{
				AppProgram.DebugSkipCandleUpdatesForTests = false;
				}
			}

		private sealed class DailyModelRunResult
			{
			public required DateTime TrainUntilUtc { get; init; }
			public required List<BacktestRecord> Records { get; init; }
			}

		/// <summary>
		/// Мини-версия продового пайплайна дневной модели:
		/// - holdout 120 дней;
		/// - ModelTrainer;
		/// - PredictionEngine;
		/// - forward-метрики по 6h до baseline-exit.
		/// </summary>
		private static async Task<DailyModelRunResult> RunDailyModelAsync (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));

			PredictionEngine.DebugAllowDisabledModels = true;

			var ordered = allRows
				.OrderBy (r => r.Date)
				.ToList ();

			if (ordered.Count == 0)
				throw new InvalidOperationException ("RunDailyModelAsync: пустой allRows.");

			var maxDate = ordered[^1].Date;

			const int HoldoutDays = 120;
			var trainUntil = maxDate.AddDays (-HoldoutDays);

			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			if (trainRows.Count < 100)
				{
				// мало данных — учимся на всей истории, как в проде
				trainRows = ordered;
				trainUntil = ordered[^1].Date;
				}

			var trainer = new ModelTrainer
				{
				DisableMoveModel = false,
				DisableDirNormalModel = false,
				DisableDirDownModel = true,
				DisableMicroFlatModel = false
				};

			var bundle = trainer.TrainAll (trainRows);

			if (bundle.MlCtx == null)
				throw new InvalidOperationException (
					"ModelTrainer вернул ModelBundle с MlCtx == null.");

			var engine = new PredictionEngine (bundle);

			var records = await BuildPredictionRecordsAsyncForTests (
				mornings,
				solAll6h,
				engine);

			return new DailyModelRunResult
				{
				TrainUntilUtc = trainUntil,
				Records = records
				};
			}

		/// <summary>
		/// Строит PredictionRecord'ы для всех mornings:
		/// - Predict через PredictionEngine;
		/// - forward-окно до baseline-exit по 6h (maxHigh/minLow/close).
		/// </summary>
		private static async Task<List<BacktestRecord>> BuildPredictionRecordsAsyncForTests (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<Candle6h> solAll6h,
			PredictionEngine engine )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (engine == null) throw new ArgumentNullException (nameof (engine));

			var sorted6h = solAll6h is List<Candle6h> list6h
				? list6h
				: solAll6h.ToList ();

			if (sorted6h.Count == 0)
				throw new InvalidOperationException (
					"[forward-test] Пустая серия 6h для SOL.");

			var indexByOpenTime = new Dictionary<DateTime, int> (sorted6h.Count);
			for (int i = sorted6h.Count - 1; i >= 0; i--)
				indexByOpenTime[sorted6h[i].OpenTimeUtc] = i;

			var nyTz = Windowing.NyTz;
			var list = new List<BacktestRecord> (mornings.Count);

			foreach (var r in mornings)
				{
				var pr = engine.Predict (r);

				var entryUtc = r.Date;
				var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

				if (!indexByOpenTime.TryGetValue (entryUtc, out var entryIdx))
					{
					throw new InvalidOperationException (
						$"[forward-test] entry candle {entryUtc:O} not found in 6h series.");
					}

				var exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					var end = (i + 1 < sorted6h.Count)
						? sorted6h[i + 1].OpenTimeUtc
						: start.AddHours (6);

					// [start; end) — выровнено с RowBuilder
					if (exitUtc >= start && exitUtc < end)
						{
						exitIdx = i;
						break;
						}
					}

				if (exitIdx < 0)
					{
					throw new InvalidOperationException (
						$"[forward-test] no 6h candle covering baseline exit {exitUtc:O} (entry {entryUtc:O}).");
					}

				if (exitIdx <= entryIdx)
					{
					throw new InvalidOperationException (
						$"[forward-test] exitIdx {exitIdx} <= entryIdx {entryIdx} для entry {entryUtc:O}.");
					}

				var entryPrice = sorted6h[entryIdx].Close;

				double maxHigh = double.MinValue;
				double minLow = double.MaxValue;

				for (int j = entryIdx + 1; j <= exitIdx; j++)
					{
					var c = sorted6h[j];
					if (c.High > maxHigh) maxHigh = c.High;
					if (c.Low < minLow) minLow = c.Low;
					}

				if (maxHigh == double.MinValue || minLow == double.MaxValue)
					{
					throw new InvalidOperationException (
						$"[forward-test] no candles between entry {entryUtc:O} and exit {exitUtc:O}.");
					}

				var fwdClose = sorted6h[exitIdx].Close;

				list.Add (new PredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,
					PredLabel = pr.Class,

					PredMicroUp = pr.Micro.ConsiderUp,
					PredMicroDown = pr.Micro.ConsiderDown,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					Entry = entryPrice,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,

					RegimeDown = r.RegimeDown,
					Reason = pr.Reason,
					MinMove = r.MinMove,

					DelayedSource = string.Empty,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedEntryExecuted = false,
					DelayedEntryPrice = 0.0,
					DelayedIntradayResult = 0,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,
					TargetLevelClass = 0,
					DelayedWhyNot = null,
					DelayedEntryExecutedAtUtc = null,
					SlProb = 0.0,
					SlHighDecision = false
					});
				}

			return await Task.FromResult (list);
			}

		private static double ComputeOosAccuracy (
			List<BacktestRecord> records,
			DateTime trainUntilUtc )
			{
			var oos = records
				.Where (r => r.DateUtc > trainUntilUtc)
				.ToList ();

			if (oos.Count == 0)
				return double.NaN;

			var correct = oos.Count (r => r.PredLabel == r.TrueLabel);
			return correct / (double) oos.Count;
			}

		private static void ApplyLegacy24hLabelsUsingCurrentMinMove (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));

			var dict6h = solAll6h.ToDictionary (c => c.OpenTimeUtc);
			var validDates = new HashSet<DateTime> ();

			foreach (var r in allRows)
				{
				if (!dict6h.TryGetValue (r.Date, out var c0))
					continue;

				var t4 = r.Date.AddHours (24);
				if (!dict6h.TryGetValue (t4, out var c4))
					continue;

				var solClose = c0.Close;
				var solCloseFwd = c4.Close;
				if (solClose <= 0 || solCloseFwd <= 0)
					continue;

				var solFwd24 = solCloseFwd / solClose - 1.0;
				var minMove = r.MinMove;

				int label =
					solFwd24 <= -minMove ? 0 :
					solFwd24 >= +minMove ? 2 :
					1;

				r.Label = label;
				validDates.Add (r.Date);
				}

			if (validDates.Count == 0)
				throw new InvalidOperationException (
					"[legacy-test] нет ни одного дня с полным 24h-окном (MinMove=new).");

			allRows.RemoveAll (r => !validDates.Contains (r.Date));
			mornings.RemoveAll (r => !validDates.Contains (r.Date));
			}

		private static void ApplyLegacy24hLabelsUsingLegacyMinMove (
			List<DataRow> allRows,
			List<DataRow> mornings,
			List<Candle6h> solAll6h )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));

			var dict6h = solAll6h.ToDictionary (c => c.OpenTimeUtc);
			var validDates = new HashSet<DateTime> ();

			foreach (var r in allRows)
				{
				if (!dict6h.TryGetValue (r.Date, out var c0))
					continue;

				var t4 = r.Date.AddHours (24);
				if (!dict6h.TryGetValue (t4, out var c4))
					continue;

				var solClose = c0.Close;
				var solCloseFwd = c4.Close;
				if (solClose <= 0 || solCloseFwd <= 0)
					continue;

				var solFwd24 = solCloseFwd / solClose - 1.0;

				var legacyMinMove = ComputeLegacyMinMove (
					asOfUtc: r.Date,
					atrPct: r.AtrPct,
					dynVol: r.DynVol,
					solAll6h: solAll6h);

				int label =
					solFwd24 <= -legacyMinMove ? 0 :
					solFwd24 >= +legacyMinMove ? 2 :
					1;

				r.Label = label;
				r.MinMove = legacyMinMove;
				validDates.Add (r.Date);
				}

			if (validDates.Count == 0)
				throw new InvalidOperationException (
					"[legacy-test] нет ни одного дня с полным 24h-окном (MinMove=old).");

			allRows.RemoveAll (r => !validDates.Contains (r.Date));
			mornings.RemoveAll (r => !validDates.Contains (r.Date));
			}

		private static double ComputeLegacyMinMove (
			DateTime asOfUtc,
			double atrPct,
			double dynVol,
			List<Candle6h> solAll6h )
			{
			const double DailyMinMoveFloor = 0.0075;
			const double DailyMinMoveCap = 0.036;
			const double DailyMinMoveAtrMul = 1.10;
			const double DailyMinMoveVolMul = 1.10;

			const double MonthlyCapMin = 0.75;
			const double MonthlyCapMax = 1.35;

			if (dynVol <= 0)
				dynVol = 0.004;

			double baseMinMove = Math.Max (
				DailyMinMoveFloor,
				Math.Max (
					atrPct * DailyMinMoveAtrMul,
					dynVol * DailyMinMoveVolMul));

			double monthFactor = ComputeMonthlyMinMoveFactorLegacy (
				asOf: asOfUtc,
				all: solAll6h,
				defaultFactor: 1.0);

			monthFactor = Math.Clamp (monthFactor, MonthlyCapMin, MonthlyCapMax);

			double minMove = Math.Min (
				baseMinMove * monthFactor,
				DailyMinMoveCap);

			return minMove;
			}

		private static double ComputeMonthlyMinMoveFactorLegacy (
			DateTime asOf,
			List<Candle6h> all,
			double defaultFactor )
			{
			DateTime from = asOf.AddDays (-30);
			var seg = all
				.Where (c => c.OpenTimeUtc >= from && c.OpenTimeUtc <= asOf)
				.ToList ();

			if (seg.Count < 10)
				return defaultFactor;

			double sumRange = 0.0;
			int cnt = 0;

			foreach (var c in seg)
				{
				double range = c.High - c.Low;
				if (range > 0 && c.Close > 0)
					{
					sumRange += range / c.Close;
					cnt++;
					}
				}

			if (cnt == 0)
				return defaultFactor;

			double monthVol = sumRange / cnt;

			// 2.5% считаем "нормой", как в старом коде.
			double factor = monthVol / 0.025;
			return factor;
			}
		}
	}
