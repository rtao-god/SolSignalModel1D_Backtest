using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Каузальная строка (то, что было доступно на момент принятия решения).
	/// </summary>
	public sealed class CausalDataRow : IHasDateUtc
		{
		public DateTime DateUtc { get; }

		// ===== Контекст, который может использоваться rule-based логикой (фильтры/режимы) =====
		public bool RegimeDown { get; }
		public bool IsMorning { get; }
		public int HardRegime { get; }

		/// <summary>
		/// Мин. движение (порог), рассчитанный каузально (без заглядывания в текущий forward).
		/// Используется бизнес-логикой и диагностикой, но НЕ обязан входить в ML-вектор.
		/// </summary>
		public double MinMove { get; }

		// ===== Фичи (ровно то, что подаётся в модель) =====
		public double SolRet30 { get; }
		public double BtcRet30 { get; }
		public double SolBtcRet30 { get; }

		public double SolRet1 { get; }
		public double SolRet3 { get; }
		public double BtcRet1 { get; }
		public double BtcRet3 { get; }

		public double FngNorm { get; }
		public double DxyChg30 { get; }
		public double GoldChg30 { get; }

		public double BtcVs200 { get; }

		/// <summary>
		/// RSI центрированный и отмасштабированный (как в текущем боевом датасете).
		/// </summary>
		public double SolRsiCenteredScaled { get; }

		/// <summary>
		/// Наклон RSI и отмасштабированный (как в текущем боевом датасете).
		/// </summary>
		public double RsiSlope3Scaled { get; }

		public double GapBtcSol1 { get; }
		public double GapBtcSol3 { get; }

		public double AtrPct { get; }
		public double DynVol { get; }

		public double SolAboveEma50 { get; }
		public double SolEma50vs200 { get; }
		public double BtcEma50vs200 { get; }

		/// <summary>
		/// Единственный канонический вектор фичей для ML.
		/// Возвращаем ReadOnlyMemory, чтобы запретить мутации массива извне.
		/// </summary>
		public ReadOnlyMemory<double> FeaturesVector => _featuresVector;

		private readonly double[] _featuresVector;

		/// <summary>
		/// Стабильные имена фичей в том же порядке, что и FeaturesVector.
		/// Любое изменение списка/порядка = новая версия датасета/модели.
		/// </summary>
		public static IReadOnlyList<string> FeatureNames { get; } = new[]
		{
			nameof(SolRet30),
			nameof(BtcRet30),
			nameof(SolBtcRet30),

			nameof(SolRet1),
			nameof(SolRet3),
			nameof(BtcRet1),
			nameof(BtcRet3),

			nameof(FngNorm),
			nameof(DxyChg30),
			nameof(GoldChg30),

			nameof(BtcVs200),

			nameof(SolRsiCenteredScaled),
			nameof(RsiSlope3Scaled),

			nameof(GapBtcSol1),
			nameof(GapBtcSol3),

			"RegimeDownFlag",
			nameof(AtrPct),
			nameof(DynVol),
			"HardRegimeIs2Flag",

			nameof(SolAboveEma50),
			nameof(SolEma50vs200),
			nameof(BtcEma50vs200),
		};

		public static int FeatureCount => FeatureNames.Count;

		public CausalDataRow (
			DateTime dateUtc,
			bool regimeDown,
			bool isMorning,
			int hardRegime,
			double minMove,

			double solRet30,
			double btcRet30,
			double solBtcRet30,

			double solRet1,
			double solRet3,
			double btcRet1,
			double btcRet3,

			double fngNorm,
			double dxyChg30,
			double goldChg30,

			double btcVs200,

			double solRsiCenteredScaled,
			double rsiSlope3Scaled,

			double gapBtcSol1,
			double gapBtcSol3,

			double atrPct,
			double dynVol,

			double solAboveEma50,
			double solEma50vs200,
			double btcEma50vs200 )
			{
			DateUtc = UtcTime.RequireUtc (dateUtc, nameof (dateUtc));

			RegimeDown = regimeDown;
			IsMorning = isMorning;
			HardRegime = hardRegime;

			MinMove = minMove;

			SolRet30 = solRet30;
			BtcRet30 = btcRet30;
			SolBtcRet30 = solBtcRet30;

			SolRet1 = solRet1;
			SolRet3 = solRet3;
			BtcRet1 = btcRet1;
			BtcRet3 = btcRet3;

			FngNorm = fngNorm;
			DxyChg30 = dxyChg30;
			GoldChg30 = goldChg30;

			BtcVs200 = btcVs200;

			SolRsiCenteredScaled = solRsiCenteredScaled;
			RsiSlope3Scaled = rsiSlope3Scaled;

			GapBtcSol1 = gapBtcSol1;
			GapBtcSol3 = gapBtcSol3;

			AtrPct = atrPct;
			DynVol = dynVol;

			SolAboveEma50 = solAboveEma50;
			SolEma50vs200 = solEma50vs200;
			BtcEma50vs200 = btcEma50vs200;

			_featuresVector = BuildFeatureVector ();
			ValidateFinite (_featuresVector);
			}

		private double[] BuildFeatureVector ()
			{
			// Инвариант: порядок обязан совпадать с FeatureNames.
			return new[]
			{
				SolRet30,
				BtcRet30,
				SolBtcRet30,

				SolRet1,
				SolRet3,
				BtcRet1,
				BtcRet3,

				FngNorm,
				DxyChg30,
				GoldChg30,

				BtcVs200,

				SolRsiCenteredScaled,
				RsiSlope3Scaled,

				GapBtcSol1,
				GapBtcSol3,

				RegimeDown ? 1.0 : 0.0,
				AtrPct,
				DynVol,
				HardRegime == 2 ? 1.0 : 0.0,

				SolAboveEma50,
				SolEma50vs200,
				BtcEma50vs200,
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
						$"[CausalDataRow] Non-finite feature value at index {i}: {x}. " +
						"Это ошибка данных/индикаторов; такие значения ломают метрики и могут имитировать «утечки».");
					}
				}
			}
		}
	}
