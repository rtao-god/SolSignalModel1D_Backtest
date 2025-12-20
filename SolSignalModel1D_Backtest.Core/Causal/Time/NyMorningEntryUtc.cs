using SolSignalModel1D_Backtest.Core.Infra;
using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Time
	{
	/// <summary>
	/// Нормализация "дневной даты" (00:00Z) в канонический момент входа "NY morning".
	/// Это нужно, потому что 00:00Z в NY часто попадает в предыдущий локальный день (вплоть до воскресенья),
	/// и тогда baseline-exit становится "undefined" по weekend-правилу.
	/// </summary>
	public static class NyMorningEntryUtc
		{
		/// <summary>
		/// Превращает day-stamp (DateUtc на 00:00Z) в entryUtc "NY morning":
		/// - летом: 08:00 UTC
		/// - зимой: 07:00 UTC
		/// DST определяется по NY таймзоне на полдень этого дня (чтобы избежать пограничных часов перехода).
		/// </summary>
		public static DateTime FromDayStampUtcOrThrow ( DateTime dateUtc00 )
			{
			if (dateUtc00 == default)
				throw new ArgumentException ("dateUtc must be initialized.", nameof (dateUtc00));

			if (dateUtc00.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("dateUtc must be UTC.", nameof (dateUtc00));

			if (dateUtc00.TimeOfDay != TimeSpan.Zero)
				throw new ArgumentException (
					$"Expected day-stamp at 00:00Z, got {dateUtc00:O}. " +
					"Передай сюда именно DateUtc-день, а не реальный entryUtc.");

			// Полдень UTC выбран как стабильно существующий момент (не попадает в DST-skip/overlap окна).
			var noonUtc = dateUtc00.Date.AddHours (12);

			var nyTz = TimeZones.NewYork;
			var nyLocalNoon = TimeZoneInfo.ConvertTimeFromUtc (noonUtc, nyTz);
			bool isDst = nyTz.IsDaylightSavingTime (nyLocalNoon);

			int entryHourUtc = isDst ? 8 : 7;

			return new DateTime (
				dateUtc00.Year, dateUtc00.Month, dateUtc00.Day,
				entryHourUtc, 0, 0,
				DateTimeKind.Utc);
			}

		/// <summary>
		/// Универсальный вход: если уже дали реальный entryUtc — возвращаем как есть;
		/// если дали day-stamp 00:00Z — конвертируем в "NY morning".
		/// Любые другие времена считаются ошибкой контракта.
		/// </summary>
		public static DateTime NormalizeEntryUtcOrThrow ( DateTime utc )
			{
			if (utc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("utc must be UTC.", nameof (utc));

			if (utc.TimeOfDay == TimeSpan.Zero)
				return FromDayStampUtcOrThrow (utc);

			// Если сюда попал уже entryUtc, он должен быть "NY morning" по контракту.
			if (!Windowing.IsNyMorning (utc, nyTz: TimeZones.NewYork))
				{
				throw new InvalidOperationException (
					$"Non-morning entryUtc passed where NY-morning expected: {utc:O}. " +
					"Используй NormalizeEntryUtcOrThrow на day-stamp, или передавай канонический entryUtc (07/08 UTC).");
				}

			return utc;
			}
		}
	}
