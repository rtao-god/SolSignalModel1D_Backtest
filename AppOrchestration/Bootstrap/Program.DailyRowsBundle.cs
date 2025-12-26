using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: вспомогательные контейнеры бутстрапа.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Пакет дневных строк:
		/// - AllRows  — вся выборка (все дни в окне);
		/// - Mornings — только утренние точки (NY-окно входа).
		/// </summary>
		private sealed class DailyRowsBundle
			{
			public List<LabeledCausalRow> AllRows { get; init; } = new ();
			public List<LabeledCausalRow> Mornings { get; init; } = new ();
			}

		/// <summary>
		/// Результат инфраструктурного бутстрапа:
		/// - все нужные свечные ряды;
		/// - from/to окна бэктеста;
		/// - дневные строки (allRows + mornings).
		/// </summary>
		private sealed class BootstrapData
			{
			public List<Candle6h> SolAll6h { get; init; } = new ();
			public List<Candle6h> BtcAll6h { get; init; } = new ();
			public List<Candle6h> PaxgAll6h { get; init; } = new ();
			public List<Candle1h> SolAll1h { get; init; } = new ();
			public List<Candle1m> Sol1m { get; init; } = new ();

			public DateTime FromUtc { get; init; }
			public DateTime ToUtc { get; init; }

			public DailyRowsBundle RowsBundle { get; init; } = null!;
			}
		}
	}
