using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;
using SolSignalModel1D_Backtest.Core.Causal.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Causal.ML.Delayed
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
			IReadOnlyList<BacktestRecord> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			var ctx = BuildContexts (rows, sol1h, sol6hDict);
			return ctx.Select (c => c.Sample).ToList ();
			}

		public static List<PullbackContinuationContext> BuildContexts (
			IReadOnlyList<BacktestRecord> rows,
			IReadOnlyList<Candle1h> sol1h,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol1h == null) throw new ArgumentNullException (nameof (sol1h));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			var res = new List<PullbackContinuationContext> (rows.Count * 4);

			if (rows.Count == 0)
				return res;

			if (sol1h.Count == 0)
				throw new ArgumentException ("sol1h must be non-empty.", nameof (sol1h));

			if (sol6hDict.Count == 0)
				throw new ArgumentException ("sol6hDict must be non-empty.", nameof (sol6hDict));

			var allHours = sol1h.OrderBy (h => h.OpenTimeUtc).ToList ();

			foreach (var r in rows)
				{
				// EntryUtc = реальный timestamp входа, а не day-key.
				var entryUtc = CausalTimeKey.EntryUtc (r);

                if (!sol6hDict.TryGetValue(entryUtc.Value, out var day6h))
                {
                    throw new InvalidOperationException(
                        $"[pullback-offline] sol6hDict has no key for entryUtc={entryUtc:O}. " +
                        "Это рассинхрон данных: sol6hDict должен ключиться по 6h OpenTimeUtc и покрывать все entryUtc из rows.");
                }

                if (day6h.OpenTimeUtc != entryUtc.Value)
                {
                    throw new InvalidOperationException(
                        $"[pullback-offline] 6h OpenTimeUtc mismatch for entryUtc. entryUtc={entryUtc:O}, day6h.OpenTimeUtc={day6h.OpenTimeUtc:O}.");
                }

                // Для каузального entry используем только Open.
                double entry = day6h.Open;
				if (entry <= 0.0)
					throw new InvalidOperationException ($"[pullback-offline] Invalid entry price (day6h.Open) for entryUtc={entryUtc:O}: {entry}.");

				double minMove = r.MinMove;
				if (!double.IsFinite (minMove) || minMove <= 0.0)
					{
					throw new InvalidOperationException (
						$"[pullback-offline] invalid MinMove for {entryUtc:O}: {minMove}. " +
						"Чинить нужно MinMoveEngine/RowBuilder, а не подставлять дефолты.");
					}

				DateTime endUtc;
                try { endUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, NyTz).Value; }
                catch (Exception ex)
					{
					throw new InvalidOperationException (
						$"Failed to compute baseline exit for entryUtc={entryUtc:o}, tz={NyTz.Id}.",
						ex);
					}

                var dayHours = allHours
					.Where(h => h.OpenTimeUtc >= entryUtc.Value && h.OpenTimeUtc < endUtc)
					.ToList();

                if (dayHours.Count == 0)
					throw new InvalidOperationException ($"[pullback-offline] No 1h candles in window. entryUtc={entryUtc:O}, endUtc={endUtc:O}.");

                BuildForDir(res, r, entryUtc.Value, dayHours, allHours, entry, minMove, goLong: true, NyTz);
                BuildForDir(res, r, entryUtc.Value, dayHours, allHours, entry, minMove, goLong: false, NyTz);
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
						label = true;
						}
					}

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

