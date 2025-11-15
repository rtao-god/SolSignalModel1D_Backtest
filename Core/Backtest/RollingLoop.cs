using SolSignalModel1D_Backtest.Core.Analytics;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public sealed class RollingLoop
		{
		private const int RollingTrainDays = 260;
		private const int RollingTestDays = 60;

		private const int SlMinTrainSamples = 80;
		private const int SlRetrainEvery = 30;

		private const int AMinTrainSamples = 80;
		private const int ARetrainEvery = 30;

		private const int BMinTrainSamples = 80;
		private const int BRetrainEvery = 30;

		public async Task RunAsync (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict,
			List<SlHitSample> slOffline,
			List<PullbackContinuationSample> pullbackOffline,
			List<SmallImprovementSample> smallOffline )
			{
			await Task.CompletedTask;

			var diagnostics = new BacktestDiagnostics ();

			if (sol1h == null || sol1h.Count == 0)
				diagnostics.Add ("[fatal-ish] no 1h candles provided — liquidation checks are rough.");

			var mornings = rows.Where (r => r.IsMorning).OrderBy (r => r.Date).ToList ();
			if (mornings.Count == 0)
				{
				Console.WriteLine ("[warn] no morning rows for rolling");
				return;
				}

			var stats = new ExecutionStats ();
			var allRecords = new List<PredictionRecord> ();

			var slState = new SlOnlineState
				{
				Trainer = new SlFirstTrainer (),
				MinTrainSamples = SlMinTrainSamples,
				RetrainEvery = SlRetrainEvery
				};

			var pullbackState = new PullbackContinuationOnlineState
				{
				Trainer = new PullbackContinuationTrainer (),
				MinTrainSamples = AMinTrainSamples,
				RetrainEvery = ARetrainEvery
				};

			var smallState = new SmallImprovementOnlineState
				{
				Trainer = new SmallImprovementTrainer (),
				MinTrainSamples = BMinTrainSamples,
				RetrainEvery = BRetrainEvery
				};

			DateTime minDate = mornings.First ().Date;
			DateTime maxDate = mornings.Last ().Date;
			DateTime cursor = minDate.AddDays (RollingTrainDays);

			ConsoleStyler.WriteHeader ("==== ROLLING ====");

			while (true)
				{
				DateTime trainStart = cursor.AddDays (-RollingTrainDays);
				DateTime trainEnd = cursor;
				DateTime testEnd = cursor.AddDays (RollingTestDays);

				var trainRows = rows
					.Where (r => r.Date >= trainStart && r.Date < trainEnd)
					.OrderBy (r => r.Date)
					.ToList ();

				var testRows = rows
					.Where (r => r.IsMorning && r.Date >= trainEnd && r.Date < testEnd)
					.OrderBy (r => r.Date)
					.ToList ();

				if (testRows.Count == 0)
					{
					cursor = cursor.AddDays (RollingTestDays);
					if (cursor >= maxDate) break;
					continue;
					}

				var trainer = new ModelTrainer ();
				var testDates = new HashSet<DateTime> (testRows.Select (t => t.Date));
				var bundle = trainer.TrainAll (trainRows, testDates);
				var engine = new PredictionEngine (bundle);

				ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
				{
					Console.WriteLine ($"[roll] train {trainStart:yyyy-MM-dd}..{trainEnd:yyyy-MM-dd}, test {trainEnd:yyyy-MM-dd}..{testEnd:yyyy-MM-dd}");
				});

				var pastSl = slOffline.Where (s => s.EntryUtc < trainEnd).ToList ();
				slState.TryRetrain (pastSl, trainEnd);

				var pastA = pullbackOffline.Where (s => s.EntryUtc < trainEnd).ToList ();
				pullbackState.TryRetrain (pastA, trainEnd);

				var pastB = smallOffline.Where (s => s.EntryUtc < trainEnd).ToList ();
				smallState.TryRetrain (pastB, trainEnd);

				foreach (var dayRow in testRows)
					{
					var rec = DayExecutor.ProcessDay (
						dayRow,
						engine,
						sol1h,
						sol6hDict,
						slState,
						pullbackState,
						smallState,
						stats
					);

					if (rec.Entry <= 0)
						diagnostics.AddBadRecord (rec.DateUtc, "entry <= 0");
					if (rec.Close24 <= 0)
						diagnostics.AddBadRecord (rec.DateUtc, "close24 <= 0");

					allRecords.Add (rec);
					}

				cursor = cursor.AddDays (RollingTestDays);
				if (cursor >= maxDate) break;
				}

			var policyResults = new List<BacktestPolicyResult> ();
			var policies = new List<ILeveragePolicy>
			{
			new LeveragePolicies.ConstPolicy("const_2x", 2.0),
			new LeveragePolicies.ConstPolicy("const_5x", 5.0),
			new LeveragePolicies.ConstPolicy("const_10x", 10.0),
			new LeveragePolicies.ConstPolicy("const_15x", 15.0),
			new LeveragePolicies.ConstPolicy("const_50x", 50.0),
			new LeveragePolicies.RiskAwarePolicy(),
			new LeveragePolicies.UltraSafePolicy()
			};

			var margins = new[] { MarginMode.Cross, MarginMode.Isolated };

			foreach (var pol in policies)
				{
				foreach (var margin in margins)
					{
					PnlCalculator.ComputePnL (
						allRecords,
						sol1h,
						pol,
						margin,
						out var pnlTrades,
						out var totalPnlPct,
						out var maxDdPct,
						out var tradesBySource,
						out var withdrawnTotal,
						out var bucketSnapshots,
						out var hadLiq
					);

					policyResults.Add (new BacktestPolicyResult
						{
						PolicyName = pol.Name,
						Margin = margin,
						Trades = pnlTrades,
						TotalPnlPct = totalPnlPct,
						MaxDdPct = maxDdPct,
						TradesBySource = tradesBySource,
						WithdrawnTotal = withdrawnTotal,
						BucketSnapshots = bucketSnapshots,
						HadLiquidation = hadLiq
						});
					}
				}

			// 1) вывод по политикам
			BacktestReportBuilder.PrintPolicies (allRecords, policyResults);

			// 2) подробная статистика по моделям (дневная / микры / delayed / SL-conf)
			BacktestModelStatsPrinter.Print (allRecords);

			// 3) диагностика (потерянные свечи и т.п.)
			diagnostics.Print ();

			// 4) что насчитали по онлайновым счётчикам
			Console.WriteLine ();
			stats.Print ();

			Console.WriteLine ();
			Console.WriteLine ($"[sl-offline] total samples: {slOffline.Count}");
			Console.WriteLine ($"[pullback-offline] total samples: {pullbackOffline.Count}");
			Console.WriteLine ($"[small-offline] total samples: {smallOffline.Count}");
			}
		}
	}
