using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Политика плеча: по одному дню решает, какое плечо использовать.
	/// Работает поверх BacktestRecord, чтобы иметь доступ к causal-фичам.
	/// </summary>
	public interface ILeveragePolicy
		{
		/// <summary>Имя политики для отчётов.</summary>
		string Name { get; }

		/// <summary>
		/// Вернуть плечо для данной сделки.
		/// Допускается использование любых causal-фичей и forward-метрик,
		/// но без прямой зависимости от конкретного PnL-результата.
		/// </summary>
		double ResolveLeverage ( BacktestRecord rec );
		}
	}
