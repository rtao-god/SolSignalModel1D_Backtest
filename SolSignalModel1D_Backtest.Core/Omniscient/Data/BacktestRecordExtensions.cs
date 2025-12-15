using System;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
	{
	public static class BacktestRecordExtensions
		{
		public static DateTime ToCausalDateUtc ( this BacktestRecord r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));
			return r.DateUtc.ToCausalDateUtc ();
			}
		}
	}
