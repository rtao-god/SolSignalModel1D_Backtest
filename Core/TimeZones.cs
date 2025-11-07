using System;

namespace SolSignalModel1D_Backtest.Core
	{
	public static class TimeZones
		{
		public static TimeZoneInfo GetNewYork ()
			{
			try
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("America/New_York");
				}
			catch
				{
				return TimeZoneInfo.FindSystemTimeZoneById ("Eastern Standard Time");
				}
			}
		}
	}
