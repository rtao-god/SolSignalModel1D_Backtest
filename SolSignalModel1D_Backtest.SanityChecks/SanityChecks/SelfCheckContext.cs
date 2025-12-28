using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
	{
	/// <summary>
	/// Входные данные для self-check'ов.
	/// </summary>
	public sealed class SelfCheckContext
		{
		/// <summary>Все дневные строки (train + OOS).</summary>
		public IReadOnlyList<LabeledCausalRow> AllRows { get; init; } = Array.Empty<LabeledCausalRow> ();

		/// <summary>Только утренние точки (NY-окно входа).</summary>
		public IReadOnlyList<LabeledCausalRow> Mornings { get; init; } = Array.Empty<LabeledCausalRow> ();

		/// <summary>Омнисциентные записи (causal + forward) по mornings.</summary>
		public IReadOnlyList<BacktestRecord> Records { get; init; } = Array.Empty<BacktestRecord> ();

		public IReadOnlyList<Candle6h> SolAll6h { get; init; } = Array.Empty<Candle6h> ();
		public IReadOnlyList<Candle1h> SolAll1h { get; init; } = Array.Empty<Candle1h> ();
		public IReadOnlyList<Candle1m> Sol1m { get; init; } = Array.Empty<Candle1m> ();

		public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; init; }
		public TimeZoneInfo NyTz { get; init; } = TimeZoneInfo.Utc;
		}
	}
