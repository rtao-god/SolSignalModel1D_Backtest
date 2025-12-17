using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Delayed
	{
	public static class PullbackContinuationOfflineBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;
		private static readonly double[] DeepFactors = new[] { 0.35, 0.45, 0.55 };
		private const double DeepMaxDelayHours = 4.0;

		/// <summary>
		/// Совместимый API: возвращает только ML-сэмплы.
		/// Для диагностики используйте BuildContexts(...).
		/// </summary>
		public static List<PullbackContinuationSample> Build (
			List<BacktestRecord> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var ctx = BuildContexts (rows, sol1h, sol6hDict);
			return ctx.Select (c => c.Sample).ToList ();
			}

		/// <summary>
		/// Возвращает ML-сэмплы + контекст, чтобы можно было отлаживать:
		/// - почему день попал/не попал в датасет,
		/// - какие базовые исходы и delayed-исходы,
		/// - какие факторы (DeepFactors) дали label=true/false.
		/// </summary>
		public static List<PullbackContinuationContext> BuildContexts (
			List<BacktestRecord> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var res = new List<PullbackContinuationContext> (rows?.Count * 4 ?? 0);

			if (rows == null || rows.Count == 0)
				return res;

			if (sol1h == null || sol1h.Count == 0)
				return res;

			if (sol6hDict == null || sol6hDict.Count == 0)
				return res;

			var allHours = sol1h.OrderBy (h => h.OpenTimeUtc).ToList ();

			foreach (var r in rows)
				{
				var entryUtc = r.ToCausalDateUtc ();

				if (!sol6hDict.TryGetValue (entryUtc, out var day6h))
					continue;

				// КРИТИЧНО: dict ключится по OpenTimeUtc, значит Close/High/Low этой 6h-свечи
				// содержат будущее относительно entryUtc. Для каузального entry используем ТОЛЬКО Open.
				double entry = day6h.Open;
				if (entry <= 0.0) continue;

				double minMove = r.MinMove;
				if (double.IsNaN (minMove) || double.IsInfinity (minMove) || minMove <= 0.0)
					{
					throw new InvalidOperationException (
						$"[delayed-offline] invalid MinMove for {entryUtc:O}: {minMove}. " +
						"Fix MinMoveEngine/RowBuilder; do not default here.");
					}

				DateTime endUtc;
				try { endUtc = Windowing.ComputeBaselineExitUtc (entryUtc, NyTz); }
				catch (Exception ex)
					{
					throw new InvalidOperationException (
						$"Failed to compute baseline exit for entryUtc={entryUtc:o}, tz={NyTz.Id}. " +
						"Fix data/windowing logic instead of relying on fallback.",
						ex);
					}

				var dayHours = allHours
					.Where (h => h.OpenTimeUtc >= entryUtc && h.OpenTimeUtc < endUtc)
					.ToList ();

				if (dayHours.Count == 0)
					continue;

				BuildForDir (res, r, entryUtc, dayHours, allHours, entry, minMove, goLong: true, NyTz);
				BuildForDir (res, r, entryUtc, dayHours, allHours, entry, minMove, goLong: false, NyTz);
				}

			return res;
			}

		private static void BuildForDir (
			List<PullbackContinuationContext> sink,
			BacktestRecord r,
			DateTime entryUtc,
			List<Candle1h> dayHours,
			IReadOnlyList<Candle1h> allHours,
			double entryPrice,
			double dayMinMove,
			bool goLong,
			TimeZoneInfo nyTz )
			{
			bool goShort = !goLong;
			bool strong = true;

			var baseOutcome = HourlyTradeEvaluator.EvaluateOne (
				dayHours, entryUtc, goLong, goShort, entryPrice, dayMinMove, strong, nyTz);

			// Датасет модели A строится только на "плохих" днях (SL-first на базовом входе).
			if (baseOutcome.Result != HourlyTradeResult.SlFirst)
				return;

			foreach (var f in DeepFactors)
				{
				var delayed = DelayedEntryEvaluator.Evaluate (
					dayHours, entryUtc, goLong, goShort, entryPrice, dayMinMove, strong, f, DeepMaxDelayHours);

				bool label = false;

				if (delayed.Executed)
					{
					if (delayed.Result == DelayedIntradayResult.TpFirst)
						{
						label = true;
						}
					else if (delayed.Result == DelayedIntradayResult.SlFirst &&
							 baseOutcome.SlPct > 0 &&
							 delayed.SlPct > 0 &&
							 delayed.SlPct < baseOutcome.SlPct * 0.7)
						{
						// "спасло" = получили SL заметно меньше базового.
						label = true;
						}
					}

				// Важно: TargetLevelFeatureBuilder обязан быть каузальным относительно entryUtc.
				// Здесь передаётся полный ряд, чтобы builder мог взять историческое окно без копирований.
				var feats = TargetLevelFeatureBuilder.Build (
					entryUtc, goLong, strong, dayMinMove, entryPrice, allHours);

				var sample = new PullbackContinuationSample
					{
					Label = label,
					Features = feats,
					EntryUtc = entryUtc
					};

				sink.Add (new PullbackContinuationContext
					{
					Record = r,
					EntryUtc = entryUtc,
					GoLong = goLong,
					DelayFactor = f,
					EntryPrice12 = entryPrice,
					MinMove = dayMinMove,

					BaseResult = baseOutcome.Result,
					BaseTpPct = baseOutcome.TpPct,
					BaseSlPct = baseOutcome.SlPct,

					DelayedExecuted = delayed.Executed,
					DelayedExecutedAtUtc = delayed.ExecutedAtUtc,
					DelayedResult = delayed.Result,
					DelayedTpPct = delayed.TpPct,
					DelayedSlPct = delayed.SlPct,
					TargetEntryPrice = delayed.TargetEntryPrice,

					Label = label,
					Sample = sample
					});
				}
			}
		}
	}
