using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Стопит продолжения уже переросшего тренда:
	/// если модель сказала Рост, но SolRet30 уже большой → Боковик,
	/// если модель сказала Обвал, но SolRet30 уже очень отрицательный → Боковик.
	/// </summary>
	public static class TrendMetaStopper
		{
		public static int Apply ( BacktestRecord r, int predClass, out string reason )
			{
			reason = string.Empty;

			// +10% за ~7.5 суток — уже достаточно
			const double upAbsThresh = 0.10;
			const double downAbsThresh = 0.10;

			double ret30 = r.Causal.SolRet30;

			if (predClass == 2 && !r.RegimeDown && ret30 >= upAbsThresh)
				{
				reason = "stop:overext-up";
				return 1;
				}

			if (predClass == 0 && ret30 <= -downAbsThresh)
				{
				reason = "stop:overext-down";
				return 1;
				}

			return predClass;
			}
		}
	}
