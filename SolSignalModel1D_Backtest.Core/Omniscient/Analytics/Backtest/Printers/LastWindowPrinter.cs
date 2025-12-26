using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Последний день каждого окна: это ПРОСТО close-P&L за 24ч от точки входа (политика не влияет).
	/// PnL по политикам выводится в PnL-таблицах ниже.
	/// </summary>
	public static class LastWindowPrinter
		{
		public static void Print ( IReadOnlyList<BacktestRecord> lastWindowRecords )
			{
			if (lastWindowRecords == null || lastWindowRecords.Count == 0)
				return;

			ConsoleStyler.WriteHeader ("=== Last test day per window ===");

			var t = new TextTable ();
			t.AddHeader ("date", "side", "pred", "micro", "fact", "entry", "maxH", "minL", "close", "closePnL%");

            foreach (var r in lastWindowRecords.OrderBy(x => x.Causal.DayKeyUtc.Value))
            {
				bool goLong = r.PredLabel == 2 || r.PredLabel == 1 && r.PredMicroUp;
				bool goShort = r.PredLabel == 0 || r.PredLabel == 1 && r.PredMicroDown;
				string side = goLong ? "LONG" : goShort ? "SHORT" : "-";

				double closePnlPct = 0.0;
				if (r.Entry > 0 && r.Close24 > 0 && (goLong || goShort))
					{
					closePnlPct = goLong
						? (r.Close24 / r.Entry - 1.0) * 100.0
						: (r.Entry / r.Close24 - 1.0) * 100.0;
					}

				string pred = r.PredLabel switch
					{
						0 => "Обвал",
						1 => "Боковик",
						2 => "Рост",
						_ => "?"
						};

				string microStr = r.PredLabel == 1
					? r.PredMicroUp ? "БоковикРост" : r.PredMicroDown ? "БоковикОбвал" : "нет"
					: "—";

				string fact = r.TrueLabel switch
					{
						0 => "Обвал",
						1 => "Боковик",
						2 => "Рост",
						_ => "?"
						};

				var color = closePnlPct >= 0 ? ConsoleStyler.GoodColor : ConsoleStyler.BadColor;
                var day = r.Causal.DayKeyUtc.Value;

                t.AddColoredRow (color,
                    day.ToString("yyyy-MM-dd"),
                    side,
					pred,
					microStr,
					fact,
					r.Entry.ToString ("0.####"),
					r.MaxHigh24.ToString ("0.####"),
					r.MinLow24.ToString ("0.####"),
					r.Close24.ToString ("0.####"),
					closePnlPct.ToString ("0.00")
				);
				}

			t.WriteToConsole ();
			}
		}
	}
