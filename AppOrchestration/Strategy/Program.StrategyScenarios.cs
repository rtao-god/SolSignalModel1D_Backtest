using SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program с отдельным блоком сценарных стратегий
	/// по дневной модели: baseline по всему периоду, хвост ~240 дней и sweep по пресетам.
	/// Вынесено в отдельную папку Strategy, чтобы явно отделить запуск стратегий от остального пайплайна.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Запускает все сценарии стратегии по дневной модели:
		/// 1) baseline на всём периоде;
		/// 2) baseline на хвосте из последних TailCount дней;
		/// 3) sweep по пресетам StrategyParameters.AllPresets.
		/// </summary>
		private static void RunStrategyScenarios (
			List<DataRow> mornings,
			List<PredictionRecord> records,
			List<Candle1m> sol1m
		)
			{
			// 1) Основной прогон по baseline-пресету на всём периоде.
			var baselineParams = StrategyParameters.Baseline;

			Console.WriteLine ("[strategy:model] полный период по дневной модели (baseline)");
			var statsFull = StrategySimulator.Run (mornings, records, sol1m, baselineParams);
			StrategyPrinter.Print (statsFull);

			// 2) Хвост из ~240 последних сигналов по baseline.
			const int TailCount = 240;

			if (records.Count > TailCount && mornings.Count > TailCount)
				{
				var offset = records.Count - TailCount;

				var tailMornings = mornings.Skip (offset).ToList ();
				var tailRecords = records.Skip (offset).ToList ();

				Console.WriteLine ($"[strategy:model] хвост из {TailCount} последних дней (baseline)");
				var statsTail = StrategySimulator.Run (tailMornings, tailRecords, sol1m, baselineParams);
				StrategyPrinter.Print (statsTail);
				}

			// 3) Параметрический прогон: sweep по пресетам StrategyParameters.AllPresets.
			Console.WriteLine ();
			Console.WriteLine ("===== Strategy param sweep (presets) =====");

			foreach (var preset in StrategyParameters.AllPresets)
				{
				// a) Печать параметров выбранного пресета.
				StrategyParametersPrinter.Print (preset);

				// b) Расчёт и печать результатов стратегии с этим пресетом.
				var stats = StrategySimulator.Run (mornings, records, sol1m, preset);
				StrategyPrinter.Print (stats);
				}
			}
		}
	}
