using System;

namespace SolSignalModel1D_Backtest.Reports
	{
	/// <summary>
	/// Заглушка под будущий отчёт по бэктесту.
	/// Сейчас нужна только для компиляции API-эндпоинта /api/backtest/summary.
	/// Когда появится реальный отчёт, это место можно расширить.
	/// </summary>
	public sealed class BacktestSummaryReport
		{
		/// <summary>
		/// Момент генерации отчёта.
		/// </summary>
		public DateTime GeneratedAtUtc { get; set; }

		/// <summary>
		/// Произвольный комментарий или версия отчёта.
		/// </summary>
		public string Notes { get; set; } = string.Empty;
		}
	}
