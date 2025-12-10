using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Принтер агрегированных вероятностей:
	/// - дневные (Day),
	/// - с микро-оверлеем (Day+Micro),
	/// - с микро+SL (Total).
	/// Никакой новой математики не считает, только усредняет уже посчитанные поля CausalPredictionRecord.
	/// </summary>
	public static class AggregationProbsPrinter
		{
		/// <summary>
		/// Печатает агрегированные вероятности по сегментам (Train/OOS/Recent/Full)
		/// и подробную отладочную таблицу по последним дням.
		/// </summary>
		public static void Print (
			IReadOnlyList<CausalPredictionRecord> records,
			DateTime trainUntilUtc,
			int recentDays = 240,
			int debugLastDays = 20 )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (records.Count == 0)
				{
				ConsoleStyler.WriteHeader ("==== AGGREGATION PROBS ====");
				Console.WriteLine ("[agg-probs] no records, nothing to print.");
				return;
				}

			if (recentDays <= 0) recentDays = 1;
			if (debugLastDays <= 0) debugLastDays = 1;

			ConsoleStyler.WriteHeader ("==== AGGREGATION PROBS ====");

			// --- 0) Стабильная сортировка и общий диапазон ---
			var ordered = records
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var minDateUtc = ordered.First ().DateUtc;
			var maxDateUtc = ordered.Last ().DateUtc;

			Console.WriteLine (
				$"[agg-probs] full records period = {minDateUtc:yyyy-MM-dd}..{maxDateUtc:yyyy-MM-dd}, totalRecords = {ordered.Count}");

			// --- 1) Сегменты по trainUntilUtc / recentDays ---
			var train = ordered
				.Where (r => r.DateUtc <= trainUntilUtc)
				.ToList ();

			var oos = ordered
				.Where (r => r.DateUtc > trainUntilUtc)
				.ToList ();

			var full = ordered;

			var fromRecentUtc = maxDateUtc.AddDays (-recentDays);
			var recent = ordered
				.Where (r => r.DateUtc >= fromRecentUtc)
				.ToList ();

			if (recent.Count == 0)
				{
				recent = full;
				}

			// --- 2) Краткая сводка по сегментам ---
			var metaTable = new TextTable ();
			metaTable.AddHeader ("segment", "from", "to", "days");

			AddSegmentMetaRow (metaTable, "Train", train);
			AddSegmentMetaRow (metaTable, "OOS", oos);
			AddSegmentMetaRow (metaTable, $"Recent({recentDays}d)", recent);
			AddSegmentMetaRow (metaTable, "Full", full);

			metaTable.WriteToConsole ();
			Console.WriteLine ();

			// --- 3) Усреднённые вероятности и confidence по сегментам ---
			PrintSegmentAverages ("Train", train);
			PrintSegmentAverages ("OOS", oos);
			PrintSegmentAverages ($"Recent (last {recentDays} days)", recent);
			PrintSegmentAverages ("Full history", full);

			// --- 4) Подробная таблица по последним N дням ---
			var debugRecords = ordered.Skip (Math.Max (0, ordered.Count - debugLastDays)).ToList ();
			PrintLastDaysDebug (debugRecords);
			}

		private static void AddSegmentMetaRow ( TextTable t, string name, IReadOnlyList<CausalPredictionRecord> seg )
			{
			if (seg == null || seg.Count == 0)
				{
				t.AddRow (name, "-", "-", "0");
				return;
				}

			var from = seg.First ().DateUtc;
			var to = seg.Last ().DateUtc;

			t.AddRow (
				name,
				from.ToString ("yyyy-MM-dd"),
				to.ToString ("yyyy-MM-dd"),
				seg.Count.ToString ());
			}

		private static void PrintSegmentAverages ( string title, IReadOnlyList<CausalPredictionRecord> seg )
			{
			ConsoleStyler.WriteHeader ($"[agg-probs] {title}");

			if (seg == null || seg.Count == 0)
				{
				Console.WriteLine ("[agg-probs] segment is empty.");
				Console.WriteLine ();
				return;
				}

			int n = seg.Count;
			double invN = 1.0 / n;

			double sumUpDay = 0.0, sumFlatDay = 0.0, sumDownDay = 0.0;
			double sumUpDm = 0.0, sumFlatDm = 0.0, sumDownDm = 0.0;
			double sumUpTot = 0.0, sumFlatTot = 0.0, sumDownTot = 0.0;

			double sumSumDay = 0.0;
			double sumSumDm = 0.0;
			double sumSumTot = 0.0;

			double sumConfDay = 0.0;
			double sumConfMicro = 0.0;

			int slNonZero = 0;

			foreach (var r in seg)
				{
				double pUd = r.ProbUp_Day;
				double pFd = r.ProbFlat_Day;
				double pDd = r.ProbDown_Day;

				double pUm = r.ProbUp_DayMicro;
				double pFm = r.ProbFlat_DayMicro;
				double pDm = r.ProbDown_DayMicro;

				double pUt = r.ProbUp_Total;
				double pFt = r.ProbFlat_Total;
				double pDt = r.ProbDown_Total;

				sumUpDay += pUd;
				sumFlatDay += pFd;
				sumDownDay += pDd;

				sumUpDm += pUm;
				sumFlatDm += pFm;
				sumDownDm += pDm;

				sumUpTot += pUt;
				sumFlatTot += pFt;
				sumDownTot += pDt;

				sumSumDay += pUd + pFd + pDd;
				sumSumDm += pUm + pFm + pDm;
				sumSumTot += pUt + pFt + pDt;

				sumConfDay += r.Conf_Day;
				sumConfMicro += r.Conf_Micro;

				if (r.SlProb > 0.0)
					slNonZero++;
				}

			double avgUpDay = sumUpDay * invN;
			double avgFlatDay = sumFlatDay * invN;
			double avgDownDay = sumDownDay * invN;
			double avgSumDay = sumSumDay * invN;

			double avgUpDm = sumUpDm * invN;
			double avgFlatDm = sumFlatDm * invN;
			double avgDownDm = sumDownDm * invN;
			double avgSumDm = sumSumDm * invN;

			double avgUpTot = sumUpTot * invN;
			double avgFlatTot = sumFlatTot * invN;
			double avgDownTot = sumDownTot * invN;
			double avgSumTot = sumSumTot * invN;

			double avgConfDay = sumConfDay * invN;
			double avgConfMicro = sumConfMicro * invN;

			bool degenerateDay = avgSumDay <= 1e-6;
			bool degenerateDm = avgSumDm <= 1e-6;
			bool degenerateTot = avgSumTot <= 1e-6;

			if (degenerateDay || degenerateDm || degenerateTot)
				{
				var first = seg[0];

				Console.WriteLine (
					"[agg-probs][FATAL] segment '{0}' has near-zero average probabilities. " +
					"avgSumDay={1:0.000000}, avgSumDm={2:0.000000}, avgSumTot={3:0.000000}",
					title,
					avgSumDay,
					avgSumDm,
					avgSumTot);

				Console.WriteLine (
					"[agg-probs][FATAL] example record: date={0:O}, " +
					"P_day=({1}, {2}, {3}), P_dm=({4}, {5}, {6}), P_tot=({7}, {8}, {9}), " +
					"Conf_Day={10}, Conf_Micro={11}, SlProb={12}",
					first.DateUtc,
					first.ProbUp_Day,
					first.ProbFlat_Day,
					first.ProbDown_Day,
					first.ProbUp_DayMicro,
					first.ProbFlat_DayMicro,
					first.ProbDown_DayMicro,
					first.ProbUp_Total,
					first.ProbFlat_Total,
					first.ProbDown_Total,
					first.Conf_Day,
					first.Conf_Micro,
					first.SlProb);

				throw new InvalidOperationException (
					"[AggregationProbsPrinter] Degenerate probabilities: at least one layer has avg sum ≈ 0. " +
					"Вероятности Prob*_Day / Prob*_DayMicro / Prob*_Total, скорее всего, не были заполнены в пайплайне.");
				}

			var probsTable = new TextTable ();
			probsTable.AddHeader ("layer", "P_up", "P_flat", "P_down", "sum");

			probsTable.AddRow (
				"Day",
				FormatProb (avgUpDay),
				FormatProb (avgFlatDay),
				FormatProb (avgDownDay),
				FormatProb (avgSumDay));

			probsTable.AddRow (
				"Day+Micro",
				FormatProb (avgUpDm),
				FormatProb (avgFlatDm),
				FormatProb (avgDownDm),
				FormatProb (avgSumDm));

			probsTable.AddRow (
				"Total (Day+Micro+SL)",
				FormatProb (avgUpTot),
				FormatProb (avgFlatTot),
				FormatProb (avgDownTot),
				FormatProb (avgSumTot));

			probsTable.WriteToConsole ();
			Console.WriteLine ();

			var confTable = new TextTable ();
			confTable.AddHeader ("metric", "value");

			confTable.AddRow ("Conf_Day (avg)", FormatProb (avgConfDay));
			confTable.AddRow ("Conf_Micro (avg)", FormatProb (avgConfMicro));
			confTable.AddRow ("records with SL-score", $"{slNonZero}/{seg.Count}");

			confTable.WriteToConsole ();
			Console.WriteLine ();
			}

		private static string FormatProb ( double x ) => x.ToString ("0.000");

		private static void PrintLastDaysDebug ( IReadOnlyList<CausalPredictionRecord> last )
			{
			if (last == null || last.Count == 0)
				return;

			ConsoleStyler.WriteHeader ($"[agg-probs] last {last.Count} days (debug)");

			var t = new TextTable ();
			t.AddHeader (
				"Date",
				"y",
				"predD",
				"predDM",
				"predTot",
				"P_d (u/f/d)",
				"P_dm (u/f/d)",
				"P_tot (u/f/d)",
				"microUsed",
				"slUsed",
				"microAgree",
				"slPenLong",
				"slPenShort");

			const double eps = 1e-3;

			foreach (var r in last)
				{
				string date = r.DateUtc.ToString ("yyyy-MM-dd");

				string pDay = $"{FormatProb (r.ProbUp_Day)}/{FormatProb (r.ProbFlat_Day)}/{FormatProb (r.ProbDown_Day)}";
				string pDm = $"{FormatProb (r.ProbUp_DayMicro)}/{FormatProb (r.ProbFlat_DayMicro)}/{FormatProb (r.ProbDown_DayMicro)}";
				string pTot = $"{FormatProb (r.ProbUp_Total)}/{FormatProb (r.ProbFlat_Total)}/{FormatProb (r.ProbDown_Total)}";

				bool microUsed = HasOverlayChange (
					r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day,
					r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro,
					eps);

				bool slUsed = HasOverlayChange (
						r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro,
						r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total,
						eps)
					|| r.SlHighDecision
					|| r.SlProb > 0.0;

				bool microAgree = r.PredLabel_DayMicro == r.PredLabel_Day;

				bool slPenLong = r.ProbUp_Total < r.ProbUp_DayMicro - eps;
				bool slPenShort = r.ProbDown_Total < r.ProbDown_DayMicro - eps;

				t.AddRow (
					date,
					r.TrueLabel.ToString (),
					r.PredLabel_Day.ToString (),
					r.PredLabel_DayMicro.ToString (),
					r.PredLabel.ToString (),
					pDay,
					pDm,
					pTot,
					microUsed ? "Y" : ".",
					slUsed ? "Y" : ".",
					microAgree ? "Y" : ".",
					slPenLong ? "Y" : ".",
					slPenShort ? "Y" : ".");
				}

			t.WriteToConsole ();
			Console.WriteLine ();
			}

		private static bool HasOverlayChange (
			double up1,
			double flat1,
			double down1,
			double up2,
			double flat2,
			double down2,
			double eps )
			{
			return Math.Abs (up1 - up2) > eps
				|| Math.Abs (flat1 - flat2) > eps
				|| Math.Abs (down1 - down2) > eps;
			}
		}
	}
