using SolSignalModel1D_Backtest.Core.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
	{
	internal static class NyTestDates
		{
		internal static readonly TimeZoneInfo NyTz = Windowing.NyTz;

		internal static DateTime NyLocal ( int year, int month, int day, int hour, int minute = 0 )
			{
			return new DateTime (year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
			}

		internal static DateTime ToUtc ( DateTime nyLocalUnspecified )
			{
			// В тестах принципиально фиксируем:
			// - локальное NY-время задаётся как Unspecified,
			// - в UTC конвертируем строго через TimeZoneInfo, чтобы DST учитывался корректно.
			return TimeZoneInfo.ConvertTimeToUtc (nyLocalUnspecified, NyTz);
			}

		internal static List<DateTime> BuildNyWeekdaySeriesUtc (
			DateTime startNyLocalDate,
			int count,
			int hour,
			int minute = 0 )
			{
			// Генератор “торговых” entry: только будни в NY.
			// Зачем: TrainBoundary по контракту исключает weekend; если синтетика содержит weekend,
			// тесты начинают проверять “исключение weekend”, а не нужный инвариант.
			var res = new List<DateTime> (count);

			var d = new DateTime (
				startNyLocalDate.Year,
				startNyLocalDate.Month,
				startNyLocalDate.Day,
				hour, minute, 0,
				DateTimeKind.Unspecified);

			while (res.Count < count)
				{
				if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
					res.Add (ToUtc (d));

				d = d.AddDays (1);
				}

			return res;
			}
		}
	}
