using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Снимок подготовленных данных для бэктеста / превью.
	/// Этот объект должен собираться тем же пайплайном, что и baseline-бэктест
	/// (код сейчас живёт в Program.Main + её partial-файлах).
	/// </summary>
	public sealed class BacktestDataSnapshot
		{
		/// <summary>
		/// Утренние точки (NY-окна), по которым считаются сигналы и PnL.
		/// Это тот же набор, который используется в baseline-бэктесте.
		/// </summary>
		public IReadOnlyList<DataRow> Mornings { get; init; } = Array.Empty<DataRow> ();

		/// <summary>
		/// PredictionRecord со всеми нужными полями (dir/micro/SL/Delayed и т.п.),
		/// построенный на основе дневных строк и моделей.
		/// </summary>
		public IReadOnlyList<PredictionRecord> Records { get; init; } = Array.Empty<PredictionRecord> ();

		/// <summary>
		/// 1m-свечи SOLUSDT, которые используются PnL-движком.
		/// Должны покрывать весь период сигналов в Records/Mornings.
		/// </summary>
		public IReadOnlyList<Candle1m> Candles1m { get; init; } = Array.Empty<Candle1m> ();
		}
	}
