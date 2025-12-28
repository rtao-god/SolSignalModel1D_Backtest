namespace SolSignalModel1D_Backtest.Core.Causal.Legacy
	{
	/// <summary>
	/// LEGACY
	/// </summary>

	/// <summary>
	/// Заглушка для уровней ликвидаций.
	/// Сейчас возвращает 0, но интерфейс стабильный:
	/// RowBuilder и Program могут это вызывать.
	/// Потом сюда можно воткнуть чтение из json / api.
	/// </summary>
	public static class LiquidityLevels
		{
		public readonly struct Dist
			{
			public readonly double UpRel;
			public readonly double DownRel;

			public Dist ( double upRel, double downRel )
				{
				UpRel = upRel;
				DownRel = downRel;
				}
			}

		/// <summary>
		/// Вернёт относительную дистанцию до ближайшей ликвидности
		/// сверху и снизу от переданной цены.
		/// Сейчас — заглушка (0,0).
		/// </summary>
		public static Dist GetNearest ( double price )
			{
			// тут позже можно читать из файла / памяти / api
			return new Dist (0.0, 0.0);
			}
		}
	}
