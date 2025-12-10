using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
	{
	/// <summary>
	/// Входные данные для self-check'ов.
	/// Передаётся из Program после бутстрапа и построения PredictionRecord'ов.
	/// </summary>
	public sealed class SelfCheckContext
		{
		/// <summary>Все дневные строки (train + OOS).</summary>
		public IReadOnlyList<DataRow> AllRows { get; init; } = Array.Empty<DataRow> ();

		/// <summary>Только утренние точки (NY-окно входа).</summary>
		public IReadOnlyList<DataRow> Mornings { get; init; } = Array.Empty<DataRow> ();

		/// <summary>Прогнозы дневной модели по mornings.</summary>
		public IReadOnlyList<PredictionRecord> Records { get; init; } = Array.Empty<PredictionRecord> ();

		/// <summary>Вся история SOL 6h (для sanity-проверок окон и таргетов).</summary>
		public IReadOnlyList<Candle6h> SolAll6h { get; init; } = Array.Empty<Candle6h> ();

		/// <summary>Вся история SOL 1h (для SL- и delayed-логики).</summary>
		public IReadOnlyList<Candle1h> SolAll1h { get; init; } = Array.Empty<Candle1h> ();

		/// <summary>Вся история SOL 1m (для path-based sanity-проверок).</summary>
		public IReadOnlyList<Candle1m> Sol1m { get; init; } = Array.Empty<Candle1m> ();

		/// <summary>Граница train-периода дневной модели (_trainUntilUtc).</summary>
		public DateTime TrainUntilUtc { get; init; }

		/// <summary>Таймзона Нью-Йорка для тестов окон.</summary>
		public TimeZoneInfo NyTz { get; init; } = TimeZoneInfo.Utc;
		}
	}
