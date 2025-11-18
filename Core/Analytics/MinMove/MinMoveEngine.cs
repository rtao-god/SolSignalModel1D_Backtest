using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{
	public static class MinMoveEngine
		{
		/// <summary>
		/// Главный метод: адаптивный minMove, строго каузально.
		/// Использует только:
		/// - текущие atrPct/dynVol;
		/// - состояние state (накоплено только по прошлым дням);
		/// - historyRows с Date &lt;= asOfUtc, но с окном по дате.
		/// </summary>
		public static MinMoveResult ComputeAdaptive (
			DateTime asOfUtc,
			bool regimeDown,
			double atrPct,
			double dynVol,
			IReadOnlyList<DataRow> historyRows,
			MinMoveConfig cfg,
			MinMoveState state )
			{
			if (cfg == null) throw new ArgumentNullException (nameof (cfg));
			if (state == null) throw new ArgumentNullException (nameof (state));
			historyRows ??= Array.Empty<DataRow> ();

			// санитизация входа
			if (double.IsNaN (atrPct) || atrPct < 0) atrPct = 0.0;
			if (double.IsNaN (dynVol) || dynVol < 0) dynVol = 0.0;

			// локальная волатильность из индикаторов (ATR + dynVol)
			double localVol = ComputeLocalVol (atrPct, dynVol, cfg);

			// EWMA по волатильности (stateful, только прошлое)
			double ewma =
				(state.EwmaVol <= 0.0)
					? localVol
					: state.EwmaVol + cfg.EwmaAlpha * (localVol - state.EwmaVol);

			// обновляем оценку квантиля по path ТОЛЬКО по прошлым дням
			double q = state.QuantileQ;
			if (q <= 0.0) q = cfg.QuantileStart;

			if (state.LastQuantileTune == DateTime.MinValue ||
				(asOfUtc.Date - state.LastQuantileTune.Date).TotalDays >= cfg.QuantileRetuneEveryDays)
				{
				DateTime end = asOfUtc.Date.AddDays (-1);  // строго до сегодняшнего дня
				DateTime start = end.AddDays (-cfg.QuantileWindowDays);

				var window = historyRows
					.Where (r => r.Date.Date >= start && r.Date.Date <= end)
					.Select (r =>
					{
						double up = r.PathReachedUpPct;
						double down = Math.Abs (r.PathReachedDownPct);
						double m = Math.Max (up, down); // амплитуда в долях
						return m > 0 ? m : 0.0;
					})
					.Where (v => v > 0.0)
					.OrderBy (v => v)
					.ToArray ();

				if (window.Length >= 30)
					{
					int idx = (int) Math.Round (cfg.QuantileStart * (window.Length - 1));
					if (idx < 0) idx = 0;
					if (idx >= window.Length) idx = window.Length - 1;
					double realized = window[idx]; // типичная амплитуда path

					double target = Math.Max (cfg.MinFloorPct, ewma);

					// если типичная path-амплитуда слишком мала → чуть поднимаем квантиль
					if (realized < target * 0.9 && q < cfg.QuantileHigh)
						q = Math.Min (cfg.QuantileHigh, q + 0.05);
					// если слишком велика → немного снижаем квантиль
					else if (realized > target * 1.1 && q > cfg.QuantileLow)
						q = Math.Max (cfg.QuantileLow, q - 0.05);

					state.LastQuantileTune = asOfUtc.Date;
					}
				// если данных мало, LastQuantileTune не трогаем → будем пытаться ещё
				}

			state.EwmaVol = ewma;
			state.QuantileQ = q;

			// базовый масштаб волатильности
			double baseVol = Math.Max (localVol, ewma);

			// масштабируем minMove квантилем (относительно стартового)
			double scale = q / cfg.QuantileStart;
			double minMove = baseVol * scale;

			// в DOWN-режиме поднимаем порог
			if (regimeDown)
				minMove *= cfg.RegimeDownMul;

			// жёсткие границы
			if (minMove < cfg.MinFloorPct) minMove = cfg.MinFloorPct;
			if (minMove > cfg.MinCeilPct) minMove = cfg.MinCeilPct;

			return new MinMoveResult
				{
				MinMove = minMove,
				LocalVol = localVol,
				EwmaVol = ewma,
				QuantileUsed = q
				};
			}

		private static double ComputeLocalVol ( double atrPct, double dynVol, MinMoveConfig cfg )
			{
			double a = atrPct;
			double d = dynVol;

			if (double.IsNaN (a) || a < 0.0) a = 0.0;
			if (double.IsNaN (d) || d < 0.0) d = 0.0;

			// капаем совсем дикие значения, чтобы они не разорвали EWMA
			a = Math.Min (a, 0.25); // 25% за окно — уже экстрим
			d = Math.Min (d, 0.25);

			double v = cfg.AtrWeight * a + cfg.DynVolWeight * d;

			// мягкий пол, чтобы вола не схлопывалась в ноль
			if (v < cfg.MinFloorPct * 0.5)
				v = cfg.MinFloorPct * 0.5;

			return v;
			}
		}
	}
