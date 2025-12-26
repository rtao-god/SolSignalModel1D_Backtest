using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Metrics.PolicyRatios;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.PolicyRatios
	{
	public sealed class PolicyRatiosPerPolicy
		{
		public string PolicyName { get; set; } = string.Empty;

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
		public string BacktestId { get; set; } = string.Empty;

		public List<PolicyRatiosPerPolicy> Policies { get; } = new ();

		public int PoliciesCount => Policies.Count;
		}

	public static class PolicyRatiosSnapshotBuilder
		{
		public static PolicyRatiosSnapshot Build (
			IEnumerable<BacktestPolicyResult> results,
			string? backtestId = null )
			{
			if (results == null) throw new ArgumentNullException (nameof (results));

			var list = results.ToList ();

			var snapshot = new PolicyRatiosSnapshot
				{
				BacktestId = backtestId ?? string.Empty
				};

			foreach (var r in list)
				{
				var m = PolicyRatiosMetricsCalculator.Compute (r);

				snapshot.Policies.Add (new PolicyRatiosPerPolicy
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

			return snapshot;
			}
		}
	}
