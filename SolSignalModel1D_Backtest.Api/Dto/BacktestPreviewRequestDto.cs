using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Api.Dto
	{
	/// <summary>
	/// Запрос на one-shot what-if бэктест.
	/// Позволяет передать полный BacktestConfig или переопределить baseline.
	/// </summary>
	public sealed class BacktestPreviewRequestDto
		{
		/// <summary>
		/// Конфиг бэктеста. Если не указан, используется baseline-конфиг.
		/// </summary>
		public BacktestConfigDto? Config { get; set; }

		/// <summary>
		/// Опциональный список имён политик, которые нужно оставить в прогоне.
		/// Если null/пусто — используются все политики из конфига.
		/// </summary>
		public List<string>? SelectedPolicies { get; set; }
		}
	}
