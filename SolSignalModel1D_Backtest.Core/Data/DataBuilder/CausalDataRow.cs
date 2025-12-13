using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder
	{
	/// <summary>
	/// Каузальная строка (то, что было доступно на момент принятия решения).
	/// Ключевая цель: физически отделить causal-фичи от любых forward-фактов,
	/// чтобы их нельзя было «случайно» прокинуть в модель/агрегацию.
	/// </summary>
	public sealed class CausalDataRow : IHasDateUtc
		{
		public DateTime DateUtc { get; }

		/// <summary>
		/// Признаки режима/контекста дня, доступные до начала окна.
		/// </summary>
		public bool RegimeDown { get; }
		public bool IsMorning { get; }

		// ===== CAUSAL FEATURES (числа, доступные до начала окна) =====
		public double SolRet30 { get; }
		public double BtcRet30 { get; }
		public double SolRet1 { get; }
		public double SolRet3 { get; }
		public double BtcRet1 { get; }
		public double BtcRet3 { get; }

		public double Fng { get; }
		public double DxyChg30 { get; }
		public double GoldChg30 { get; }
		public double BtcVs200 { get; }
		public double SolRsiCentered { get; }
		public double RsiSlope3 { get; }

		public double AtrPct { get; }
		public double DynVol { get; }
		public double MinMove { get; }

		public double TrendRet24h { get; }
		public double TrendVol7d { get; }
		public double VolShiftRatio { get; }
		public double TrendAbs30 { get; }

		public int HardRegime { get; }

		public double SolEma50 { get; }
		public double SolEma200 { get; }
		public double BtcEma50 { get; }
		public double BtcEma200 { get; }
		public double SolEma50vs200 { get; }
		public double BtcEma50vs200 { get; }

		/// <summary>
		/// Вектор фичей для ML (стабильный порядок + имена).
		/// Важно: наружу отдаём ReadOnlyMemory, чтобы никто не мог мутировать массив и «портить» датасет.
		/// </summary>
		public ReadOnlyMemory<double> FeaturesVector => _featuresVector;

		private readonly double[] _featuresVector;

		/// <summary>
		/// Стабильные имена фичей в том же порядке, что и FeaturesVector.
		/// Это упрощает PFI/диагностику и защищает от рассинхрона «индекс -> имя».
		/// </summary>
		public static IReadOnlyList<string> FeatureNames { get; } = new[]
		{
			nameof(SolRet30),
			nameof(BtcRet30),
			nameof(SolRet1),
			nameof(SolRet3),
			nameof(BtcRet1),
			nameof(BtcRet3),

			nameof(Fng),
			nameof(DxyChg30),
			nameof(GoldChg30),
			nameof(BtcVs200),
			nameof(SolRsiCentered),
			nameof(RsiSlope3),

			nameof(AtrPct),
			nameof(DynVol),
			nameof(MinMove),

			nameof(TrendRet24h),
			nameof(TrendVol7d),
			nameof(VolShiftRatio),
			nameof(TrendAbs30),

			nameof(HardRegime),

			nameof(SolEma50),
			nameof(SolEma200),
			nameof(BtcEma50),
			nameof(BtcEma200),
			nameof(SolEma50vs200),
			nameof(BtcEma50vs200),

			nameof(RegimeDown),
			nameof(IsMorning),
		};

		public CausalDataRow (
			DateTime dateUtc,
			bool regimeDown,
			bool isMorning,
			double solRet30,
			double btcRet30,
			double solRet1,
			double solRet3,
			double btcRet1,
			double btcRet3,
			double fng,
			double dxyChg30,
			double goldChg30,
			double btcVs200,
			double solRsiCentered,
			double rsiSlope3,
			double atrPct,
			double dynVol,
			double minMove,
			double trendRet24h,
			double trendVol7d,
			double volShiftRatio,
			double trendAbs30,
			int hardRegime,
			double solEma50,
			double solEma200,
			double btcEma50,
			double btcEma200,
			double solEma50vs200,
			double btcEma50vs200 )
			{
			DateUtc = UtcTime.RequireUtc (dateUtc, nameof (dateUtc));
			RegimeDown = regimeDown;
			IsMorning = isMorning;

			SolRet30 = solRet30;
			BtcRet30 = btcRet30;
			SolRet1 = solRet1;
			SolRet3 = solRet3;
			BtcRet1 = btcRet1;
			BtcRet3 = btcRet3;

			Fng = fng;
			DxyChg30 = dxyChg30;
			GoldChg30 = goldChg30;
			BtcVs200 = btcVs200;
			SolRsiCentered = solRsiCentered;
			RsiSlope3 = rsiSlope3;

			AtrPct = atrPct;
			DynVol = dynVol;
			MinMove = minMove;

			TrendRet24h = trendRet24h;
			TrendVol7d = trendVol7d;
			VolShiftRatio = volShiftRatio;
			TrendAbs30 = trendAbs30;

			HardRegime = hardRegime;

			SolEma50 = solEma50;
			SolEma200 = solEma200;
			BtcEma50 = btcEma50;
			BtcEma200 = btcEma200;
			SolEma50vs200 = solEma50vs200;
			BtcEma50vs200 = btcEma50vs200;

			_featuresVector = BuildFeatureVector ();
			ValidateFinite (_featuresVector);
			}

		private double[] BuildFeatureVector ()
			{
			// Инвариант: порядок обязан совпадать с FeatureNames.
			// Любое изменение порядка — это «новая версия» датасета/модели.
			return new[]
			{
				SolRet30,
				BtcRet30,
				SolRet1,
				SolRet3,
				BtcRet1,
				BtcRet3,

				Fng,
				DxyChg30,
				GoldChg30,
				BtcVs200,
				SolRsiCentered,
				RsiSlope3,

				AtrPct,
				DynVol,
				MinMove,

				TrendRet24h,
				TrendVol7d,
				VolShiftRatio,
				TrendAbs30,

				(double)HardRegime,

				SolEma50,
				SolEma200,
				BtcEma50,
				BtcEma200,
				SolEma50vs200,
				BtcEma50vs200,

				RegimeDown ? 1.0 : 0.0,
				IsMorning ? 1.0 : 0.0,
			};
			}

		private static void ValidateFinite ( double[] v )
			{
			for (int i = 0; i < v.Length; i++)
				{
				var x = v[i];
				if (double.IsNaN (x) || double.IsInfinity (x))
					{
					throw new InvalidOperationException (
						$"Non-finite feature value at index {i}: {x}. " +
						"Такие значения приводят к нестабильной метрике и ложным выводам о «утечке».");
					}
				}
			}
		}
	}
