using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Trading;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Верхнеуровневый “дирижёр”, куда Program.cs/тесты передают уже готовые данные:
	/// mornings (NY-окна), records (PredictionRecord), 1m-свечи, и список политик.
	/// Он конфигурирует RollingLoop и запускает, + печатает модельные метрики.
	/// </summary>
	public sealed class BacktestRunner
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		public sealed class Config
			{
			public double DailyStopPct { get; init; } = 0.05; // SL%
			public double DailyTpPct { get; init; } = 0.03;   // TP%
			}

		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			Config? cfg = null )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));
			if (policies == null) throw new ArgumentNullException (nameof (policies));

			cfg ??= new Config ();

			// 1) Модельные метрики (дневная confusion + SL path-based, 1m)
			BacktestModelStatsPrinter.Print (
				records,
				candles1m,
				cfg.DailyTpPct,
				cfg.DailyStopPct,
				NyTz);

			// 2) Запуск PnL/Delayed/окон по политикам
			var loop = new RollingLoop ();
			loop.Run (
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				dailyStopPct: cfg.DailyStopPct,
				dailyTpPct: cfg.DailyTpPct
			);
			DelayedStatsPrinter.Print (records);
			}
		}
	}
