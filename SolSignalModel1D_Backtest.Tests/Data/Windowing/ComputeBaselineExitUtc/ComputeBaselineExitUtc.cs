using CoreWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.Windowing;

namespace SolSignalModel1D_Backtest.Tests.Data.Windowing.ComputeBaselineExitUtc
	{
	/// <summary>
	/// Legacy-адаптер для старых тестов:
	/// возвращает "следующее NY-утро" в UTC для entryUtc.
	/// Реализация делегирует в прод-контракт Windowing:
	/// baseline-exit = morning - 2min, поэтому здесь возвращаем morning = baseline-exit + 2min.
	/// </summary>
	internal static class ComputeBaselineExitUtc
		{
		public static DateTime ForEntryUtc ( DateTime entryUtc, TimeSpan? nyMorningLocalTime = null )
			{
			if (entryUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("Expected UTC DateTime.", nameof (entryUtc));

			if (nyMorningLocalTime != null)
				throw new InvalidOperationException (
					"[tests] nyMorningLocalTime override is not supported. " +
					"Use Core.Causal.Time.Windowing contract (DST-aware 07/08).");

			var baselineExitUtc = CoreWindowing.ComputeBaselineExitUtc (entryUtc, CoreWindowing.NyTz);
			return baselineExitUtc.AddMinutes (2);
			}
		}
	}
