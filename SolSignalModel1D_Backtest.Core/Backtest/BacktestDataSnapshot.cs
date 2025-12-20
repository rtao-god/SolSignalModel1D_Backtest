using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Снимок подготовленных данных для бэктеста / превью.
	/// </summary>
	public sealed class BacktestDataSnapshot
		{
		/// <summary>
		/// Утренние точки (NY-окна), по которым считаются сигналы и PnL.
		/// </summary>
		public IReadOnlyList<LabeledCausalRow> Mornings { get; init; } = Array.Empty<LabeledCausalRow> ();

		/// <summary>Омнисциентные записи (каузал + forward-факты).</summary>
		public IReadOnlyList<BacktestRecord> Records { get; init; } = Array.Empty<BacktestRecord> ();

		/// <summary>1m-свечи, используемые PnL-движком.</summary>
		public IReadOnlyList<Candle1m> Candles1m { get; init; } = Array.Empty<Candle1m> ();
		}
	}
