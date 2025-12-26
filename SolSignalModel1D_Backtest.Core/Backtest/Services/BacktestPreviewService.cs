using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Backtest.Services
{
	/// <summary>
	/// Сервис для "what-if" бэктеста (preview) на уже подготовленных данных.
	/// Здесь НЕТ загрузки свечей, индикаторов и моделей — только:
	/// 1) маппинг BacktestConfig → PolicySpec[]
	/// 2) вызов BacktestEngine.RunBacktest(...)
	/// </summary>
	public sealed class BacktestPreviewService
	{
		/// <summary>
		/// Запускает превью-бэктест на заданном конфиге и данных.
		/// Предполагается, что:
		/// - mornings/records/candles1m собраны тем же пайплайном, что и baseline;
		/// - config уже содержит нужный набор политик (после SelectedPolicies-фильтрации).
		/// </summary>
		/// <param name="mornings">
		/// Дневные точки (NY-утро), те же, что идут в baseline-бэктест.
		/// </param>
		/// <param name="records">
		/// PredictionRecord с уже проставленными полями (dir/micro/SL/Delayed и т.п.).
		/// </param>
		/// <param name="candles1m">
		/// Минутные свечи SOL/USDT для PnL-движка.
		/// </param>
		/// <param name="config">
		/// Конфигурация бэктеста (SL/TP + политики плеча/маржи).
		/// </param>
		/// <returns>BacktestSummary, из которого потом строится ReportDocument/DTO.</returns>
		public BacktestSummary RunPreview (
			IReadOnlyList<LabeledCausalRow> mornings,
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			BacktestConfig config)
		{
			if (mornings == null || mornings.Count == 0)
				throw new ArgumentException("mornings must be non-empty", nameof(mornings));
			if (records == null || records.Count == 0)
				throw new ArgumentException("records must be non-empty", nameof(records));
			if (candles1m == null || candles1m.Count == 0)
				throw new ArgumentException("candles1m must be non-empty", nameof(candles1m));
			if (config == null)
				throw new ArgumentNullException(nameof(config));

			// 1) Строим PolicySpec[] через уже существующую фабрику,
			//    чтобы не дублировать логику выбора ILeveragePolicy.
			//    Это тот же путь, что используется в консольном baseline-бэктесте.
			var policies = BacktestPolicyFactory.BuildPolicySpecs(config);
			if (policies == null || policies.Count == 0)
				throw new InvalidOperationException("BacktestPreview: после BuildPolicySpecs список политик пуст.");

			// 2) Универсальный движок бэктеста:
			//    BASE/ANTI-D × WITH SL / NO SL → BacktestSummary.
			//    Здесь нет консольного вывода; только чистые данные.
			var summary = BacktestEngine.RunBacktest(
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				config: config);

			return summary;
		}
	}
}
