using System;

namespace SolSignalModel1D_Backtest.Core.Utils.Indicators
	{
	/// <summary>
	/// Единые правила warm-up для построителей датасета.
	/// Цель: отсеять ранние индексы, где индикаторы/ret-окна по определению невалидны,
	/// чтобы не плодить:
	/// - "нулевые" индикаторы (тихие дефолты),
	/// - ложные аварии (dynVol/atr=0 из-за короткой истории).
	/// </summary>
	public static class WarmupGuards
		{
		public static bool ShouldSkipDailyRowWarmup (
			int solIdx,
			int btcIdx,
			int goldIdx,
			int retLookbackMax,
			int dynVolLookbackWindows,
			int goldLookbackWindows,
			out string reason )
			{
			reason = string.Empty;

			if (solIdx < 0 || btcIdx < 0 || goldIdx < 0)
				{
				reason = "negative index";
				return true;
				}

			// Ret6h(idx, windowsBack) требует idx - windowsBack >= 0.
			if (solIdx < retLookbackMax)
				{
				reason = $"solIdx<{retLookbackMax}";
				return true;
				}

			if (btcIdx < retLookbackMax)
				{
				reason = $"btcIdx<{retLookbackMax}";
				return true;
				}

			// Gold 30d: используем gIdx-30.
			if (goldIdx < goldLookbackWindows)
				{
				reason = $"goldIdx<{goldLookbackWindows}";
				return true;
				}

			// DynVol требует хотя бы минимального числа шагов; иначе часто 0.
			if (solIdx < dynVolLookbackWindows)
				{
				reason = $"solIdx<{dynVolLookbackWindows} for dynVol";
				return true;
				}

			return false;
			}
		}
	}
