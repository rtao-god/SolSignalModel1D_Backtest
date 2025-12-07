namespace SolSignalModel1D_Backtest.Api.Dto
	{
	/// <summary>
	/// DTO для создания нового профиля бэктеста.
	/// </summary>
	public sealed class BacktestProfileCreateDto
		{
		public string? Name { get; set; }

		public string? Description { get; set; }

		/// <summary>
		/// Категория профиля (по умолчанию "user").
		/// </summary>
		public string? Category { get; set; }

		/// <summary>
		/// Можно сразу пометить профиль как избранный.
		/// </summary>
		public bool? IsFavorite { get; set; }

		public BacktestConfigDto? Config { get; set; }
		}
	}
