using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Trading.Leverage
	{
	/// <summary>
	/// Омнисциентная политика плеча.
	/// Допускает доступ к Forward-фактам и предназначена только для диагностики/what-if.
	/// НЕЛЬЗЯ использовать для оценки качества/реалистичного бэктеста без явного режима.
	/// </summary>
	public interface IOmniscientLeveragePolicy
		{
		string Name { get; }
		double ResolveLeverage ( BacktestRecord rec );
		}
	}
