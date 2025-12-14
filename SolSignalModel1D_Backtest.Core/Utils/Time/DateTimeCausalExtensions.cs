namespace SolSignalModel1D_Backtest.Core.Utils.Time
	{
	/// <summary>
	/// Нормализация времени к "каузальной дате" (UTC day).
	/// Контракт:
	/// - если Kind=Utc -> берём UTC-день;
	/// - если Kind=Unspecified -> трактуем как уже-UTC (типичный результат .Date / парсинга без TZ);
	/// - если Kind=Local -> это ошибка: локальные даты в каузальном пайплайне дают тихие сдвиги.
	/// </summary>
	public static class DateTimeCausalExtensions
		{
		public static DateTime ToCausalDateUtc ( this DateTime dt )
			{
			if (dt.Kind == DateTimeKind.Local)
				{
				throw new InvalidOperationException (
					$"[time] Local DateTime is запрещён для каузальной даты: {dt:O}. " +
					"Используй UTC и нормализуй явно.");
				}

			// dt.Kind == Utc или Unspecified: берём календарный день и фиксируем Kind=Utc
			return new DateTime (dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);
			}
		}
	}
