using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest
	{
	/// <summary>
	/// Итог бэктеста для заданного BacktestConfig.
	/// Содержит:
	/// - использованный конфиг;
	/// - интервал дат и число сигналов;
	/// - результаты по политикам (BASE/ANTI-D × WITH SL / NO SL);
	/// - агрегированные метрики по всем политикам.
	/// </summary>
	public sealed class BacktestSummary
		{
		/// <summary>
		/// Конфигурация бэктеста, по которой был выполнен прогон.
		/// </summary>
		public BacktestConfig Config { get; init; } = null!;

		/// <summary>
		/// Первая дата утреннего сигнала (UTC).
		/// </summary>
		public DateTime FromDateUtc { get; init; }

		/// <summary>
		/// Последняя дата утреннего сигнала (UTC).
		/// </summary>
		public DateTime ToDateUtc { get; init; }

		/// <summary>
		/// Количество сигналов (mornings.Count).
		/// </summary>
		public int SignalDays { get; init; }

		/// <summary>
		/// Результаты политик в базовом направлении (BASE, WITH SL).
		/// </summary>
		public List<BacktestPolicyResult> WithSlBase { get; init; } = new ();

		/// <summary>
		/// Результаты политик в базовом направлении (BASE, NO SL).
		/// </summary>
		public List<BacktestPolicyResult> NoSlBase { get; init; } = new ();

		/// <summary>
		/// Результаты политик с Anti-D overlay (ANTI-D, WITH SL).
		/// </summary>
		public List<BacktestPolicyResult> WithSlAnti { get; init; } = new ();

		/// <summary>
		/// Результаты политик с Anti-D overlay (ANTI-D, NO SL).
		/// </summary>
		public List<BacktestPolicyResult> NoSlAnti { get; init; } = new ();

		/// <summary>
		/// Лучшая суммарная доходность (TotalPnlPct) среди всех политик и веток.
		/// </summary>
		public double BestTotalPnlPct { get; init; }

		/// <summary>
		/// Худший MaxDD (в процентах) среди всех политик и веток.
		/// </summary>
		public double WorstMaxDdPct { get; init; }

		/// <summary>
		/// Количество политик, у которых была хотя бы одна ликвидация.
		/// </summary>
		public int PoliciesWithLiquidation { get; init; }

		/// <summary>
		/// Общее количество сделок по всем политикам и веткам.
		/// </summary>
		public int TotalTrades { get; init; }
		}
	}
