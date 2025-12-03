using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Строит SL-датасет для оффлайн-обучения.
	/// Лейбл: кто был первым по 1m (TP / SL) в baseline-окне
	/// entryUtc → следующее рабочее NY-утро (минус 2 минуты).
	/// Фичи: по 1h (см. SlFeatureBuilder).
	/// Внутри делегирует фактическую сборку SlDatasetBuilder.
	/// </summary>
	public static class SlOfflineBuilder
		{
		public static List<SlHitSample> Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict,
			double tpPct = 0.03,
			double slPct = 0.05,
			Func<DataRow, bool>? strongSelector = null )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			// В текущем проде наружный код уже подаёт сюда train-only список.
			// Для совместимости используем trainUntil = max(Date).
			var trainUntil = rows.Count > 0
				? rows.Max (r => r.Date)
				: DateTime.MinValue;

			var dataset = SlDatasetBuilder.Build (
				rows: rows,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntil: trainUntil,
				tpPct: tpPct,
				slPct: slPct,
				strongSelector: strongSelector);

			Console.WriteLine (
				$"[sl-offline] built {dataset.Samples.Count} SL-samples " +
				$"(1m path labels, 1h features, tp={tpPct:0.###}, sl={slPct:0.###})");

			return dataset.Samples;
			}
		}
	}
