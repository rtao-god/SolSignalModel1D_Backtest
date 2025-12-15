using System;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Оверлей против "догоняния" уже переросшего тренда:
	/// - если модель сказала UP, но SolRet30 уже большой → переводим в FLAT;
	/// - если модель сказала DOWN, но SolRet30 уже сильно отрицательный → переводим в FLAT.
	///
	/// Важно: это не ML, а риск-правило. Должно быть включаемым флагом в торговом слое.
	/// </summary>
	public static class TrendMetaStopper
		{
		public static int Apply ( BacktestRecord r, int predClass, out string reason )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			reason = string.Empty;

			// Если фича отсутствует, это НЕ "0%". Это "нет данных".
			// В таком случае оверлей не применяем, чтобы не вносить скрытую эвристику.
			double? ret30Opt = r.Causal.SolRet30;
			if (!ret30Opt.HasValue)
				return predClass;

			double ret30 = ret30Opt.Value;

			// +10% за ~7.5 суток — уже достаточно "вытянуто"
			const double upAbsThresh = 0.10;
			const double downAbsThresh = 0.10;

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
