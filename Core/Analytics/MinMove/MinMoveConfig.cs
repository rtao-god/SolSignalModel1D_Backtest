using System;

namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{
	/// <summary>
	/// Конфиг адаптивного minMove.
	/// Все величины в долях (0.02 = 2%).
	/// </summary>
	public sealed class MinMoveConfig
		{
		/// <summary>Жёсткий пол для minMove (ниже считаем шумом).</summary>
		public double MinFloorPct { get; init; } = 0.015;   // 1.5%

		/// <summary>Жёсткий потолок для minMove.</summary>
		public double MinCeilPct { get; init; } = 0.08;    // 8%

		/// <summary>Вес ATR(6h) в локальной волатильности.</summary>
		public double AtrWeight { get; init; } = 0.6;

		/// <summary>Вес dynVol в локальной волатильности.</summary>
		public double DynVolWeight { get; init; } = 0.4;

		/// <summary>Альфа для EWMA по волатильности (0..1).</summary>
		public double EwmaAlpha { get; init; } = 0.15;

		/// <summary>Стартовый целевой квантиль амплитуды path (0..1).</summary>
		public double QuantileStart { get; init; } = 0.6;

		/// <summary>Минимально допустимый квантиль (нижняя граница адаптации).</summary>
		public double QuantileLow { get; init; } = 0.5;

		/// <summary>Максимально допустимый квантиль (верхняя граница адаптации).</summary>
		public double QuantileHigh { get; init; } = 0.8;

		/// <summary>Сколько дней назад смотреть при оценке path-амплитуды.</summary>
		public int QuantileWindowDays { get; init; } = 90;

		/// <summary>Как часто пытаться перенастроить квантиль (в днях).</summary>
		public int QuantileRetuneEveryDays { get; init; } = 10;

		/// <summary>Множитель minMove в DOWN-режиме.</summary>
		public double RegimeDownMul { get; init; } = 1.2;
		}
	}