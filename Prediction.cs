using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
	{
	internal partial class Program
		{
		private static async Task<List<PredictionRecord>> LoadPredictionRecordsAsync (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<Candle6h> solAll6h,
			PredictionEngine engine )
			{
			// Prepare sorted 6h list for forward range calculations
			var sorted6h = solAll6h is List<Candle6h> list6h ? list6h : solAll6h.ToList ();
			int usedHeuristic = 0;
			var list = new List<PredictionRecord> (mornings.Count);

			foreach (var r in mornings)
				{
				var pr = engine.Predict (r);

				int cls = pr.Class;
				bool microUp = pr.Micro.ConsiderUp;
				bool microDn = pr.Micro.ConsiderDown;
				string reason = pr.Reason;

				if (string.Equals (pr.Reason, "fallback", StringComparison.OrdinalIgnoreCase))
					{
					var h = HeuristicPredict (r);
					cls = h.Class;
					microUp = h.MicroUp;
					microDn = h.MicroDown;
					reason = $"heur:{h.Reason}";
					usedHeuristic++;
					}

				// Вычисляем показатели по forward-окну (до базового выхода t_exit)
				DateTime entryUtc = r.Date;

				// Здесь также используем общий NyTz, чтобы baseline-окно было согласовано с остальными расчётами.
				DateTime exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz);

				int entryIdx = sorted6h.FindIndex (c => c.OpenTimeUtc == entryUtc);
				if (entryIdx < 0)
					throw new InvalidOperationException ($"[forward] entry candle {entryUtc:O} not found in 6h series");

				int exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					DateTime end = (i + 1 < sorted6h.Count)
						? sorted6h[i + 1].OpenTimeUtc
						: start.AddHours (6);
					if (exitUtc >= start && exitUtc <= end)
						{
						exitIdx = i;
						break;
						}
					}
				if (exitIdx < 0)
					{
					Console.WriteLine ($"[forward] no 6h candle covering baseline exit {exitUtc:O} (entry {entryUtc:O})");
					throw new InvalidOperationException ($"[forward] no 6h candle covering baseline exit {exitUtc:O}");
					}
				if (exitIdx <= entryIdx)
					{
					throw new InvalidOperationException ($"[forward] exitIdx {exitIdx} <= entryIdx {entryIdx}");
					}

				double entryPrice = sorted6h[entryIdx].Close;
				double maxHigh = double.MinValue;
				double minLow = double.MaxValue;
				for (int j = entryIdx + 1; j <= exitIdx; j++)
					{
					var c = sorted6h[j];
					if (c.High > maxHigh) maxHigh = c.High;
					if (c.Low < minLow) minLow = c.Low;
					}
				if (maxHigh == double.MinValue || minLow == double.MaxValue)
					{
					throw new InvalidOperationException ($"[forward] no candles between entry {entryUtc:O} and exit {exitUtc:O}");
					}
				double fwdClose = sorted6h[exitIdx].Close;

				list.Add (new PredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,
					PredLabel = cls,

					PredMicroUp = microUp,
					PredMicroDown = microDn,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					Entry = entryPrice,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,

					RegimeDown = r.RegimeDown,
					Reason = reason,
					MinMove = r.MinMove,

					DelayedSource = string.Empty,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedEntryExecuted = false,
					DelayedEntryPrice = 0.0,
					DelayedIntradayResult = 0,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,
					TargetLevelClass = 0,
					DelayedWhyNot = null,
					DelayedEntryExecutedAtUtc = null,

					SlProb = 0.0,
					SlHighDecision = false
					});
				}

			Console.WriteLine ($"[predict] heuristic applied = {usedHeuristic}/{mornings.Count}");
			return await Task.FromResult (list);
			}

		private static (int Class, bool MicroUp, bool MicroDown, string Reason) HeuristicPredict ( DataRow r )
			{
			double up = 0, dn = 0;

			if (r.SolEma50vs200 > 0.005) up += 1.2;
			if (r.SolEma50vs200 < -0.005) dn += 1.2;
			if (r.BtcEma50vs200 > 0.0) up += 0.6;
			if (r.BtcEma50vs200 < 0.0) dn += 0.6;

			if (r.SolRet3 > 0) up += 0.7; else if (r.SolRet3 < 0) dn += 0.7;
			if (r.SolRet1 > 0) up += 0.4; else if (r.SolRet1 < 0) dn += 0.4;

			if (r.SolRsiCentered > +4) up += 0.7;
			if (r.SolRsiCentered < -4) dn += 0.7;

			if (r.BtcRet30 > 0) up += 0.3; else if (r.BtcRet30 < 0) dn += 0.3;
			if (r.DxyChg30 > 0.01) dn += 0.2;
			if (r.GoldChg30 > 0.01) dn += 0.1;

			double gap = Math.Abs (up - dn);
			bool move = (up >= 1.8 || dn >= 1.8) && gap >= 0.6;

			if (move)
				{
				return (up >= dn ? 2 : 0, false, false, $"move:{(up >= dn ? "up" : "down")}, u={up:0.00}, d={dn:0.00}");
				}
			else
				{
				bool microUp = up > dn + 0.3;
				bool microDn = dn > up + 0.3;

				if (!microUp && !microDn)
					{
					if (r.RsiSlope3 > +8) microUp = true;
					else if (r.RsiSlope3 < -8) microDn = true;
					}

				return (1, microUp, microDn, $"flat: u={up:0.00} d={dn:0.00} rsiSlope={r.RsiSlope3:0.0}");
				}
			}

		private static PredictionEngine CreatePredictionEngineOrFallback ()
			{
			var bundle = new ModelBundle
				{
				MlCtx = null,
				MoveModel = null,
				DirModelNormal = null,
				DirModelDown = null,
				MicroFlatModel = null
				};
			return new PredictionEngine (bundle);
			}
		}
	}
