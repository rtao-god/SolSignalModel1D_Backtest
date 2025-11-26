using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public static class DayDebugPrinter
		{
		public static void PrintTestDay ( DataRow r, PredictionRecord rec )
			{
			// кратко: дата, класс, микро, вход/выход, что закрыло
			Console.WriteLine ($"[day] {r.Date:yyyy-MM-dd}  pred={rec.PredLabel} micro=({(rec.PredMicroUp ? "UP" : rec.PredMicroDown ? "DOWN" : "-")})  entry={rec.Entry:F2}  exit24={rec.Close24:F2}  delayedExec={(rec.DelayedEntryExecuted ? "Y" : "N")} src={rec.DelayedSource ?? "-"}");

			// ключевые индикаторы (минимальный нужный набор)
			Console.WriteLine ($"      sol30={r.SolRet30:+0.00%;-0.00%}  btc30={r.BtcRet30:+0.00%;-0.00%}  atr={r.AtrPct:0.00%}  dyn={r.DynVol:0.00%}  fng={r.Fng}  dxy30={r.DxyChg30:+0.00%;-0.00%}  gold30={r.GoldChg30:+0.00%;-0.00%}");
			Console.WriteLine ($"      rsiC={r.SolRsiCentered:+0.0;-0.0}  rsiSlope3={r.RsiSlope3:+0.0;-0.0}  btc200={r.BtcVs200:+0.00%;-0.00%}  solE50v200={r.SolEma50vs200:+0.00%;-0.00%}  btcE50v200={r.BtcEma50vs200:+0.00%;-0.00%}  minMove={r.MinMove:0.00%}");

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
