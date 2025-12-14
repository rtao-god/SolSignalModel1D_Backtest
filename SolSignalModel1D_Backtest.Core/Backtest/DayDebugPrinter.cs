using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public static class DayDebugPrinter
		{
		public static void PrintTestDay ( BacktestRecord r, BacktestRecord rec )
			{
			// кратко: дата, класс, микро, вход/выход, что закрыло
			Console.WriteLine ($"[day] {r.Causal.DateUtc:yyyy-MM-dd}  pred={rec.PredLabel} micro=({(rec.PredMicroUp ? "UP" : rec.PredMicroDown ? "DOWN" : "-")})  entry={rec.Entry:F2}  exit24={rec.Close24:F2}  delayedExec={(rec.DelayedEntryExecuted ? "Y" : "N")} src={rec.DelayedSource ?? "-"}");

			// ключевые индикаторы (минимальный нужный набор)
			Console.WriteLine ($"      sol30={r.Causal.SolRet30:+0.00%;-0.00%}  btc30={r.Causal.BtcRet30:+0.00%;-0.00%}  atr={r.Causal.AtrPct:0.00%}  dyn={r.Causal.DynVol:0.00%}  fng={r.Causal.Fng}  dxy30={r.Causal.DxyChg30:+0.00%;-0.00%}  gold30={r.Causal.GoldChg30:+0.00%;-0.00%}");
			Console.WriteLine ($"      rsiC={r.Causal.SolRsiCentered:+0.0;-0.0}  rsiSlope3={r.Causal.RsiSlope3:+0.0;-0.0}  btc200={r.Causal.BtcVs200:+0.00%;-0.00%}  solE50v200={r.Causal.SolEma50vs200:+0.00%;-0.00%}  btcE50v200={r.Causal.BtcEma50vs200:+0.00%;-0.00%}  minMove={r.MinMove:0.00%}");

			// диагноз причин отказа DelayedA/B
			if (rec.DelayedSource == "A" && !rec.DelayedEntryExecuted && rec.DelayedEntryAsked)
				{
				var why = rec.DelayedWhyNot ?? "unknown";
				Console.WriteLine ($"      [A] asked but not executed: {why}");
				}
			if (rec.DelayedSource == "B" && !rec.DelayedEntryExecuted && rec.DelayedEntryAsked)
				{
				var why = rec.DelayedWhyNot ?? "unknown";
				Console.WriteLine ($"      [B] asked but not executed: {why}");
				}
			}
		}
	}
