using System;

namespace SolSignalModel1D_Backtest.Core.Analytics.MinMove
	{
	/// <summary>
	/// Конфиг адаптивного minMove.
	/// Эта версия настроена чуть мягче:
	/// - поменьше горизонт истории;
	/// - уже коридор квантиля;
	/// - менее агрессивная режимная поправка.
	/// </summary>
	//public sealed class MinMoveConfig
	//	{
		/*public int HistDays { get; set; } = 120;   // было 150
		public int EwmaDays { get; set; } = 25;    // было 30

		public double WinsorLo { get; set; } = 0.05;
		public double WinsorHi { get; set; } = 0.95;

		// хотим flat-долю ближе к 50–65%, чтобы не уходило в экстремы
		public double TargetFlatLo { get; set; } = 0.50;  // было 0.45
		public double TargetFlatHi { get; set; } = 0.65;  // было 0.60

		// стартовый квантийль и его допустимый коридор
		public double QuantileStart { get; set; } = 0.60;
		public double QuantileStep { get; set; } = 0.02;
		public int QuantileUpdateEvery { get; set; } = 5;

		public double QuantileMin { get; set; } = 0.55;  // было 0.50
		public double QuantileMax { get; set; } = 0.70;  // было 0.75

		/// <summary>Нижний коридор для minMove (0.8%).</summary>
		public double MinFloorPct { get; set; } = 0.8 / 100.0;

		/// <summary>Верхний коридор для minMove (6%).</summary>
		public double MaxCapPct { get; set; } = 6.0 / 100.0;

		// издержки и экономический пол
		public double FeesPctRoundTrip { get; set; } = 0.0016; // 0.16% туда-обратно
		public double SlippagePct { get; set; } = 0.0008; // 0.08% оценка
		public double EconK { get; set; } = 2.0;

		// асимметрию пока не трогаем
		public bool UseAsymmetry { get; set; } = false;

		// режимная поправка — делаем мягче
		public bool UseRegimeAdj { get; set; } = true;
		public double RegimeDownMul { get; set; } = 1.08; // было 1.15

		// привязка TP к minMove — по смыслу используется в Delayed A
		public bool LinkTpToMinMove { get; set; } = true;
		public double TpK { get; set; } = 1.2;*/
		//}

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