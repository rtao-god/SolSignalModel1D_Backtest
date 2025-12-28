using SolSignalModel1D_Backtest.Core.Causal.Analytics.StrategySimulators;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.StrategySimulators;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program с отдельным блоком сценарных стратегий
	/// по дневной модели: baseline по всему периоду, хвост ~240 дней и sweep по пресетам.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Запускает все сценарии стратегии по дневной модели:
		/// 1) baseline на всём периоде;
		/// 2) baseline на хвосте ~240 дней;
		/// 3) sweep по пресетам.
		/// </summary>
		private static void RunStrategyScenarios (
			List<LabeledCausalRow> mornings,
			List<BacktestRecord> records,
			IReadOnlyList<Candle1m> sol1m
		)
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));

			// Не копируем 1m-историю, если под капотом уже List<T>, но тип поднят до IReadOnlyList<T>.
			// Копия создаётся только если источник реально не List<T>.
			var sol1mList = sol1m as List<Candle1m> ?? sol1m.ToList ();

			// 1) Основной прогон по baseline-пресету на всём периоде.
			var baselineParams = StrategyParameters.Baseline;

			Console.WriteLine ("[strategy:model] полный период по дневной модели (baseline)");
			var statsFull = StrategySimulator.Run (mornings, records, sol1mList, baselineParams);
			StrategyPrinter.Print (statsFull);

			// 2) Хвост из ~240 последних сигналов по baseline.
			const int TailCount = 240;

			if (records.Count > TailCount && mornings.Count > TailCount)
				{
				var offset = records.Count - TailCount;

				var tailMornings = mornings.Skip (offset).ToList ();
				var tailRecords = records.Skip (offset).ToList ();

				Console.WriteLine ($"[strategy:model] хвост из {TailCount} последних дней (baseline)");
				var statsTail = StrategySimulator.Run (tailMornings, tailRecords, sol1mList, baselineParams);
				StrategyPrinter.Print (statsTail);
				}

			// 3) Параметрический прогон: sweep по пресетам StrategyParameters.AllPresets.
			Console.WriteLine ();
			Console.WriteLine ("===== Strategy param sweep (presets) =====");

			foreach (var preset in StrategyParameters.AllPresets)
				{
				StrategyParametersPrinter.Print (preset);

				var stats = StrategySimulator.Run (mornings, records, sol1mList, preset);
				StrategyPrinter.Print (stats);
				}
			}
		}
	}
