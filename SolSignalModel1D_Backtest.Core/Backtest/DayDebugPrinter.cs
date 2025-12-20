using System;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public static class DayDebugPrinter
		{
		public static void PrintTestDay ( BacktestRecord r, BacktestRecord rec )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));
			if (rec == null) throw new ArgumentNullException (nameof (rec));

			// Delayed-флаги могут быть nullable (слой мог не применяться / быть не рассчитан).
			// Сравнение с true — строгая проверка “точно включено”.
			bool delayedExec = rec.DelayedEntryExecuted == true;
			bool delayedAsked = rec.DelayedEntryAsked == true;

			Console.WriteLine (
				$"[day] {r.ToCausalDateUtc ():yyyy-MM-dd}  pred={rec.PredLabel} " +
				$"micro=({(rec.PredMicroUp ? "UP" : rec.PredMicroDown ? "DOWN" : "-")})  " +
				$"entry={rec.Entry:F2}  exit24={rec.Close24:F2}  delayedExec={(delayedExec ? "Y" : "N")} " +
				$"src={rec.DelayedSource ?? "-"}");

			Console.WriteLine (
				$"      sol30={r.Causal.SolRet30:+0.00%;-0.00%}  btc30={r.Causal.BtcRet30:+0.00%;-0.00%}  " +
				$"atr={r.Causal.AtrPct:0.00%}  dyn={r.Causal.DynVol:0.00%}  fng={r.Causal.Fng}  " +
				$"dxy30={r.Causal.DxyChg30:+0.00%;-0.00%}  gold30={r.Causal.GoldChg30:+0.00%;-0.00%}");

			Console.WriteLine (
				$"      rsiC={r.Causal.SolRsiCentered:+0.0;-0.0}  rsiSlope3={r.Causal.RsiSlope3:+0.0;-0.0}  " +
				$"btc200={r.Causal.BtcVs200:+0.00%;-0.00%}  solE50v200={r.Causal.SolEma50vs200:+0.00%;-0.00%}  " +
				$"btcE50v200={r.Causal.BtcEma50vs200:+0.00%;-0.00%}  minMove={r.MinMove:0.00%}");

			// Причина “почему не исполнилось” — это результат решений/гейтов слоя,
			// поэтому хранится в causal-части, а не как “факт рынка”.
			if (rec.DelayedSource == "A" && !delayedExec && delayedAsked)
				{
				var why = rec.Causal.DelayedWhyNot ?? "unknown";
				Console.WriteLine ($"      [A] asked but not executed: {why}");
				}

			if (rec.DelayedSource == "B" && !delayedExec && delayedAsked)
				{
				var why = rec.Causal.DelayedWhyNot ?? "unknown";
				Console.WriteLine ($"      [B] asked but not executed: {why}");
				}
			}
		}
	}
