using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{
	public static class MinMoveEngine
		{
		/// <summary>
		/// Адаптивный minMove на момент asOfUtc.
		/// Критично: при работе с historyRows используется только прошлое относительно asOfUtc,
		/// и окно по дням должно быть строго фиксированной длины QuantileWindowDays.
		/// </summary>
		public static MinMoveResult ComputeAdaptive (
			DateTime asOfUtc,
			bool regimeDown,
			double atrPct,
			double dynVol,
			IReadOnlyList<MinMoveHistoryRow> historyRows,
			MinMoveConfig cfg,
			MinMoveState state )
			{
			if (cfg == null) throw new ArgumentNullException (nameof (cfg));
			if (state == null) throw new ArgumentNullException (nameof (state));
			if (asOfUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ("[min-move] asOfUtc must be UTC.");

			historyRows ??= Array.Empty<MinMoveHistoryRow> ();

			if (double.IsNaN (atrPct) || atrPct < 0)
				throw new InvalidOperationException ($"[min-move] Invalid atrPct={atrPct}.");
			if (double.IsNaN (dynVol) || dynVol < 0)
				throw new InvalidOperationException ($"[min-move] Invalid dynVol={dynVol}.");

			double localVol = ComputeLocalVol (atrPct, dynVol, cfg);

			double ewma =
				(state.EwmaVol <= 0.0)
					? localVol
					: state.EwmaVol + cfg.EwmaAlpha * (localVol - state.EwmaVol);

			double q = state.QuantileQ;
			if (q <= 0.0) q = cfg.QuantileStart;

			bool needRetune =
				state.LastQuantileTune == DateTime.MinValue ||
				(asOfUtc.Date - state.LastQuantileTune.Date).TotalDays >= cfg.QuantileRetuneEveryDays;

			if (needRetune)
				{
				// end = "вчера" (day-key), текущий день не включаем.
				DateTime end = asOfUtc.Date.AddDays (-1);

				// Строго N дней: (end-N; end] => day-key: end-(N-1) .. end.
				// Это ловит типичную регрессию N+1 при условии ">= start && <= end".
				DateTime startExclusive = end.AddDays (-cfg.QuantileWindowDays);

				var window = historyRows
					.Where (r => r.DateUtc.Kind == DateTimeKind.Utc)
					.Where (r => r.DateUtc.Date > startExclusive && r.DateUtc.Date <= end)
					.Select (r => r.RealizedPathAmpPct)
					.Where (v => v > 0.0 && !double.IsNaN (v) && !double.IsInfinity (v))
					.OrderBy (v => v)
					.ToArray ();

				if (window.Length >= 30)
					{
					int idx = (int) Math.Round (q * (window.Length - 1));
					if (idx < 0) idx = 0;
					if (idx >= window.Length) idx = window.Length - 1;

					double realized = window[idx];

					// target — “база” для сравнения реализованной амплитуды с текущим уровнем волатильности.
					double target = Math.Max (cfg.MinFloorPct, ewma);

					if (realized < target * 0.9 && q < cfg.QuantileHigh)
						q = Math.Min (cfg.QuantileHigh, q + 0.05);
					else if (realized > target * 1.1 && q > cfg.QuantileLow)
						q = Math.Max (cfg.QuantileLow, q - 0.05);

					state.LastQuantileTune = asOfUtc.Date;
					}
				}

			state.EwmaVol = ewma;
			state.QuantileQ = q;

			double baseVol = Math.Max (localVol, ewma);

			double scale = q / cfg.QuantileStart;
			double minMove = baseVol * scale;

			if (regimeDown)
				minMove *= cfg.RegimeDownMul;

			if (minMove < cfg.MinFloorPct) minMove = cfg.MinFloorPct;
			if (minMove > cfg.MinCeilPct) minMove = cfg.MinCeilPct;

			return new MinMoveResult
				{
				AsOfUtc = asOfUtc,
				RegimeDown = regimeDown,

				MinMove = minMove,
				LocalVol = localVol,
				EwmaVol = ewma,
				QuantileUsed = q
				};
			}

		private static double ComputeLocalVol ( double atrPct, double dynVol, MinMoveConfig cfg )
			{
			double a = Math.Min (atrPct, 0.25);
			double d = Math.Min (dynVol, 0.25);

			double v = cfg.AtrWeight * a + cfg.DynVolWeight * d;

			// Доменный пол для устойчивости: иначе EWMA может схлопнуться при аномально “тихих” входах.
			double softFloor = cfg.MinFloorPct * 0.5;
			if (v < softFloor)
				v = softFloor;

			return v;
			}
		}
	}
