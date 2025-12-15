using System;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Core.Utils
	{
	public static class PrintHelpers
		{
		public static string ClassToRu ( int cls )
			{
			return cls switch
				{
					0 => "Обвал",
					1 => "Боковик",
					2 => "Рост",
					_ => "UNKNOWN"
					};
			}

		public static string MicroToRu ( MicroInfo m )
			{
			if (!m.Predicted && !m.ConsiderUp && !m.ConsiderDown)
				return "нет";

			if (m.ConsiderUp) return "БоковикРост";
			if (m.ConsiderDown) return "БоковикОбвал";
			return "нет";
			}

		public static string FactToRu ( BacktestRecord r )
			{
			if (r.Forward.TrueLabel == 1)
				{
				if (r.FactMicroUp) return "БоковикРост";
				if (r.FactMicroDown) return "БоковикОбвал";
				return "Боковик";
				}
			if (r.Forward.TrueLabel == 0) return "Обвал";
			if (r.Forward.TrueLabel == 2) return "Рост";
			return "UNKNOWN";
			}

		/// <summary>
		/// Красиво показать один день дебага.
		/// </summary>
		public static void PrintDebugDay (
			BacktestRecord row,
			(double entry, double maxHigh, double minLow, double fwdClose) fwd,
			int predClass,
			MicroInfo micro,
			string reason )
			{
			double rsi = (row.Causal.SolRsiCentered ?? double.NaN) + 50.0;
			double atrPct = (row.Causal.AtrPct ?? double.NaN) * 100.0;
			double minMovePct = row.MinMove * 100.0;

			Console.WriteLine ($"[dbg-day] {row.ToCausalDateUtc():yyyy-MM-dd HH:mm}");
			Console.WriteLine ($"  entry={fwd.entry:0.####}  maxHigh24={fwd.maxHigh:0.####}  minLow24={fwd.minLow:0.####}  fwdClose24={fwd.fwdClose:0.####}");
			Console.WriteLine ($"  rsi:{rsi:0.0}  atr:{atrPct:0.00}%  minMove:{minMovePct:0.00}%");
			Console.WriteLine ($"  Прогноз:{ClassToRu (predClass)}  Микро:{MicroToRu (micro)}  Факт:{FactToRu (row)}  (reason:{reason})");
			}

		public static string Pct ( double v ) => $"{v:0.0}%";
		}
	}
