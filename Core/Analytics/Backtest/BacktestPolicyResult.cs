using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	public sealed class BacktestPolicyResult
		{
		public string PolicyName { get; set; } = string.Empty;
		public MarginMode Margin { get; set; }

		public List<PnLTrade> Trades { get; set; } = new List<PnLTrade> ();

		// === из ComputePnL ===
		public double TotalPnlPct { get; set; }
		public double MaxDdPct { get; set; }
		public Dictionary<string, int> TradesBySource { get; set; } =
			new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
		public double WithdrawnTotal { get; set; }
		public List<PnlBucketSnapshot> BucketSnapshots { get; set; } = new List<PnlBucketSnapshot> ();
		public bool HadLiquidation { get; set; }
		}
	}
