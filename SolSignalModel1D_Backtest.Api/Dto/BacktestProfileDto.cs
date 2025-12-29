namespace SolSignalModel1D_Backtest.Api.Dto
	{
	public sealed class BacktestProfileDto
		{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string? Description { get; set; }

		/// <summary>
		/// Системный профиль (baseline и т.п.) или пользовательский.
		/// </summary>
		public bool IsSystem { get; set; }

		/// <summary>
		/// Категория профиля: system / user / scratch / ...
		/// </summary>
		public string? Category { get; set; }

		/// <summary>
		/// Пометка "избранный профиль".
		/// </summary>
		public bool IsFavorite { get; set; }

		/// <summary>
		/// Конфиг бэктеста, привязанный к профилю.
		/// Для списка профилей сейчас тоже отдаём конфиг целиком,
		/// чтобы фронт мог сразу инициализировать форму.
		/// </summary>
		public BacktestConfigDto? Config { get; set; }
		}
	}
