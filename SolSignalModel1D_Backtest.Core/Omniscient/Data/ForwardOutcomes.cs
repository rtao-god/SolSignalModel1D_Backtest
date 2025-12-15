using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
	{
	/// <summary>
	/// Набор forward-исходов по одному дню:
	/// entry, 24h-диапазон, closing и 1m-путь внутри baseline-окна.
	/// Здесь только факты «после начала окна», causal-слой до них физически
	/// не добирается.
	/// </summary>
	public sealed class ForwardOutcomes
		{
		/// <summary>
		/// Начало baseline-окна для дня (UTC).
		/// Совпадает с DateUtc у CausalPredictionRecord.
		/// </summary>
		public DateTime DateUtc { get; init; }

		/// <summary>
		/// Конец baseline-окна (UTC), обычно следующее NY-утро минус служебный оффсет.
		/// </summary>
		public DateTime WindowEndUtc { get; init; }

		/// <summary>
		/// Цена входа в baseline-окне (Open первой 1m-свечи окна).
		/// </summary>
		public double Entry { get; init; }

		/// <summary>
		/// Максимальный High внутри baseline-окна.
		/// </summary>
		public double MaxHigh24 { get; init; }

		/// <summary>
		/// Минимальный Low внутри baseline-окна.
		/// </summary>
		public double MinLow24 { get; init; }

		/// <summary>
		/// Close последней 1m-свечи baseline-окна.
		/// </summary>
		public double Close24 { get; init; }

		/// <summary>
		/// Полный 1m-путь baseline-окна.
		/// Используется всеми forward-/PnL-проходами.
		/// </summary>
		public IReadOnlyList<Candle1m> DayMinutes { get; init; } = Array.Empty<Candle1m> ();

		/// <summary>
		/// Path-based волатильность: максимальный ход в любую сторону от entry.
		/// </summary>
		public double MinMove { get; init; }

		/// <summary>
		/// Истина (класс) по forward-окну. Хранится в forward-части, чтобы causal-слой не мог подсмотреть.
		/// </summary>
		public int TrueLabel { get; init; }

		/// <summary>Истина по микро-направлению (если используется).</summary>
		public bool FactMicroUp { get; init; }

		/// <summary>Истина по микро-направлению (если используется).</summary>
		public bool FactMicroDown { get; init; }

		// path-метрики (их ждёт WindowTailPrinter и др.)
		public int PathFirstPassDir { get; init; }
		public DateTime? PathFirstPassTimeUtc { get; init; }
		public double PathReachedUpPct { get; init; }
		public double PathReachedDownPct { get; init; }
		}
	}
