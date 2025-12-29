namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.StrategySimulators
	{
	/// <summary>
	/// Консольный вывод параметров сценарной стратегии (одного пресета).
	/// Вынесено отдельно, чтобы:
	/// - не смешивать математику симулятора и логирование;
	/// - всегда видеть, какие именно offsets/риск использовались в прогоне.
	/// </summary>
	public static class StrategyParametersPrinter
		{
		public static void Print ( StrategyParameters p )
			{
			if (p == null) throw new ArgumentNullException (nameof (p));

			Console.WriteLine ();
			Console.WriteLine ($"[strategy:params] preset = {p.Name}");
			Console.WriteLine ("  -- volumes --");
			Console.WriteLine ($"  BaseStakeUsd              = {p.BaseStakeUsd:F2}");
			Console.WriteLine ($"  HedgeStakeUsd             = {p.HedgeStakeUsd:F2}");

			Console.WriteLine ("  -- price offsets (USD) --");
			Console.WriteLine ($"  BaseTpOffsetUsd           = {p.BaseTpOffsetUsd:F2}");
			Console.WriteLine ($"  HedgeTriggerOffsetUsd     = {p.HedgeTriggerOffsetUsd:F2}");
			Console.WriteLine ($"  HedgeStopOffsetUsd        = {p.HedgeStopOffsetUsd:F2}");
			Console.WriteLine ($"  HedgeTpOffsetUsd          = {p.HedgeTpOffsetUsd:F2}");
			Console.WriteLine ($"  SecondLegStopOffsetUsd    = {p.SecondLegStopOffsetUsd:F2}");
			Console.WriteLine ($"  DoublePositionTpOffsetUsd = {p.DoublePositionTpOffsetUsd:F2}");

			Console.WriteLine ("  -- risk --");
			Console.WriteLine ($"  InitialBalanceUsd         = {p.InitialBalanceUsd:F2}");
			Console.WriteLine ($"  TotalRiskFractionPerTrade = {p.TotalRiskFractionPerTrade:P2}");

			Console.WriteLine ();
			}
		}
	}
