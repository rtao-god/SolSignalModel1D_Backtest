using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Верхнеуровневый “дирижёр”, куда Program.cs/тесты передают уже готовые данные:
	/// mornings (NY-окна), records (PredictionRecord), 1m-свечи и политики плеча.
	/// Он получает BacktestConfig, конфигурирует RollingLoop и печатает модельные метрики.
	/// </summary>
	public sealed class BacktestRunner
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Запускает бэктест по готовым данным и заранее собранным PolicySpec.
		/// Все "магические числа" (SL/TP, набор политик) приходят через BacktestConfig.
		/// </summary>
		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			BacktestConfig config )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));
			if (policies == null) throw new ArgumentNullException (nameof (policies));
			if (config == null) throw new ArgumentNullException (nameof (config));

			// 1) Модельные метрики (дневная confusion + SL path-based, 1m)
			BacktestModelStatsPrinter.Print (
				records,
				candles1m,
				config.DailyTpPct,
				config.DailyStopPct,
				NyTz);

			// 2) Запуск PnL/Delayed/окон по политикам
			var loop = new RollingLoop ();
			loop.Run (
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				config: config);

			DelayedStatsPrinter.Print (records);
			}
		}
	}
