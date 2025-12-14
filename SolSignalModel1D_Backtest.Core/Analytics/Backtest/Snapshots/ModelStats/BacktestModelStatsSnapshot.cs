using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats
	{
	/// <summary>
	/// Снимок модельных статистик бэктеста:
	/// - дневная путаница (3 класса);
	/// - путаница по тренду (DOWN vs UP);
	/// - SL-модель (confusion, метрики, sweep по порогам).
	/// Эти данные можно как печатать в консоль, так и отдавать во внешние отчёты.
	/// </summary>
	public sealed class BacktestModelStatsSnapshot
		{
		/// <summary>
		/// Минимальная дата среди PredictionRecord.DateUtc.
		/// </summary>
		public DateTime FromDateUtc { get; set; }

		/// <summary>
		/// Максимальная дата среди PredictionRecord.DateUtc.
		/// </summary>
		public DateTime ToDateUtc { get; set; }

		/// <summary>
		/// Статистика по дневной путанице (3 класса).
		/// </summary>
		public DailyConfusionStats Daily { get; set; } = new DailyConfusionStats ();

		/// <summary>
		/// Статистика по направлению тренда (DOWN vs UP).
		/// </summary>
		public TrendDirectionStats Trend { get; set; } = new TrendDirectionStats ();

		/// <summary>
		/// Статистика SL-модели (confusion, метрики, пороги).
		/// </summary>
		public SlStats Sl { get; set; } = new SlStats ();
		}

	/// <summary>
	/// Итог по дневной путанице (3 класса).
	/// </summary>
	public sealed class DailyConfusionStats
		{
		public List<DailyClassStatsRow> Rows { get; } = new List<DailyClassStatsRow> ();

		public int OverallCorrect { get; set; }

		public int OverallTotal { get; set; }

		/// <summary>
		/// Общая accuracy по всем дням, %.
		/// </summary>
		public double OverallAccuracyPct { get; set; }
		}

	/// <summary>
	/// Строка по одному истинному классу (0/1/2).
	/// </summary>
	public sealed class DailyClassStatsRow
		{
		public int TrueLabel { get; set; }

		public string LabelName { get; set; } = string.Empty;

		public int Pred0 { get; set; }

		public int Pred1 { get; set; }

		public int Pred2 { get; set; }

		public int Correct { get; set; }

		public int Total { get; set; }

		/// <summary>
		/// Accuracy по строке, %.
		/// </summary>
		public double AccuracyPct { get; set; }
		}

	/// <summary>
	/// Итог по путанице направления тренда (DOWN vs UP).
	/// </summary>
	public sealed class TrendDirectionStats
		{
		public List<TrendDirectionStatsRow> Rows { get; } = new List<TrendDirectionStatsRow> ();

		public int OverallCorrect { get; set; }

		public int OverallTotal { get; set; }

		public double OverallAccuracyPct { get; set; }
		}

	/// <summary>
	/// Строка путаницы тренда (DOWN days / UP days).
	/// </summary>
	public sealed class TrendDirectionStatsRow
		{
		/// <summary>
		/// Читабельное имя ряда, например "DOWN days" / "UP days".
		/// </summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>
		/// Индекс ряда: 0 = DOWN, 1 = UP.
		/// </summary>
		public int TrueIndex { get; set; }

		public int PredDown { get; set; }

		public int PredUp { get; set; }

		public int Correct { get; set; }

		public int Total { get; set; }

		/// <summary>
		/// Accuracy по строке, %.
		/// </summary>
		public double AccuracyPct { get; set; }
		}

	/// <summary>
	/// Комплексная статистика по SL-модели.
	/// </summary>
	public sealed class SlStats
		{
		public SlConfusionStats Confusion { get; set; } = new SlConfusionStats ();

		public SlMetricsStats Metrics { get; set; } = new SlMetricsStats ();

		public List<SlThresholdStatsRow> Thresholds { get; } = new List<SlThresholdStatsRow> ();
		}

	/// <summary>
	/// Confusion-таблица для SL-модели.
	/// </summary>
	public sealed class SlConfusionStats
		{
		public int TpLow { get; set; }

		public int TpHigh { get; set; }

		public int SlLow { get; set; }

		public int SlHigh { get; set; }

		public int SlSaved { get; set; }

		/// <summary>
		/// Дни, когда вообще был сигнал (goLong/goShort).
		/// </summary>
		public int TotalSignalDays { get; set; }

		/// <summary>
		/// Дни, для которых была не нулевая SlProb (&gt; 0).
		/// </summary>
		public int ScoredDays { get; set; }

		/// <summary>
		/// Дни, по которым был исход (TP или SL), участвующие в sweep-е.
		/// </summary>
		public int TotalOutcomeDays { get; set; }

		public int TotalSlDays { get; set; }

		public int TotalTpDays { get; set; }
		}

	/// <summary>
	/// Метрики по SL-модели (runtime).
	/// </summary>
	public sealed class SlMetricsStats
		{
		/// <summary>
		/// Доля дней с не нулевой SlProb среди дней с сигналом.
		/// </summary>
		public double Coverage { get; set; }

		public double Tpr { get; set; }

		public double Fpr { get; set; }

		public double Precision { get; set; }

		public double Recall { get; set; }

		public double F1 { get; set; }

		public double PrAuc { get; set; }
		}

	/// <summary>
	/// Строка sweep-а по порогам вероятности SL.
	/// </summary>
	public sealed class SlThresholdStatsRow
		{
		public double Threshold { get; set; }

		/// <summary>
		/// TPR(SL) в процентах.
		/// </summary>
		public double TprPct { get; set; }

		/// <summary>
		/// FPR(TP) в процентах.
		/// </summary>
		public double FprPct { get; set; }

		/// <summary>
		/// Доля дней, попавших в HIGH (pred HIGH %).
		/// </summary>
		public double PredHighPct { get; set; }

		public int HighTotal { get; set; }

		public int TotalDays { get; set; }

		/// <summary>
		/// Условная "хорошесть" порога: TPR ≥ 60% и FPR ≤ 40%.
		/// </summary>
		public bool IsGood { get; set; }

		public int HighSlDays { get; set; }

		public int HighTpDays { get; set; }

		public int TotalSlDays { get; set; }

		public int TotalTpDays { get; set; }
		}

	/// <summary>
	/// Внутренний билдёр снимка модельных статистик.
	/// Никакого вывода, только расчёты.
	/// </summary>
	public static class BacktestModelStatsSnapshotBuilder
		{
		private sealed class SlThresholdDay
			{
			public bool IsSlDay { get; set; }
			public double Prob { get; set; }
			}

		private enum DayOutcome
			{
			None = 0,
			TpFirst = 1,
			SlFirst = 2
			}

		/// <summary>
		/// Основная точка входа: считает все модельные статистики по тем же правилам,
		/// что и консольный принтер.
		/// </summary>
		public static BacktestModelStatsSnapshot Compute (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			double dailyTpPct,
			double dailySlPct,
			TimeZoneInfo nyTz )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));

			var snapshot = new BacktestModelStatsSnapshot ();

			if (records.Count > 0)
				{
				snapshot.FromDateUtc = records.Min (r => r.DateUtc);
				snapshot.ToDateUtc = records.Max (r => r.DateUtc);
				}

			ComputeDailyConfusion (records, snapshot.Daily);
			ComputeTrendDirectionConfusion (records, snapshot.Trend);
			ComputeSlStats (records, sol1m, dailyTpPct, dailySlPct, nyTz, snapshot.Sl);

			return snapshot;
			}

		private static void ComputeDailyConfusion (
			IReadOnlyList<BacktestRecord> records,
			DailyConfusionStats daily )
			{
			int[,] m = new int[3, 3];
			int[] rowSum = new int[3];
			int total = 0;

			foreach (var r in records)
				{
				if (r.TrueLabel is < 0 or > 2) continue;
				if (r.PredLabel is < 0 or > 2) continue;

				m[r.TrueLabel, r.PredLabel]++;
				rowSum[r.TrueLabel]++;
				total++;
				}

			int diag = 0;

			for (int y = 0; y < 3; y++)
				{
				int correct = m[y, y];
				int totalRow = rowSum[y];
				double acc = totalRow > 0
					? (double) correct / totalRow * 100.0
					: 0.0;

				diag += correct;

				daily.Rows.Add (new DailyClassStatsRow
					{
					TrueLabel = y,
					LabelName = LabelName (y),
					Pred0 = m[y, 0],
					Pred1 = m[y, 1],
					Pred2 = m[y, 2],
					Correct = correct,
					Total = totalRow,
					AccuracyPct = acc
					});
				}

			daily.OverallCorrect = diag;
			daily.OverallTotal = total;
			daily.OverallAccuracyPct = total > 0
				? (double) diag / total * 100.0
				: 0.0;
			}

		private static string LabelName ( int x ) => x switch
			{
				0 => "0 (down)",
				1 => "1 (flat)",
				2 => "2 (up)",
				_ => x.ToString ()
				};

		private static void ComputeTrendDirectionConfusion (
			IReadOnlyList<BacktestRecord> records,
			TrendDirectionStats trend )
			{
			// 0 = DOWN, 1 = UP
			int[,] m = new int[2, 2];
			int[] rowSum = new int[2];
			int total = 0;

			foreach (var r in records)
				{
				if (r.TrueLabel is < 0 or > 2) continue;

				int? trueDir = null;
				if (r.TrueLabel == 0)
					trueDir = 0; // DOWN
				else if (r.TrueLabel == 2)
					trueDir = 1; // UP
				else
					continue;    // flat (1) не учитываем

				bool predUp = r.PredLabel == 2 || r.PredLabel == 1 && r.PredMicroUp;
				bool predDown = r.PredLabel == 0 || r.PredLabel == 1 && r.PredMicroDown;

				int? predDir = null;
				if (predUp && !predDown)
					predDir = 1;
				else if (predDown && !predUp)
					predDir = 0;
				else
					continue; // без явного направления не считаем

				int y = trueDir.Value;
				int x = predDir.Value;

				m[y, x]++;
				rowSum[y]++;
				total++;
				}

			string[] names = { "DOWN days", "UP days" };

			int diag = 0;

			for (int y = 0; y < 2; y++)
				{
				int correct = m[y, y];
				int totalRow = rowSum[y];
				double acc = totalRow > 0
					? (double) correct / totalRow * 100.0
					: 0.0;

				diag += correct;

				trend.Rows.Add (new TrendDirectionStatsRow
					{
					Name = names[y],
					TrueIndex = y,
					PredDown = m[y, 0],
					PredUp = m[y, 1],
					Correct = correct,
					Total = totalRow,
					AccuracyPct = acc
					});
				}

			trend.OverallCorrect = diag;
			trend.OverallTotal = total;
			trend.OverallAccuracyPct = total > 0
				? (double) diag / total * 100.0
				: 0.0;
			}

		private static void ComputeSlStats (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> sol1m,
			double dailyTpPct,
			double dailySlPct,
			TimeZoneInfo nyTz,
			SlStats slStats )
			{
			if (slStats == null) throw new ArgumentNullException (nameof (slStats));

			int tp_low = 0, tp_high = 0, sl_low = 0, sl_high = 0;
			int slSaved = 0;

			int totalSignalDays = 0;
			int scoredDays = 0;

			var prPoints = new List<(double Score, int Label)> ();
			var thrDays = new List<SlThresholdDay> ();

			var m1 = sol1m
				.OrderBy (m => m.OpenTimeUtc)
				.ToList ();

			foreach (var r in records)
				{
				bool goLong = r.PredLabel == 2 || r.PredLabel == 1 && r.PredMicroUp;
				bool goShort = r.PredLabel == 0 || r.PredLabel == 1 && r.PredMicroDown;

				if (!goLong && !goShort)
					continue;

				totalSignalDays++;

				var outcome = GetDayOutcomeFromMinutes (
					r,
					m1,
					dailyTpPct,
					dailySlPct,
					nyTz);

				if (outcome == DayOutcome.None)
					{
					// без TP/SL — не участвует ни в confusion, ни в PR-AUC/threshold.
					continue;
					}

				bool isSlDay = outcome == DayOutcome.SlFirst;
				bool predHigh = r.SlHighDecision;

				bool hasScore = r.SlProb > 0.0;
				if (hasScore)
					{
					scoredDays++;
					prPoints.Add ((r.SlProb, isSlDay ? 1 : 0));
					thrDays.Add (new SlThresholdDay
						{
						IsSlDay = isSlDay,
						Prob = r.SlProb
						});
					}

				if (!isSlDay)
					{
					if (predHigh) tp_high++; else tp_low++;
					}
				else
					{
					if (predHigh) sl_high++; else sl_low++;
					if (predHigh) slSaved++;
					}
				}

			// Confusion
			var confusion = slStats.Confusion;
			confusion.TpLow = tp_low;
			confusion.TpHigh = tp_high;
			confusion.SlLow = sl_low;
			confusion.SlHigh = sl_high;
			confusion.SlSaved = slSaved;
			confusion.TotalSignalDays = totalSignalDays;
			confusion.ScoredDays = scoredDays;
			confusion.TotalOutcomeDays = thrDays.Count;

			int totalSl = thrDays.Count (d => d.IsSlDay);
			int totalTp = thrDays.Count - totalSl;

			confusion.TotalSlDays = totalSl;
			confusion.TotalTpDays = totalTp;

			// Метрики
			int tp = sl_high; // SL-day & pred HIGH
			int fn = sl_low;  // SL-day & pred LOW
			int fp = tp_high; // TP-day & pred HIGH
			int tn = tp_low;  // TP-day & pred LOW

			double tpr = tp + fn > 0 ? (double) tp / (tp + fn) : 0.0;
			double fpr = fp + tn > 0 ? (double) fp / (fp + tn) : 0.0;
			double precision = tp + fp > 0 ? (double) tp / (tp + fp) : 0.0;
			double recall = tpr;
			double f1 = precision + recall > 0
				? 2.0 * precision * recall / (precision + recall)
				: 0.0;

			double coverage = totalSignalDays > 0
				? (double) scoredDays / totalSignalDays
				: 0.0;

			double prAuc = prPoints.Count >= 2
				? ComputePrAuc (prPoints)
				: 0.0;

			slStats.Metrics = new SlMetricsStats
				{
				Coverage = coverage,
				Tpr = tpr,
				Fpr = fpr,
				Precision = precision,
				Recall = recall,
				F1 = f1,
				PrAuc = prAuc
				};

			// Sweep по порогам
			slStats.Thresholds.Clear ();

			if (thrDays.Count == 0)
				return;

			double[] thresholds = { 0.30, 0.40, 0.50, 0.60 };

			foreach (double thr in thresholds)
				{
				int highSl = thrDays.Count (d => d.IsSlDay && d.Prob >= thr);
				int highTp = thrDays.Count (d => !d.IsSlDay && d.Prob >= thr);
				int highTotal = highSl + highTp;

				double tprLocal = totalSl > 0 ? (double) highSl / totalSl * 100.0 : 0.0;
				double fprLocal = totalTp > 0 ? (double) highTp / totalTp * 100.0 : 0.0;
				double highFrac = thrDays.Count > 0 ? (double) highTotal / thrDays.Count * 100.0 : 0.0;

				bool isGood = tprLocal >= 60.0 && fprLocal <= 40.0;

				slStats.Thresholds.Add (new SlThresholdStatsRow
					{
					Threshold = thr,
					TprPct = tprLocal,
					FprPct = fprLocal,
					PredHighPct = highFrac,
					HighTotal = highTotal,
					TotalDays = thrDays.Count,
					IsGood = isGood,
					HighSlDays = highSl,
					HighTpDays = highTp,
					TotalSlDays = totalSl,
					TotalTpDays = totalTp
					});
				}
			}

		/// <summary>
		/// Path-based истина по 1m:
		/// полностью копирует правила из консольного принтера.
		/// </summary>
		private static DayOutcome GetDayOutcomeFromMinutes (
			BacktestRecord r,
			IReadOnlyList<Candle1m> allMinutes,
			double tpPct,
			double slPct,
			TimeZoneInfo nyTz )
			{
			bool goLong = r.PredLabel == 2 || r.PredLabel == 1 && r.PredMicroUp;
			bool goShort = r.PredLabel == 0 || r.PredLabel == 1 && r.PredMicroDown;
			if (!goLong && !goShort) return DayOutcome.None;
			if (r.Entry <= 0) return DayOutcome.None;

			DateTime from = r.DateUtc;
			DateTime to = Windowing.ComputeBaselineExitUtc (from, nyTz);

			var dayMinutes = allMinutes
				.Where (m => m.OpenTimeUtc >= from && m.OpenTimeUtc < to)
				.ToList ();

			if (dayMinutes.Count == 0)
				return DayOutcome.None;

			if (goLong)
				{
				double tp = r.Entry * (1.0 + tpPct);
				double sl = slPct > 1e-9 ? r.Entry * (1.0 - slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.High >= tp;
					bool hitSl = !double.IsNaN (sl) && m.Low <= sl;
					if (!hitTp && !hitSl) continue;

					if (hitSl) return DayOutcome.SlFirst;
					return DayOutcome.TpFirst;
					}
				}
			else
				{
				double tp = r.Entry * (1.0 - tpPct);
				double sl = slPct > 1e-9 ? r.Entry * (1.0 + slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.Low <= tp;
					bool hitSl = !double.IsNaN (sl) && m.High >= sl;
					if (!hitTp && !hitSl) continue;

					if (hitSl) return DayOutcome.SlFirst;
					return DayOutcome.TpFirst;
					}
				}

			return DayOutcome.None;
			}

		/// <summary>
		/// PR-AUC по точкам (score, label) в координатах (recall, precision).
		/// </summary>
		private static double ComputePrAuc ( List<(double Score, int Label)> points )
			{
			if (points == null || points.Count == 0)
				return 0.0;

			int totalPos = points.Count (p => p.Forward.TrueLabel == 1);
			int totalNeg = points.Count (p => p.Forward.TrueLabel == 0);

			if (totalPos == 0)
				return 0.0;

			var sorted = points
				.OrderByDescending (p => p.Score)
				.ToList ();

			int tp = 0, fp = 0;
			double prevRecall = 0.0;
			double basePrecision = (double) totalPos / (totalPos + totalNeg);
			double prevPrecision = basePrecision;
			double auc = 0.0;

			foreach (var (score, label) in sorted)
				{
				if (label == 1) tp++; else fp++;

				double recall = (double) tp / totalPos;
				double precision = tp + fp > 0
					? (double) tp / (tp + fp)
					: prevPrecision;

				double deltaR = recall - prevRecall;
				if (deltaR > 0)
					{
					auc += deltaR * (precision + prevPrecision) * 0.5;
					}

				prevRecall = recall;
				prevPrecision = precision;
				}

			return auc;
			}
		}
	}
