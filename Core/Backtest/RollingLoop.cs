using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Analytics;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public sealed class RollingLoop
		{
		private const int RollingTrainDays = 260;
		private const int RollingTestDays = 60;
		private const double TpPct = 0.03;

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

			var mornings = rows.Where (r => r.IsMorning).OrderBy (r => r.Date).ToList ();
			if (mornings.Count == 0)
				{
				Console.WriteLine ("[warn] no morning rows for rolling");
				return;
				}

			var stats = new ExecutionStats ();
			var allRecords = new List<PredictionRecord> ();

			// SL
			var slState = new SlOnlineState
				{
				Trainer = new SlFirstTrainer (),
				MinTrainSamples = SlMinTrainSamples,
				RetrainEvery = SlRetrainEvery
				};

			// A
			var pullbackState = new PullbackContinuationOnlineState
				{
				Trainer = new PullbackContinuationTrainer (),
				MinTrainSamples = AMinTrainSamples,
				RetrainEvery = ARetrainEvery
				};

			// B
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

				// дневная модель — как раньше, исключаем тестовые даты
				var trainer = new ModelTrainer ();
				var testDates = new HashSet<DateTime> (testRows.Select (t => t.Date));
				var bundle = trainer.TrainAll (trainRows, testDates);
				var engine = new PredictionEngine (bundle);

				ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
				{
					Console.WriteLine ($"[roll] train {trainStart:yyyy-MM-dd}..{trainEnd:yyyy-MM-dd}, test {trainEnd:yyyy-MM-dd}..{testEnd:yyyy-MM-dd}");
				});

				// SL-онлайн — каузально: берём только оффлайн-сделки до trainEnd
				var pastSl = slOffline.Where (s => s.EntryUtc < trainEnd).ToList ();
				slState.TryRetrain (pastSl, trainEnd);

				// A-онлайн
				var pastA = pullbackOffline.Where (s => s.EntryUtc < trainEnd).ToList ();
				pullbackState.TryRetrain (pastA, trainEnd);

				// B-онлайн
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

					allRecords.Add (rec);
					}

				// отладочный вывод по последнему дню окна
				var lastDay = testRows.OrderByDescending (x => x.Date).First ();
				var fwd = BacktestHelpers.GetForwardInfo (lastDay.Date, sol6hDict);
				var lastPred = engine.Predict (lastDay);
				int dbgCls = lastPred.Item1;
				string dbgReason = lastPred.Item3;
				var dbgMicro = lastPred.Item4;
				PrintHelpers.PrintDebugDay (lastDay, fwd, dbgCls, dbgMicro, dbgReason);

				cursor = cursor.AddDays (RollingTestDays);
				if (cursor >= maxDate) break;
				}

			// ===== summary =====
			Console.WriteLine ();
			ConsoleStyler.WriteHeader ("==== SUMMARY ====");
			Console.WriteLine ($"total tested: {allRecords.Count}");

			// классификация
			var clsRes = ClassificationMetrics.Compute (allRecords, useMicro: true);
			ConsoleStyler.WriteHeader ("=== Classification (micro-aware) ===");
			var tCls = new TextTable ();
			tCls.AddHeader ("metric", "value");
			tCls.AddColoredRow (clsRes.Accuracy >= 0.5 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor,
				"accuracy, %", (clsRes.Accuracy * 100.0).ToString ("0.0"));
			tCls.AddRow ("macro F1, %", (clsRes.MacroF1 * 100.0).ToString ("0.0"));
			tCls.AddRow ("micro F1, %", (clsRes.MicroF1 * 100.0).ToString ("0.0"));
			tCls.WriteToConsole ();

			// дневной tp-or-close
			var tr = TradingMetrics.Compute (allRecords, TpPct);
			Console.WriteLine ();
			ConsoleStyler.WriteHeader ($"=== Trading (tp-or-close, {TpPct * 100:0.#}%) ===");
			var tTr = new TextTable ();
			tTr.AddHeader ("metric", "value");
			tTr.AddColoredRow (tr.TotalPnlPct >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor,
				"Total PnL, %", tr.TotalPnlPct.ToString ("0.0"));
			tTr.AddRow ("Total PnL, x", tr.TotalPnlMultiplier.ToString ("0.00"));
			tTr.AddColoredRow (tr.MaxDrawdownPct <= 30 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor,
				"Max DD, %", tr.MaxDrawdownPct.ToString ("0.0"));
			tTr.AddRow ("Trades (opened)", tr.Trades.ToString ());
			tTr.AddRow ("tp-hit overall, %", (tr.TpTotal == 0 ? 0.0 : 100.0 * tr.TpHits / tr.TpTotal).ToString ("0.0"));
			tTr.WriteToConsole ();

			// почасовой "сыро"
			if (sol1h != null && sol1h.Count > 0)
				{
				var hourly = HourlyTradeEvaluator.Evaluate (allRecords, sol1h);
				Console.WriteLine ();
				ConsoleStyler.WriteHeader ("=== Trading WITH hourly TP/SL (raw) ===");
				var tHr = new TextTable ();
				tHr.AddHeader ("metric", "value");
				tHr.AddColoredRow (hourly.TotalPnlPct >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor,
					"Total PnL, %", hourly.TotalPnlPct.ToString ("0.0"));
				tHr.AddRow ("Total PnL, x", hourly.TotalPnlMultiplier.ToString ("0.00"));
				tHr.AddRow ("Max DD, %", hourly.MaxDrawdownPct.ToString ("0.0"));
				tHr.AddRow ("Trades", hourly.Trades.ToString ());
				tHr.AddRow ("tp-first", hourly.TpFirst.ToString ());
				tHr.AddColoredRow (ConsoleStyler.BadColor, "sl-first", hourly.SlFirst.ToString ());
				tHr.AddRow ("ambiguous", hourly.Ambiguous.ToString ());
				tHr.WriteToConsole ();
				}

			Console.WriteLine ();
			stats.Print ();

			Console.WriteLine ();
			Console.WriteLine ($"[sl-offline] total samples: {slOffline.Count}");
			Console.WriteLine ($"[pullback-offline] total samples: {pullbackOffline.Count}");
			Console.WriteLine ($"[small-offline] total samples: {smallOffline.Count}");
			}
		}
	}
