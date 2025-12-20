using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
	{
	/// <summary>
	/// Тестовые расширения для унификации "каузальной даты" после рефакторинга контрактов.
	/// Инвариант: в тестах под "causal date" подразумевается дата/время, доступные на момент принятия решения (утро).
	/// </summary>
	public static class CausalDateExtensions
		{
		public static DateTime ToCausalDateUtc ( this LabeledCausalRow row )
			{
			if (row == null) throw new ArgumentNullException (nameof (row));
			return row.Causal.DateUtc;
			}

		public static DateTime ToCausalDateUtc ( this BacktestRecord row )
			{
			if (row == null) throw new ArgumentNullException (nameof (row));
			return row.Causal.DateUtc;
			}
		}
	}
