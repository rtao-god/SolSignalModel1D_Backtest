using System;

namespace SolSignalModel1D_Backtest.Core.Domain
	{
	/// <summary>
	/// Минимальный контракт «есть дата в UTC».
	/// Нужен для аналитических операций (сэмплинг/группировки) без привязки к конкретным DTO.
	/// </summary>
	public interface IHasDateUtc
		{
		DateTime DateUtc { get; }
		}
	}
