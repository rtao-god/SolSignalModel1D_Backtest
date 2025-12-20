using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Diagnostics
	{
	/// <summary>
	/// Вспомогательный класс для консольной проверки разделения train/OOS
	/// на реальных BacktestRecord, без участия тестового проекта.
	/// </summary>
	internal static class RuntimeLeakageDebug
		{
		/// <summary>
		/// Печатает в консоль:
		/// - границу trainUntilUtc (в терминах baseline-exit);
		/// - accuracy по train и по OOS;
		/// - несколько строк около границы (последние train и первые OOS дни).
		/// </summary>
		public static void PrintDailyModelTrainOosProbe (
			IReadOnlyList<BacktestRecord> records,
			DateTime trainUntilUtc,
			TimeZoneInfo nyTz,
			int boundarySampleCount = 2 )
			{
			if (records == null || records.Count == 0)
				{
				Console.WriteLine ("[leak-probe] records is null or empty; nothing to probe.");
				return;
				}

			if (trainUntilUtc == default)
				{
				Console.WriteLine ("[leak-probe] trainUntilUtc is default(DateTime); probe is not meaningful.");
				return;
				}

			if (nyTz == null)
				{
				Console.WriteLine ("[leak-probe] nyTz is null; probe is not meaningful.");
				return;
				}

			// Упорядочиваем по entryUtc (Causal.DateUtc), чтобы корректно выделять последний train и первый OOS.
			var ordered = records
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var boundary = new TrainBoundary (trainUntilUtc, nyTz);
			var split = boundary.Split (ordered, r => r.Causal.DateUtc);

			var train = split.Train;
			var oos = split.Oos;

			if (split.Excluded.Count > 0)
				{
				Console.WriteLine (
					$"[leak-probe] WARNING: excluded={split.Excluded.Count} days (baseline-exit undefined by contract). " +
					"Эти дни не учитываются ни в train, ни в OOS.");
				}

			// Локальная функция для расчёта accuracy:
			// TrueLabel берём из Forward (это факт), PredLabel — из Causal (это прогноз).
			(int total, int correct, double acc) Acc ( IReadOnlyList<BacktestRecord> xs )
				{
				if (xs == null) throw new ArgumentNullException (nameof (xs));
				if (xs.Count == 0)
					return (0, 0, double.NaN);

				int correct = 0;

				for (int i = 0; i < xs.Count; i++)
					{
					var r = xs[i];

					// Инвариант: label — омнисциентный факт (Forward), предикт — каузальный результат (Causal).
					if (r.Forward.TrueLabel == r.Causal.PredLabel)
						correct++;
					}

				double accVal = (double) correct / xs.Count;
				return (xs.Count, correct, accVal);
				}

			var trainAcc = Acc (train);
			var oosAcc = Acc (oos);

			Console.WriteLine (
				$"[leak-probe] trainUntil(baseline-exit) = {boundary.TrainUntilIsoDate}, totalRecords = {ordered.Count}");

			Console.WriteLine (
				$"[leak-probe] TRAIN: count={trainAcc.total}, correct={trainAcc.correct}, acc={trainAcc.acc:P2}");

			if (oos.Count == 0)
				{
				Console.WriteLine ("[leak-probe] OOS: count=0 (нет дней после границы по baseline-exit контракту)");
				}
			else
				{
				Console.WriteLine (
					$"[leak-probe] OOS:   count={oosAcc.total}, correct={oosAcc.correct}, acc={oosAcc.acc:P2}");
				}

			static void PrintRow ( string kind, BacktestRecord r )
				{
				var c = r.Causal;

				Console.WriteLine (
					$"[leak-probe] {kind} {c.DateUtc:yyyy-MM-dd} " +
					$"true={r.Forward.TrueLabel} pred={c.PredLabel} " +
					$"microUp={c.PredMicroUp} microDown={c.PredMicroDown} " +
					$"minMove={c.MinMove:0.000}");
				}

			var trainSample = train
				.OrderByDescending (r => r.Causal.DateUtc)
				.Take (boundarySampleCount)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var oosSample = oos
				.OrderBy (r => r.Causal.DateUtc)
				.Take (boundarySampleCount)
				.ToList ();

			Console.WriteLine ("[leak-probe] Sample near boundary (TRAIN → OOS):");

			if (trainSample.Count == 0)
				{
				Console.WriteLine ("[leak-probe]   (no train rows)");
				}
			else
				{
				foreach (var r in trainSample)
					PrintRow ("T  ", r);
				}

			if (oosSample.Count == 0)
				{
				Console.WriteLine ("[leak-probe]   (no OOS rows)");
				}
			else
				{
				foreach (var r in oosSample)
					PrintRow ("OOS", r);
				}
			}
		}
	}
