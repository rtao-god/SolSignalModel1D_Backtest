using System;
using Xunit;
using SolSignalModel1D_Backtest.Core.Infra;
using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing
	{
	/// <summary>
	/// Инварианты окна дневной сделки:
	/// - на выходные вход невозможен;
	/// - пятница переносится на ближайшее рабочее утро;
	/// - IsNyMorning помечает только будние 7/8 часов.
	/// </summary>
	public sealed class WindowingInvariantsTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		[Fact]
		public void ComputeBaselineExitUtc_ThrowsOnWeekendEntry ()
			{
			// Локальное NY-время: суббота 08:00.
			var entryLocal = new DateTime (2024, 3, 9, 8, 0, 0, DateTimeKind.Unspecified);
			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocal, NyTz);

			Assert.Throws<InvalidOperationException> (() =>
				CoreWindowing.ComputeBaselineExitUtc (entryUtc, NyTz));
			}

		[Fact]
		public void ComputeBaselineExitUtc_MovesFridayToNextBusinessMorning ()
			{
			// Пятница 08:00 NY.
			var entryLocal = new DateTime (2024, 3, 8, 8, 0, 0, DateTimeKind.Unspecified);
			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocal, NyTz);

			var exitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, NyTz);
			var exitLocal = TimeZoneInfo.ConvertTimeFromUtc (exitUtc, NyTz);

			// Должен быть первый рабочий день после уикенда.
			Assert.Equal (DayOfWeek.Monday, exitLocal.DayOfWeek);

			// Обязательно строго позже входа.
			Assert.True (exitUtc > entryUtc);
			}

		[Fact]
		public void IsNyMorning_TrueOnlyForBusinessMorningSlots ()
			{
			// Понедельник 08:00 NY.
			var mondayMorningLocal = new DateTime (2024, 3, 11, 8, 0, 0, DateTimeKind.Unspecified);
			var mondayMorningUtc = TimeZoneInfo.ConvertTimeToUtc (mondayMorningLocal, NyTz);

			// Та же дата, но не утро.
			var mondayNoonLocal = new DateTime (2024, 3, 11, 12, 0, 0, DateTimeKind.Unspecified);
			var mondayNoonUtc = TimeZoneInfo.ConvertTimeToUtc (mondayNoonLocal, NyTz);

			// Суббота 08:00.
			var saturdayLocal = new DateTime (2024, 3, 9, 8, 0, 0, DateTimeKind.Unspecified);
			var saturdayUtc = TimeZoneInfo.ConvertTimeToUtc (saturdayLocal, NyTz);

			Assert.True (CoreWindowing.IsNyMorning (mondayMorningUtc, NyTz));
			Assert.False (CoreWindowing.IsNyMorning (mondayNoonUtc, NyTz));
			Assert.False (CoreWindowing.IsNyMorning (saturdayUtc, NyTz));
			}
		}
	}
