using SolSignalModel1D_Backtest.Core.Causal.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Causal.Trading.Leverage
	{
	/// <summary>
	/// Каузальная политика плеча.
	/// Инвариант: не имеет доступа к Forward-фактам (DayMinutes/TrueLabel/Entry и т.п.).
	/// Любая зависимость от будущего окна — архитектурная ошибка и утечка.
	/// </summary>
	public interface ICausalLeveragePolicy
		{
		string Name { get; }

		/// <summary>
		/// Решение плеча на момент "утра" по каузальному слою.
		/// </summary>
		double ResolveLeverage ( CausalPredictionRecord causal );
		}
	}
