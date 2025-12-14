using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// Единый реестр: какие свечи нужны каким подсистемам.
	/// Меняешь тут – меняется во всём проекте.
	/// </summary>
	public static class CandleRequirementRegistry
		{
		/// <summary>
		/// Возвращает список профилей, которые мы вообще используем в бэктесте.
		/// </summary>
		public static IReadOnlyList<ModelCandleProfile> GetProfiles ()
			{
			return new List<ModelCandleProfile>
			{
				// дневной слой – строим BacktestRecord: нужен SOL 6h, BTC 6h, PAXG 6h 
				new ModelCandleProfile(
					"daily-rowbuilder",
					new List<(string, CandleTimeframe)>
					{
						("SOLUSDT", CandleTimeframe.H6),
						("BTCUSDT", CandleTimeframe.H6),
						("PAXGUSDT", CandleTimeframe.H6)
					}),

				// SL-модель – в SlFeatureBuilder берутся последние 6 часов 1h SOL
				new ModelCandleProfile(
					"sl-model",
					new List<(string, CandleTimeframe)>
					{
						("SOLUSDT", CandleTimeframe.H1)
					}),

				// delayed A/B – тоже почасовой разбор
				new ModelCandleProfile(
					"delayed-models",
					new List<(string, CandleTimeframe)>
					{
						("SOLUSDT", CandleTimeframe.H1)
					}),

				// ликвидация / точный фитиль – минутки по SOL
				new ModelCandleProfile(
					"liquidation-check",
					new List<(string, CandleTimeframe)>
					{
						("SOLUSDT", CandleTimeframe.M1)
					})
			};
			}
		}
	}
