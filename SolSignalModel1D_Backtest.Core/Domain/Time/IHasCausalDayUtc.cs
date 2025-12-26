namespace SolSignalModel1D_Backtest.Core.Domain.Time
	{
	/// <summary>
	/// Единый контракт для объектов, которые однозначно привязаны к "каузальному дню".
	/// Это убирает потребность в extension-методах вида ToCausalDateUtc(rowType).
	/// </summary>
	public interface IHasCausalDayUtc
		{
		CausalDayUtc CausalDayUtc { get; }
		}
	}
