using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Metrics.PolicyRatios;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Snapshots.PolicyRatios
	{
	public sealed class PolicyRatiosPerPolicy
		{
		public required string PolicyName { get; init; }

		public int TradesCount { get; set; }

		public double TotalPnlPct { get; set; }

		public double MaxDdPct { get; set; }

		public double Mean { get; set; }

		public double Std { get; set; }

		public double DownStd { get; set; }

		public double Sharpe { get; set; }

		public double Sortino { get; set; }

		public double Cagr { get; set; }

		public double Calmar { get; set; }

		public double WinRate { get; set; }

		public double WithdrawnTotal { get; set; }

		public bool HadLiquidation { get; set; }
		}

	public sealed class PolicyRatiosSnapshot
		{
		public required string BacktestId { get; init; }

		public required IReadOnlyList<PolicyRatiosPerPolicy> Policies { get; init; }

		public int PoliciesCount => Policies.Count;
		}

	public static class PolicyRatiosSnapshotBuilder
		{
		public static PolicyRatiosSnapshot Build (
			IEnumerable<BacktestPolicyResult> results,
			string backtestId )
			{
			if (results == null) throw new ArgumentNullException (nameof (results));
			if (string.IsNullOrWhiteSpace (backtestId))
				throw new ArgumentException ("[policy-ratios] backtestId is required.", nameof (backtestId));

			var list = results.ToList ();

			var policies = new List<PolicyRatiosPerPolicy> (list.Count);

			foreach (var r in list)
				{
				var m = PolicyRatiosMetricsCalculator.Compute (r);

				policies.Add (new PolicyRatiosPerPolicy
					{
					PolicyName = r.PolicyName,
					TradesCount = m.TradesCount,
					TotalPnlPct = r.TotalPnlPct,
					MaxDdPct = r.MaxDdPct,
					Mean = m.Mean,
					Std = m.Std,
					DownStd = m.DownStd,
					Sharpe = m.Sharpe,
					Sortino = m.Sortino,
					Cagr = m.Cagr,
					Calmar = m.Calmar,
					WinRate = m.WinRate,
					WithdrawnTotal = r.WithdrawnTotal,
					HadLiquidation = r.HadLiquidation
					});
				}

			return new PolicyRatiosSnapshot
				{
				BacktestId = backtestId,
				Policies = policies
				};
			}
		}
	}
