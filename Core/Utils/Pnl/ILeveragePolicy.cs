using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// Политика: по дню решает, какое плечо использовать.
	/// </summary>
	public interface ILeveragePolicy
		{
		/// <summary> имя политики для вывода в отчёте.</summary>
		string Name { get; }

		/// <summary>Вернуть плечо для данной сделки.</summary>
		double ResolveLeverage ( PredictionRecord rec );
		}
	}
