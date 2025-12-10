using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
	{
	/// <summary>
	/// Набор forward-исходов по одному дню:
	/// entry, 24h-диапазон, closing и 1m-путь внутри baseline-окна.
	/// Здесь хранятся только факты «после начала окна», чтобы causal-слой
	/// физически не мог к ним обращаться.
	/// </summary>
	public sealed class ForwardOutcomes
		{
		/// <summary>
		/// Начало baseline-окна для дня (UTC).
		/// </summary>
		public DateTime DateUtc { get; init; }

		/// <summary>
		/// Конец baseline-окна (UTC), обычно следующее NY-утро минус служебный оффсет.
		/// </summary>
		public DateTime WindowEndUtc { get; init; }

		/// <summary>
		/// Цена входа в baseline-окне, по которой считается дневная сделка.
		/// </summary>
		public double Entry { get; init; }

		/// <summary>
		/// Максимальный High за baseline-окно.
		/// </summary>
		public double MaxHigh24 { get; init; }

		/// <summary>
		/// Минимальный Low за baseline-окно.
		/// </summary>
		public double MinLow24 { get; init; }

		/// <summary>
		/// Close последней 1m-свечи baseline-окна.
		/// </summary>
		public double Close24 { get; init; }

		/// <summary>
		/// 1m-свечи внутри baseline-окна [DateUtc; WindowEndUtc).
		/// Используются PnL-движком (TP/SL, ликвидация, MAE/MFE).
		/// </summary>
		public List<Candle1m> DayMinutes { get; init; } = new ();

		/// <summary>
		/// Оценка дневной волатильности вида
		/// max(|High/Entry − 1|, |Low/Entry − 1|).
		/// Вынесена сюда, чтобы все forward-оценки жили в одном месте.
		/// </summary>
		public double MinMove { get; init; }

		// При необходимости сюда можно добавлять дополнительные SL-факты:
		// флаги и цены срабатываний, порядок TP/SL и т.п.
		}
	}
