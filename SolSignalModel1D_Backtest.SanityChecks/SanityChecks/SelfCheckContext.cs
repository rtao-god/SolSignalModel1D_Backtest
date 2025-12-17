using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
	{
	/// <summary>
	/// Входные данные для self-check'ов.
	/// Передаётся из Program после бутстрапа и построения PredictionRecord'ов.
	/// </summary>
	public sealed class SelfCheckContext
		{
		/// <summary>Все дневные строки (train + OOS).</summary>
		public IReadOnlyList<BacktestRecord> AllRows { get; init; } = Array.Empty<BacktestRecord> ();

		/// <summary>Только утренние точки (NY-окно входа).</summary>
		public IReadOnlyList<BacktestRecord> Mornings { get; init; } = Array.Empty<BacktestRecord> ();

		/// <summary>Прогнозы дневной модели по mornings.</summary>
		public IReadOnlyList<BacktestRecord> Records { get; init; } = Array.Empty<BacktestRecord> ();

		/// <summary>Вся история SOL 6h (для sanity-проверок окон и таргетов).</summary>
		public IReadOnlyList<Candle6h> SolAll6h { get; init; } = Array.Empty<Candle6h> ();

		/// <summary>Вся история SOL 1h (для SL- и delayed-логики).</summary>
		public IReadOnlyList<Candle1h> SolAll1h { get; init; } = Array.Empty<Candle1h> ();

		/// <summary>Вся история SOL 1m (для path-based sanity-проверок).</summary>
		public IReadOnlyList<Candle1m> Sol1m { get; init; } = Array.Empty<Candle1m> ();

		/// <summary>Граница train-периода в терминах baseline-exit (для TrainBoundary).</summary>
		public DateTime TrainUntilUtc { get; init; }

		/// <summary>Таймзона Нью-Йорка для тестов окон.</summary>
		public TimeZoneInfo NyTz { get; init; } = TimeZoneInfo.Utc;
		}
	}
