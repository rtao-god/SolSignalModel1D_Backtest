using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.SL
	{
	/// <summary>
	/// Контейнер датасета для SL-модели.
	/// </summary>
	public sealed class SlDataset
		{
		public DateTime TrainUntilUtc { get; init; }

		public List<BacktestRecord> MorningRows { get; init; } = new List<BacktestRecord> ();

		public List<SlHitSample> Samples { get; init; } = new List<SlHitSample> ();
		}

	/// <summary>
	/// Builder SL-датасета.
	/// </summary>
	public static class SlDatasetBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public static SlDataset Build (
			List<BacktestRecord> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict,
			DateTime trainUntil,
			double tpPct,
			double slPct,
			Func<BacktestRecord, bool>? strongSelector )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[SlDatasetBuilder] sol1m is required and must be non-empty.");

			if (sol1h == null || sol1h.Count == 0)
				throw new InvalidOperationException ("[SlDatasetBuilder] sol1h is required and must be non-empty.");

			if (trainUntil.Kind == DateTimeKind.Local)
				throw new InvalidOperationException ($"[SlDatasetBuilder] trainUntil must not be Local: {trainUntil:O}.");

			var trainUntilUtc = trainUntil.Kind == DateTimeKind.Unspecified
				? DateTime.SpecifyKind (trainUntil, DateTimeKind.Utc)
				: trainUntil;

			// Берём только утренние точки, у которых baseline-exit укладывается в trainUntil.
			var rowsTrain = new List<BacktestRecord> (rows.Count);

			foreach (var r in rows.OrderBy (r => r.DateUtc))
				{
				if (r.Causal.IsMorning != true)
					continue;

				if (!NyWindowing.TryComputeBaselineExitUtc (r.DateUtc, NyTz, out var exitUtc))
					continue;

				if (exitUtc <= trainUntilUtc)
					rowsTrain.Add (r);
				}

			if (rowsTrain.Count == 0)
				{
				return new SlDataset
					{
					TrainUntilUtc = trainUntilUtc,
					MorningRows = new List<BacktestRecord> (),
					Samples = new List<SlHitSample> ()
					};
				}

			// Генерация SL-сэмплов только на заранее отфильтрованных днях.
			var allSamples = SlOfflineBuilder.Build (
				rows: rowsTrain,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				tpPct: tpPct,
				slPct: slPct,
				strongSelector: strongSelector);

			if (allSamples.Count == 0)
				{
				return new SlDataset
					{
					TrainUntilUtc = trainUntilUtc,
					MorningRows = new List<BacktestRecord> (),
					Samples = new List<SlHitSample> ()
					};
				}

			// Финальный safety-cut по baseline-exit (на случай неконсистентного EntryUtc в сэмплах).
			var filteredSamples = new List<SlHitSample> (allSamples.Count);

			foreach (var s in allSamples)
				{
				var exitUtc = NyWindowing.ComputeBaselineExitUtc (s.EntryUtc, NyTz);
				if (exitUtc <= trainUntilUtc)
					filteredSamples.Add (s);
				}

			var morningByEntryUtc = rowsTrain
				.GroupBy (r => r.DateUtc)
				.ToDictionary (g => g.Key, g => g.First ());

			var morningRows = new List<BacktestRecord> ();

			foreach (var s in filteredSamples)
				{
				if (!morningByEntryUtc.TryGetValue (s.EntryUtc, out var row))
					throw new InvalidOperationException ($"[SlDatasetBuilder] No BacktestRecord for sample entryUtc={s.EntryUtc:O}.");

				morningRows.Add (row);
				}

			var distinctMorning = morningRows
				.OrderBy (r => r.DateUtc)
				.GroupBy (r => r.DateUtc)
				.Select (g => g.First ())
				.ToList ();

			return new SlDataset
				{
				TrainUntilUtc = trainUntilUtc,
				MorningRows = distinctMorning,
				Samples = filteredSamples
				};
			}
		}
	}
