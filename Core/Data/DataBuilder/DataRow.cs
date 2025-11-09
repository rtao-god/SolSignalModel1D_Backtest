using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public sealed class DataRow
		{
		public DateTime Date { get; set; }
		public double[] Features { get; set; } = Array.Empty<double> ();
		public int Label { get; set; }

		public bool RegimeDown { get; set; }
		public bool IsMorning { get; set; }

		public double SolRet30 { get; set; }
		public double BtcRet30 { get; set; }
		public double SolRet1 { get; set; }
		public double SolRet3 { get; set; }
		public double BtcRet1 { get; set; }
		public double BtcRet3 { get; set; }

		public int Fng { get; set; }
		public double DxyChg30 { get; set; }
		public double GoldChg30 { get; set; }
		public double BtcVs200 { get; set; }
		public double SolRsiCentered { get; set; }
		public double RsiSlope3 { get; set; }

		public double AtrPct { get; set; }
		public double DynVol { get; set; }
		public double MinMove { get; set; }

		public double SolFwd1 { get; set; }

		// alt-заглушки
		public double AltFracPos6h { get; set; }
		public double AltFracPos24h { get; set; }
		public double AltMedian24h { get; set; }
		public int AltCount { get; set; }
		public bool AltReliable { get; set; }

		// micro ground-truth
		public bool FactMicroUp { get; set; }
		public bool FactMicroDown { get; set; }

		// ликвы (сейчас 0, но поля нужны) (пока что LEGACY)
		public double LiqUpRel { get; set; }
		public double LiqDownRel { get; set; }

		// авто-фибо (пока что LEGACY)
		public double FiboUpRel { get; set; }
		public double FiboDownRel { get; set; }

		// фичи трендовости (как были)
		public double TrendRet24h { get; set; }
		public double TrendVol7d { get; set; }
		public double VolShiftRatio { get; set; }
		public double TrendAbs30 { get; set; }

		public int HardRegime { get; set; }

		// ===== EMA RAW + простые сравнения =====
		public double SolEma50 { get; set; }
		public double SolEma200 { get; set; }
		public double BtcEma50 { get; set; }
		public double BtcEma200 { get; set; }
		public double SolEma50vs200 { get; set; }
		public double BtcEma50vs200 { get; set; }
		}
	}
