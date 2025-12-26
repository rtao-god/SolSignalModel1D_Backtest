using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Time;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Расширенный отчёт по эффекту SL:
	/// - matched ΔPnL between with/without SL;
	/// - gated trades (по SlHighDecision);
	/// - PnL-разбивка (long/short, MAE/MFE, PF, R/R, exposure, skip_rate);
	/// - простые drawdown-метрики по active equity (MaxDD_active, TTR).
	/// </summary>
	public static class SlPnlReportPrinter
		{
		// ===== Публичные входы =====

		public static void PrintMatchedDeltaAndPnl (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<BacktestPolicyResult> withSl,
			IReadOnlyList<BacktestPolicyResult> noSl )
			{
			if (withSl == null || withSl.Count == 0) return;
			if (noSl == null || noSl.Count == 0) return;

			ConsoleStyler.WriteHeader ("==== SL EFFECT ON PNL (matched with / without SL) ====");

            var gatedDays = new HashSet<DayKeyUtc>(
				records
					.Where(r => r.SlHighDecision == true)
					.Select(r => CausalTimeKey.DayKeyUtc(r)));

            var globalDeltas = new List<double> ();
			var globalGatedDeltas = new List<double> ();
			var globalBaselineNoSl = new List<double> ();

			foreach (var w in withSl)
				{
				var n = noSl.FirstOrDefault (x => x.PolicyName == w.PolicyName && x.Margin == w.Margin);
				if (n == null) continue;

				var perPolicyDeltas = new List<double> ();
				var perPolicyGatedDeltas = new List<double> ();
				var perPolicyBaselineNoSl = new List<double> ();

				var mapNo = BuildTradeMap (n.Trades);

				if (w.Trades != null)
					{
					foreach (var tw in w.Trades)
						{
						var key = TradeKey (tw);
						if (!mapNo.TryGetValue (key, out var tn)) continue;

						double delta = tw.NetReturnPct - tn.NetReturnPct;
						perPolicyDeltas.Add (delta);
						globalDeltas.Add (delta);

                        bool isGatedDay =
							gatedDays.Contains(DayKeyUtc.FromUtcMomentOrThrow(tw.DateUtc))
							&& string.Equals(tw.Source, "Daily", StringComparison.OrdinalIgnoreCase);

                        if (isGatedDay)
							{
							perPolicyGatedDeltas.Add (delta);
							globalGatedDeltas.Add (delta);
							perPolicyBaselineNoSl.Add (tn.NetReturnPct);
							globalBaselineNoSl.Add (tn.NetReturnPct);
							}
						}
					}

				// skip_rate + exposure по этой политике
				double skipRate, avgExpWith, avgExpNo;
				ComputeSkipAndExposure (w, n, out skipRate, out avgExpWith, out avgExpNo);

				PrintPolicyDeltaBlock (
					w.PolicyName,
					w.Margin.ToString (),
					perPolicyDeltas,
					perPolicyGatedDeltas,
					perPolicyBaselineNoSl,
					skipRate,
					avgExpWith,
					avgExpNo);
				}

			PrintGlobalDeltaBlock (globalDeltas, globalGatedDeltas, globalBaselineNoSl);

			// Дополнительно: PnL-разбивка по WITH SL
			PrintPnlDistribution (withSl);
			}

		// ===== Matched ΔPnL helpers =====

		private static Dictionary<string, PnLTrade> BuildTradeMap ( IList<PnLTrade>? trades )
			{
			var dict = new Dictionary<string, PnLTrade> (StringComparer.Ordinal);
			if (trades == null) return dict;

			foreach (var t in trades)
				{
				var key = TradeKey (t);
				// если вдруг коллизия — берём первую, остальные игнорируем
				if (!dict.ContainsKey (key))
					dict[key] = t;
				}
			return dict;
			}

		private static string TradeKey ( PnLTrade t )
			{
			// по дню, источнику, корзине и направлению — для дневной модели нормально
			return $"{t.DateUtc:yyyy-MM-dd}|{t.Source}|{t.Bucket}|{t.IsLong}";
			}

		private static void ComputeSkipAndExposure (
			BacktestPolicyResult withSl,
			BacktestPolicyResult noSl,
			out double skipRate,
			out double avgExpWith,
			out double avgExpNo )
			{
			var withTrades = withSl.Trades ?? new List<PnLTrade> ();
			var noTrades = noSl.Trades ?? new List<PnLTrade> ();

			var daysWith = new HashSet<DateTime> (withTrades.Select (t => t.DateUtc.ToCausalDateUtc()));
			var daysNo = new HashSet<DateTime> (noTrades.Select (t => t.DateUtc.ToCausalDateUtc()));

			int baseDays = daysNo.Count;
			int skipped = baseDays > 0 ? daysNo.Except (daysWith).Count () : 0;
			skipRate = baseDays > 0 ? (double) skipped / baseDays : 0.0;

			double Exp ( IEnumerable<PnLTrade> tr ) =>
				tr.Any ()
					? tr.Average (x => (x.ExitTimeUtc - x.EntryTimeUtc).TotalHours)
					: 0.0;

			avgExpWith = Exp (withTrades);
			avgExpNo = Exp (noTrades);
			}

		private static void PrintPolicyDeltaBlock (
			string policyName,
			string margin,
			List<double> deltas,
			List<double> gatedDeltas,
			List<double> baselineNoSl,
			double skipRate,
			double avgExpWith,
			double avgExpNo )
			{
			ConsoleStyler.WriteHeader ($"[SL effect] Policy = {policyName}, Margin = {margin}");

			var t = new TextTable ();
			t.AddHeader ("metric", "all trades", "gated subset");

			if (deltas.Count > 0)
				{
				var sAll = BasicStats (deltas);
				var sGated = BasicStats (gatedDeltas);

				t.AddRow (
					"count",
					sAll.Count.ToString (),
					sGated.Count.ToString ());

				t.AddRow (
					"Δ Net% mean",
					$"{sAll.Mean:0.000}",
					sGated.Count > 0 ? $"{sGated.Mean:0.000}" : "n/a");

				t.AddRow (
					"Δ Net% median",
					$"{sAll.Median:0.000}",
					sGated.Count > 0 ? $"{sGated.Median:0.000}" : "n/a");

				t.AddRow (
					"Δ Net% p25 / p75",
					$"{sAll.P25:0.000} / {sAll.P75:0.000}",
					sGated.Count > 0 ? $"{sGated.P25:0.000} / {sGated.P75:0.000}" : "n/a");

				t.AddRow (
					"hit-rate (Δ>0)",
					$"{sAll.HitRatePos * 100.0:0.0}%",
					sGated.Count > 0 ? $"{sGated.HitRatePos * 100.0:0.0}%" : "n/a");
				}
			else
				{
				t.AddRow ("(no matched trades)", "", "");
				}

			// baseline PnL для gated subset (без SL)
			if (baselineNoSl.Count > 0)
				{
				var baseStats = BasicStats (baselineNoSl);
				t.AddRow (
					"baseline Net% mean (no-SL, gated)",
					$"{baseStats.Mean:0.000}",
					$"{baseStats.Median:0.000} (median)");
				}

			t.AddRow (
				"skip_rate (days no-SL but no trade with-SL)",
				$"{skipRate * 100.0:0.0}%",
				"");

			t.AddRow (
				"avg exposure hours (with / no SL)",
				$"{avgExpWith:0.00} / {avgExpNo:0.00}",
				"");

			t.WriteToConsole ();
			}

		private static void PrintGlobalDeltaBlock (
			List<double> allDeltas,
			List<double> gatedDeltas,
			List<double> baselineNoSl )
			{
			ConsoleStyler.WriteHeader ("[SL effect] Global ΔPnL summary (across policies)");

			var t = new TextTable ();
			t.AddHeader ("metric", "all policies");

			if (allDeltas.Count > 0)
				{
				var sAll = BasicStats (allDeltas);
				t.AddRow ("count (all matched trades)", sAll.Count.ToString ());
				t.AddRow ("Δ Net% mean (all)", $"{sAll.Mean:0.000}");
				t.AddRow ("Δ Net% median (all)", $"{sAll.Median:0.000}");
				t.AddRow ("Δ Net% p25 / p75 (all)", $"{sAll.P25:0.000} / {sAll.P75:0.000}");
				t.AddRow ("hit-rate (Δ>0, all)", $"{sAll.HitRatePos * 100.0:0.0}%");
				}

			if (gatedDeltas.Count > 0)
				{
				var sG = BasicStats (gatedDeltas);
				t.AddRow ("Δ Net% mean (gated)", $"{sG.Mean:0.000}");
				t.AddRow ("Δ Net% median (gated)", $"{sG.Median:0.000}");
				t.AddRow ("Δ Net% p25 / p75 (gated)", $"{sG.P25:0.000} / {sG.P75:0.000}");
				t.AddRow ("hit-rate (Δ>0, gated)", $"{sG.HitRatePos * 100.0:0.0}%");
				}

			if (baselineNoSl.Count > 0)
				{
				var b = BasicStats (baselineNoSl);
				t.AddRow ("baseline Net% mean (no-SL, gated)", $"{b.Mean:0.000}");
				t.AddRow ("baseline Net% median (no-SL, gated)", $"{b.Median:0.000}");
				}

			t.WriteToConsole ();
			}

		// ===== PnL distribution / MAE/MFE / PF / R/R / Drawdown =====

		public static void PrintPnlDistribution ( IReadOnlyList<BacktestPolicyResult> withSlResults )
			{
			if (withSlResults == null || withSlResults.Count == 0) return;

			ConsoleStyler.WriteHeader ("==== PNL DISTRIBUTION / MAE-MFE / RISK METRICS (WITH SL) ====");

			foreach (var r in withSlResults.OrderBy (x => x.PolicyName).ThenBy (x => x.Margin.ToString ()))
				{
				var trades = r.Trades ?? new List<PnLTrade> ();
				if (trades.Count == 0) continue;

				ConsoleStyler.WriteHeader ($"[PNL metrics] Policy = {r.PolicyName}, Margin = {r.Margin}");

				var longs = trades.Where (t => t.IsLong).ToList ();
				var shorts = trades.Where (t => !t.IsLong).ToList ();

				double PnlUsd ( PnLTrade t ) => t.PositionUsd * t.NetReturnPct / 100.0;

				double sumPos = trades.Where (t => PnlUsd (t) > 0).Sum (PnlUsd);
				double sumNeg = trades.Where (t => PnlUsd (t) < 0).Sum (PnlUsd);
				double profitFactor = sumNeg < 0 ? sumPos / Math.Abs (sumNeg) : 0.0;

				var wins = trades.Where (t => t.NetReturnPct > 0).ToList ();
				var losses = trades.Where (t => t.NetReturnPct < 0).ToList ();

				double avgWinAbs = wins.Count > 0 ? wins.Average (t => t.NetReturnPct) : 0.0;
				double avgLossAbs = losses.Count > 0 ? Math.Abs (losses.Average (t => t.NetReturnPct)) : 0.0;
				double rr = avgWinAbs > 0 && avgLossAbs > 0 ? avgWinAbs / avgLossAbs : 0.0;

				var tTab = new TextTable ();
				tTab.AddHeader ("metric", "value");

				tTab.AddRow ("trades (all / long / short)",
					$"{trades.Count} / {longs.Count} / {shorts.Count}");

				// Net% distribution
				var allNet = trades.Select (t => t.NetReturnPct).ToList ();
				var stAll = BasicStats (allNet);
				tTab.AddRow ("Net% median (all)", $"{stAll.Median:0.000}");
				tTab.AddRow ("Net% p25/p75 (all)", $"{stAll.P25:0.000} / {stAll.P75:0.000}");

				if (longs.Count > 0)
					{
					var stL = BasicStats (longs.Select (t => t.NetReturnPct).ToList ());
					tTab.AddRow ("Net% median (long)", $"{stL.Median:0.000}");
					tTab.AddRow ("Net% p25/p75 (long)", $"{stL.P25:0.000} / {stL.P75:0.000}");
					}

				if (shorts.Count > 0)
					{
					var stS = BasicStats (shorts.Select (t => t.NetReturnPct).ToList ());
					tTab.AddRow ("Net% median (short)", $"{stS.Median:0.000}");
					tTab.AddRow ("Net% p25/p75 (short)", $"{stS.P25:0.000} / {stS.P75:0.000}");
					}

				// MAE / MFE
				var maeList = trades.Select (t => t.MaxAdversePct).ToList ();
				var mfeList = trades.Select (t => t.MaxFavorablePct).ToList ();
				var stMae = BasicStats (maeList);
				var stMfe = BasicStats (mfeList);
				tTab.AddRow ("MAE median/p75 (%)", $"{stMae.Median:0.000} / {stMae.P75:0.000}");
				tTab.AddRow ("MFE median/p75 (%)", $"{stMfe.Median:0.000} / {stMfe.P75:0.000}");

				// PF и R/R
				tTab.AddRow ("Profit factor (Σwin/|Σloss|)", $"{profitFactor:0.3}");
				tTab.AddRow ("R/R (avg win / avg loss)", rr > 0 ? $"{rr:0.3}" : "n/a");

				// Drawdown по active equity (на уровне политики)
				var (maxDdActive, ttrDays) = ComputeActiveDrawdown (r);
				tTab.AddRow ("MaxDD_active (approx)", $"{maxDdActive * 100.0:0.0}%");
				tTab.AddRow ("Time-to-recover (days, active)", ttrDays >= 0 ? $"{ttrDays:0.0}" : "never");

				tTab.WriteToConsole ();
				}
			}

		private static (double MaxDd, double TimeToRecoverDays) ComputeActiveDrawdown ( BacktestPolicyResult res )
			{
			var trades = res.Trades ?? new List<PnLTrade> ();
			var snaps = res.BucketSnapshots ?? new List<PnlBucketSnapshot> ();
			if (trades.Count == 0 || snaps.Count == 0) return (0.0, -1.0);

			// стартовые equity по бакетам
			var bucketEquity = snaps.ToDictionary (s => s.Name, s => s.StartCapital, StringComparer.OrdinalIgnoreCase);

			var curve = new List<(DateTime Time, double ActiveTotal)> ();

			foreach (var t in trades.OrderBy (x => x.ExitTimeUtc))
				{
				if (!bucketEquity.ContainsKey (t.Bucket))
					{
					// если вдруг новый бакет — считаем, что старт = EquityAfter первой сделки
					bucketEquity[t.Bucket] = t.EquityAfter;
					}
				else
					{
					bucketEquity[t.Bucket] = t.EquityAfter;
					}

				double activeTotal = bucketEquity.Values.Sum ();
				curve.Add ((t.ExitTimeUtc, activeTotal));
				}

			if (curve.Count == 0) return (0.0, -1.0);

			double peak = curve[0].ActiveTotal;
			DateTime peakTime = curve[0].Time;
			double maxDd = 0.0;
			DateTime ddValleyTime = curve[0].Time;
			DateTime ddPeakTime = peakTime;

			for (int i = 0; i < curve.Count; i++)
				{
				var (time, value) = curve[i];
				if (value > peak)
					{
					peak = value;
					peakTime = time;
					}

				if (peak > 1e-9)
					{
					double dd = (peak - value) / peak;
					if (dd > maxDd)
						{
						maxDd = dd;
						ddValleyTime = time;
						ddPeakTime = peakTime;
						}
					}
				}

			// Time-to-recover: ищем первое восстановление выше ddPeakTime до уровня peak
			if (maxDd <= 0.0) return (0.0, 0.0);

			double target = 0.0;
			// целевой "peak" на момент максимальной просадки
			foreach (var (time, value) in curve)
				{
				if (time <= ddPeakTime && value > target)
					target = value;
				}

			DateTime? recoverTime = null;
			foreach (var (time, value) in curve.Where (c => c.Time > ddValleyTime))
				{
				if (value >= target)
					{
					recoverTime = time;
					break;
					}
				}

			double ttrDays = recoverTime.HasValue
				? (recoverTime.Value - ddValleyTime).TotalDays
				: -1.0;

			return (maxDd, ttrDays);
			}

		// ===== Basic stats helper =====

		private readonly struct Stats
			{
			public int Count { get; init; }
			public double Mean { get; init; }
			public double Median { get; init; }
			public double P25 { get; init; }
			public double P75 { get; init; }
			public double HitRatePos { get; init; }
			}

		private static Stats BasicStats ( List<double> values )
			{
			if (values == null || values.Count == 0)
				{
				return new Stats
					{
					Count = 0,
					Mean = 0,
					Median = 0,
					P25 = 0,
					P75 = 0,
					HitRatePos = 0
					};
				}

			var arr = values.OrderBy (x => x).ToArray ();
			int n = arr.Length;

			double mean = arr.Average ();
			double MedianAt ( double q )
				{
				if (n == 1) return arr[0];
				double pos = q * (n - 1);
				int idx = (int) Math.Floor (pos);
				double frac = pos - idx;
				if (idx >= n - 1) return arr[n - 1];
				return arr[idx] * (1.0 - frac) + arr[idx + 1] * frac;
				}

			double median = MedianAt (0.5);
			double p25 = MedianAt (0.25);
			double p75 = MedianAt (0.75);
			double hitRatePos = values.Count (v => v > 0) / (double) n;

			return new Stats
				{
				Count = n,
				Mean = mean,
				Median = median,
				P25 = p25,
				P75 = p75,
				HitRatePos = hitRatePos
				};
			}
		}
	}
