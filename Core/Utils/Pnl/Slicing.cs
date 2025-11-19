// Core/Utils/Pnl/PnlCalculator.Slicing.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// Частичный класс PnlCalculator: утилиты выборки минутных свечей под окна PnL.
	/// </summary>
	public static partial class PnlCalculator
		{
		/// <summary>
		/// Возвращает 1m-свечи в интервале [start; end).
		/// </summary>
		private static List<Candle1m> SliceDayMinutes ( List<Candle1m> m1, DateTime start, DateTime end )
			=> m1.Where (m => m.OpenTimeUtc >= start && m.OpenTimeUtc < end).ToList ();
		}
	}
