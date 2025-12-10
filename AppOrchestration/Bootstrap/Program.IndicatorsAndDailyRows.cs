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
			// Отдельный апдейтер для дневных индикаторов (FNG/DXY/другие рядовые фичи).
			var indicators = new IndicatorsDailyUpdater (http);

			// Берётся чуть расширенное окно, чтобы индикаторы были стабильными в начале интервала.
			// Альтернатива — начинать ровно с fromUtc, но тогда первые дни будут "греться".
			var indicatorsFrom = fromUtc.AddDays (-90);

			await indicators.UpdateAllAsync (
				indicatorsFrom,
				toUtc,
				IndicatorsDailyUpdater.FillMode.NeutralFill);

			// Явно валидируем, что для всего интервала есть данные.
			// Это делает падение ранним и предсказуемым, а не в глубине пайплайна.
			indicators.EnsureCoverageOrFail (indicatorsFrom, toUtc);

			return indicators;
			}
		}
	}
