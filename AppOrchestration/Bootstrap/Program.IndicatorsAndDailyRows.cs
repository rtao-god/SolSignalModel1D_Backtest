using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: обновление индикаторов и построение дневных строк.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Обновляет все дневные индикаторы в расширенном окне
		/// [fromUtc-90d, toUtc] и проверяет покрытие.
		/// </summary>
		private static async Task<IndicatorsDailyUpdater> BuildIndicatorsAsync (
			HttpClient http,
			DateTime fromUtc,
			DateTime toUtc )
			{
			var indicators = new IndicatorsDailyUpdater (http);

			// Берётся чуть расширенное окно, чтобы индикаторы были стабильными в начале интервала.
			var indicatorsFrom = fromUtc.AddDays (-90);

			await indicators.UpdateAllAsync (
				indicatorsFrom,
				toUtc,
				IndicatorsDailyUpdater.FillMode.NeutralFill);

			indicators.EnsureCoverageOrFail (indicatorsFrom, toUtc);

			return indicators;
			}

		/// <summary>
		/// Строит дневные строки:
		/// - allRows — все дни в окне;
		/// - mornings — отфильтрованные утренние точки в NY-окне.
		/// </summary>
		private static async Task<DailyRowsBundle> BuildDailyRowsBundleAsync (
			IndicatorsDailyUpdater indicators,
			DateTime fromUtc,
			DateTime toUtc,
			List<Candle6h> solAll6h,
			List<Candle6h> btcAll6h,
			List<Candle6h> paxgAll6h,
			List<Candle1m> sol1m )
			{
			var rowsBundle = await BuildDailyRowsAsync (
				indicators,
				fromUtc,
				toUtc,
				solAll6h,
				btcAll6h,
				paxgAll6h,
				sol1m);

			return rowsBundle;
			}
		}
	}
