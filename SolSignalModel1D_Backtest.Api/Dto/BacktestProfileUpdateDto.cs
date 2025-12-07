namespace SolSignalModel1D_Backtest.Api.Dto
	{
	/// <summary>
	/// DTO для частичного обновления профиля бэктеста.
	/// Любое из полей может быть опущено.
	/// </summary>
	public sealed class BacktestProfileUpdateDto
		{
		public string? Name { get; set; }

		public string? Category { get; set; }

		public bool? IsFavorite { get; set; }
		}
	}
