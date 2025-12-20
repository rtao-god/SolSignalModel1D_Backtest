using SolSignalModel1D_Backtest.Core.Data.Indicators;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: обновление индикаторов и построение дневных строк.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Обновляет все дневные индикаторы на интервале [fromUtc..toUtc] и проверяет покрытие.
		/// </summary>
		private static async Task<IndicatorsDailyUpdater> BuildIndicatorsAsync (
			HttpClient http,
			DateTime fromUtc,
			DateTime toUtc )
			{
			// Дневные индикаторы (FNG/DXY).
			var indicators = new IndicatorsDailyUpdater (http);

			await indicators.UpdateAllAsync (
				rangeStartUtc: fromUtc,
				rangeEndUtc: toUtc,
				fngFillMode: IndicatorsDailyUpdater.FillMode.Strict,
				dxyFillMode: IndicatorsDailyUpdater.FillMode.NeutralFill
			);

			// Гарантия, что пайплайн дальше не пойдёт на дырявых/битых рядах.
			indicators.EnsureCoverageOrFail (fromUtc, toUtc);

			return indicators;
			}
		}
	}
