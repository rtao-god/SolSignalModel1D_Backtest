using System;

namespace SolSignalModel1D_Backtest.Core.Utils
	{
	/// <summary>
	/// Жёсткие проверки UTC-времени.
	/// В аналитике/ML любые «Unspecified/Local» часто приводят к тихим смещениям окон и псевдо-утечкам.
	/// Поэтому здесь принципиально бросаем исключение, а не «угадываем» таймзону.
	/// </summary>
	public static class UtcTime
		{
		public static DateTime RequireUtc ( DateTime value, string paramName )
			{
			if (value.Kind != DateTimeKind.Utc)
				{
				throw new ArgumentException (
					$"Expected UTC DateTime (Kind=Utc), got Kind={value.Kind}. " +
					"Передавай дату уже в UTC, без автоконвертаций.",
					paramName);
				}

			return value;
			}
		}
	}
