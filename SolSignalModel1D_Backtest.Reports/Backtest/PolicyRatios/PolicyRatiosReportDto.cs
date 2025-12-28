using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Snapshots.PolicyRatios;

namespace SolSignalModel1D_Backtest.Reports.Backtest.PolicyRatios
	{
	public sealed class PolicyRatiosPerPolicyDto
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

		public double WinRatePct { get; set; }

		public double WithdrawnUsd { get; set; }

		public bool HadLiquidation { get; set; }
		}

	public sealed class PolicyRatiosReportDto
		{
		public string BacktestId { get; set; } = string.Empty;

		public DateTime? FromDateUtc { get; set; }

		public DateTime? ToDateUtc { get; set; }

		public List<PolicyRatiosPerPolicyDto> Policies { get; set; } = new ();
		}

	public static class PolicyRatiosReportBuilder
		{
		public static PolicyRatiosReportDto Build (
			PolicyRatiosSnapshot snapshot,
			DateTime? fromDateUtc = null,
			DateTime? toDateUtc = null )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));

			var dto = new PolicyRatiosReportDto
				{
				BacktestId = snapshot.BacktestId,
				FromDateUtc = fromDateUtc,
				ToDateUtc = toDateUtc,
				Policies = snapshot.Policies
					.Select (p => new PolicyRatiosPerPolicyDto
						{
						PolicyName = p.PolicyName,
						TradesCount = p.TradesCount,
						TotalPnlPct = p.TotalPnlPct,
						MaxDdPct = p.MaxDdPct,
						Mean = p.Mean,
						Std = p.Std,
						DownStd = p.DownStd,
						Sharpe = p.Sharpe,
						Sortino = p.Sortino,
						Cagr = p.Cagr,
						Calmar = p.Calmar,
						WinRatePct = p.WinRate * 100.0,
						WithdrawnUsd = p.WithdrawnTotal,
						HadLiquidation = p.HadLiquidation
						})
					.ToList ()
				};

			return dto;
			}
		}
	}
