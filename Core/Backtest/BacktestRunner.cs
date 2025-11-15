using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Верхнеуровневый “дирижёр”, куда Program.cs/тесты передают уже готовые данные:
	/// mornings (NY-окна), records (PredictionRecord), 1m-свечи, и список политик.
	/// Он просто конфигурирует RollingLoop и запускает.
	/// </summary>
	public sealed class BacktestRunner
		{
		public sealed class Config
			{
			public double DailyStopPct { get; init; } = 0.05;
			public double DailyTpPct { get; init; } = 0.03;
			}

		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			Config? cfg = null )
			{
			cfg ??= new Config ();

			var loop = new RollingLoop ();
			loop.Run (
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				dailyStopPct: cfg.DailyStopPct,
				dailyTpPct: cfg.DailyTpPct
			);
			}
		}
	}
