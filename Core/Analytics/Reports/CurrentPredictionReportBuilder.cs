using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using SolSignalModel1D_Backtest.Reports.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Reports
	{
	/// <summary>
	/// Строит ReportDocument для "текущего прогноза" на основе PredictionRecord и политик плеча.
	/// Вся математика (SL/TP/liq) остаётся в Core, проект Reports — только DTO + I/O.
	/// </summary>
	public static class CurrentPredictionReportBuilder
		{
		public static ReportDocument? Build (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<ILeveragePolicy> policies,
			double walletBalanceUsd )
			{
			if (records == null || records.Count == 0) return null;
			if (policies == null || policies.Count == 0) return null;

			var last = records
				.OrderBy (r => r.DateUtc)
				.Last ();

			var doc = new ReportDocument
				{
				Id = $"current-prediction-{last.DateUtc:yyyyMMdd}",
				Kind = "current_prediction",
				Title = "Текущий прогноз (SOLUSDT)",
				GeneratedAtUtc = DateTime.UtcNow
				};

			// === Общие поля прогноза ===
			var info = new KeyValueSection
				{
				Title = "Общие параметры прогноза"
				};

			info.Items.Add (new KeyValueItem { Key = "DateUtc", Value = last.DateUtc.ToString ("O") });
			info.Items.Add (new KeyValueItem { Key = "PredLabel", Value = last.PredLabel.ToString () });
			info.Items.Add (new KeyValueItem { Key = "Micro", Value = FormatMicro (last) });
			info.Items.Add (new KeyValueItem { Key = "RegimeDown", Value = last.RegimeDown.ToString () });
			info.Items.Add (new KeyValueItem { Key = "SlProb", Value = last.SlProb.ToString ("0.00") });
			info.Items.Add (new KeyValueItem { Key = "SlHighDecision", Value = last.SlHighDecision.ToString () });
			info.Items.Add (new KeyValueItem { Key = "Entry", Value = last.Entry.ToString ("0.0000") });
			info.Items.Add (new KeyValueItem { Key = "MinMove", Value = last.MinMove.ToString ("0.0000") });

			doc.KeyValueSections.Add (info);

			// === Forward (берём уже посчитанные поля PredictionRecord, без Windowing) ===
			if (last.MaxHigh24 > 0 && last.MinLow24 > 0 && last.Close24 > 0)
				{
				var fwd = new KeyValueSection
					{
					Title = "Forward 24h (baseline)"
					};

				fwd.Items.Add (new KeyValueItem { Key = "MaxHigh24", Value = last.MaxHigh24.ToString ("0.0000") });
				fwd.Items.Add (new KeyValueItem { Key = "MinLow24", Value = last.MinLow24.ToString ("0.0000") });
				fwd.Items.Add (new KeyValueItem { Key = "Close24", Value = last.Close24.ToString ("0.0000") });

				doc.KeyValueSections.Add (fwd);
				}

			// === Таблица по политикам (BASE vs ANTI-D) ===
			var table = new TableSection
				{
				Title = "Политики плеча (BASE vs ANTI-D)"
				};

			table.Columns.AddRange (new[]
			{
				"Policy",
				"Branch",
				"RiskDay",
				"HasDirection",
				"Skipped",
				"Direction",
				"Leverage",
				"Entry",
				"SL%",
				"TP%",
				"SL price",
				"TP price",
				"Position $",
				"Position qty",
				"Liq price",
				"Liq dist %"
			});

			foreach (var policy in policies)
				{
				AppendPolicyRows (table, last, policy, walletBalanceUsd);
				}

			doc.TableSections.Add (table);

			return doc;
			}

		// ===== Хелперы =====

		private static void AppendPolicyRows (
			TableSection table,
			PredictionRecord rec,
			ILeveragePolicy policy,
			double walletBalanceUsd )
			{
			bool hasDir = TryGetDirection (rec, out bool goLong, out bool goShort);
			bool isRiskDay = rec.SlHighDecision;
			double lev = policy.ResolveLeverage (rec);
			string policyName = policy.GetType ().Name;
			string direction = !hasDir ? "-" : (goLong ? "LONG" : "SHORT");

			// BASE ветка: торгует только нерискованные дни.
				{
				bool skipped = !hasDir || isRiskDay;
				var plan = skipped ? null : BuildTradePlan (rec, goLong, lev, walletBalanceUsd);

				table.Rows.Add (BuildRow (
					policyName,
					"BASE",
					isRiskDay,
					hasDir,
					skipped,
					direction,
					lev,
					rec.Entry,
					plan));
				}

			// ANTI-D ветка: торгует только рискованные дни.
				{
				bool skipped = !hasDir || !isRiskDay;
				var plan = skipped ? null : BuildTradePlan (rec, goLong, lev, walletBalanceUsd);

				table.Rows.Add (BuildRow (
					policyName,
					"ANTI-D",
					isRiskDay,
					hasDir,
					skipped,
					direction,
					lev,
					rec.Entry,
					plan));
				}
			}

		private sealed class TradePlan
			{
			public double SlPct { get; init; }
			public double TpPct { get; init; }
			public double SlPrice { get; init; }
			public double TpPrice { get; init; }
			public double? PositionUsd { get; init; }
			public double? PositionQty { get; init; }
			public double? LiqPrice { get; init; }
			public double? LiqDistPct { get; init; }
			}

		private static TradePlan BuildTradePlan (
			PredictionRecord rec,
			bool goLong,
			double leverage,
			double walletBalanceUsd )
			{
			double entry = rec.Entry;
			if (entry <= 0.0)
				throw new InvalidOperationException ("Entry <= 0 — нельзя построить торговый план.");

			double baseMinMove = rec.MinMove > 0.0 ? rec.MinMove : 0.02;
			double slPct = baseMinMove;
			if (slPct < 0.01) slPct = 0.01;
			else if (slPct > 0.04) slPct = 0.04;

			double tpPct = slPct * 1.5;
			if (tpPct < 0.015) tpPct = 0.015;

			double slPrice = goLong
				? entry * (1.0 - slPct)
				: entry * (1.0 + slPct);

			double tpPrice = goLong
				? entry * (1.0 + tpPct)
				: entry * (1.0 - tpPct);

			double? posUsd = null;
			double? posQty = null;
			double? liqPrice = null;
			double? liqDistPct = null;

			if (walletBalanceUsd > 0.0)
				{
				posUsd = walletBalanceUsd * leverage;
				posQty = posUsd / entry;

				if (leverage > 1.0)
					{
					const double mmr = 0.004;

					if (goLong)
						liqPrice = entry * (leverage - 1.0) / (leverage * (1.0 - mmr));
					else
						liqPrice = entry * (1.0 + leverage) / (leverage * (1.0 + mmr));

					if (liqPrice.HasValue)
						{
						liqDistPct = goLong
							? (entry - liqPrice.Value) / entry * 100.0
							: (liqPrice.Value - entry) / entry * 100.0;
						}
					}
				}

			return new TradePlan
				{
				SlPct = slPct * 100.0,
				TpPct = tpPct * 100.0,
				SlPrice = slPrice,
				TpPrice = tpPrice,
				PositionUsd = posUsd,
				PositionQty = posQty,
				LiqPrice = liqPrice,
				LiqDistPct = liqDistPct
				};
			}

		private static List<string> BuildRow (
			string policyName,
			string branch,
			bool isRiskDay,
			bool hasDirection,
			bool skipped,
			string direction,
			double leverage,
			double entry,
			TradePlan? plan )
			{
			string F ( double? v, string fmt ) => v.HasValue ? v.Value.ToString (fmt) : "-";

			return new List<string>
			{
				policyName,
				branch,
				isRiskDay.ToString(),
				hasDirection.ToString(),
				skipped.ToString(),
				direction,
				leverage.ToString("0.##"),
				entry.ToString("0.0000"),
				plan != null ? plan.SlPct.ToString("0.0")      : "-",
				plan != null ? plan.TpPct.ToString("0.0")      : "-",
				F(plan?.SlPrice,     "0.0000"),
				F(plan?.TpPrice,     "0.0000"),
				F(plan?.PositionUsd, "0.00"),
				F(plan?.PositionQty, "0.000"),
				F(plan?.LiqPrice,    "0.0000"),
				F(plan?.LiqDistPct,  "0.0")
			};
			}

		private static bool TryGetDirection (
			PredictionRecord rec,
			out bool goLong,
			out bool goShort )
			{
			goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
			goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
			return goLong || goShort;
			}

		private static string FormatMicro ( PredictionRecord r )
			{
			if (r.PredLabel != 1) return "не используется (не flat)";
			if (r.PredMicroUp) return "micro UP";
			if (r.PredMicroDown) return "micro DOWN";
			return "—";
			}
		}
	}
