using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Diagnostics
	{
	/// <summary>
	/// Вспомогательный класс для консольной проверки разделения train/OOS
	/// на реальных PredictionRecord, без участия тестового проекта.
	/// </summary>
	internal static class RuntimeLeakageDebug
		{
		/// <summary>
		/// Печатает в консоль:
		/// - границу trainUntilUtc;
		/// - accuracy по train и по OOS;
		/// - несколько строк около границы (последние train и первые OOS дни).
		/// Ничего не меняет в логике моделирования/бэктеста.
		/// </summary>
		public static void PrintDailyModelTrainOosProbe (
			IReadOnlyList<BacktestRecord> records,
			DateTime trainUntilUtc,
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

			// Упорядочиваем по дате, чтобы корректно выделять последний train и первый OOS.
			var ordered = records
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var train = ordered
				.Where (r => r.DateUtc <= trainUntilUtc)
				.ToList ();

			var oos = ordered
				.Where (r => r.DateUtc > trainUntilUtc)
				.ToList ();

			// Локальная функция для расчёта accuracy по TrueLabel/PredLabel.
			(int total, int correct, double acc) Acc ( List<BacktestRecord> xs )
				{
				if (xs.Count == 0)
					{
					return (0, 0, double.NaN);
					}

				int correct = 0;

				for (int i = 0; i < xs.Count; i++)
					{
					var r = xs[i];
					if (r.TrueLabel == r.PredLabel)
						{
						correct++;
						}
					}

				double accVal = (double) correct / xs.Count;
				return (xs.Count, correct, accVal);
				}

			var trainAcc = Acc (train);
			var oosAcc = Acc (oos);

			Console.WriteLine (
				$"[leak-probe] trainUntilUtc = {trainUntilUtc:yyyy-MM-dd}, totalRecords = {ordered.Count}");

			Console.WriteLine (
				$"[leak-probe] TRAIN: count={trainAcc.total}, correct={trainAcc.correct}, acc={trainAcc.acc:P2}");

			if (oos.Count == 0)
				{
				Console.WriteLine ("[leak-probe] OOS: count=0 (нет дней DateUtc > trainUntilUtc)");
				}
			else
				{
				Console.WriteLine (
					$"[leak-probe] OOS:   count={oosAcc.total}, correct={oosAcc.correct}, acc={oosAcc.acc:P2}");
				}

			// Компактный вывод нескольких строк около границы train/OOS.
			static void PrintRow ( string kind, BacktestRecord r )
				{
				Console.WriteLine (
					$"[leak-probe] {kind} {r.DateUtc:yyyy-MM-dd} " +
					$"true={r.TrueLabel} pred={r.PredLabel} " +
					$"microUp={r.PredMicroUp} microDown={r.PredMicroDown} " +
					$"minMove={r.MinMove:0.000}");
				}

			var trainSample = train
				.OrderByDescending (r => r.DateUtc)
				.Take (boundarySampleCount)
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var oosSample = oos
				.OrderBy (r => r.DateUtc)
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
					{
					PrintRow ("T  ", r);
					}
				}

			if (oosSample.Count == 0)
				{
				Console.WriteLine ("[leak-probe]   (no OOS rows)");
				}
			else
				{
				foreach (var r in oosSample)
					{
					PrintRow ("OOS", r);
					}
				}
			}
		}
	}
