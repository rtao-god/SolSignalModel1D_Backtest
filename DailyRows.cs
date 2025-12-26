using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static async Task<DailyRowsBundle> BuildDailyRowsAsync (
			IndicatorsDailyUpdater indicatorsUpdater,
			DateTime fromUtc, DateTime toUtc,
			List<Candle6h> solAll6h,
			List<Candle6h> btcAll6h,
			List<Candle6h> paxgAll6h,
			List<Candle1m> sol1m )
			{
			if (indicatorsUpdater == null) throw new ArgumentNullException (nameof (indicatorsUpdater));
			if (solAll6h == null) throw new ArgumentNullException (nameof (solAll6h));
			if (btcAll6h == null) throw new ArgumentNullException (nameof (btcAll6h));
			if (paxgAll6h == null) throw new ArgumentNullException (nameof (paxgAll6h));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));

			if (solAll6h.Count == 0) throw new InvalidOperationException ("[daily-rows] solAll6h is empty.");
			if (btcAll6h.Count == 0) throw new InvalidOperationException ("[daily-rows] btcAll6h is empty.");
			if (paxgAll6h.Count == 0) throw new InvalidOperationException ("[daily-rows] paxgAll6h is empty.");
			if (sol1m.Count == 0) throw new InvalidOperationException ("[daily-rows] sol1m is empty (required for labeling).");

			// Берём максимально ранний момент, где гарантированно есть 6h по всем 3 инструментам.
			// fromUtc сохраняем в сигнатуре как внешний контракт, но фактически старт ограничен наличием данных.
			var earliestSolUtc = solAll6h.Min (c => c.OpenTimeUtc);
			var earliestBtcUtc = btcAll6h.Min (c => c.OpenTimeUtc);
			var earliestPaxgUtc = paxgAll6h.Min (c => c.OpenTimeUtc);

			var histFrom = new[] { earliestSolUtc, earliestBtcUtc, earliestPaxgUtc }.Max ();

			var solWinTrainRaw = solAll6h
				.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc)
				.ToList ();

			var btcWinTrainRaw = btcAll6h
				.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc)
				.ToList ();

			var paxgWinTrainRaw = paxgAll6h
				.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc)
				.ToList ();

			Console.WriteLine (
				$"[win6h:raw] sol={solWinTrainRaw.Count}, btc={btcWinTrainRaw.Count}, paxg={paxgWinTrainRaw.Count}");

			// Выравниваем по общим openUtc: SOL ∩ BTC ∩ PAXG.
			var common = solWinTrainRaw.Select (c => c.OpenTimeUtc)
				.Intersect (btcWinTrainRaw.Select (c => c.OpenTimeUtc))
				.Intersect (paxgWinTrainRaw.Select (c => c.OpenTimeUtc))
				.ToHashSet ();

			var solWinTrain = solWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var btcWinTrain = btcWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var paxgWinTrain = paxgWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();

			Console.WriteLine (
				$"[win6h:aligned] sol={solWinTrain.Count}, btc={btcWinTrain.Count}, paxg={paxgWinTrain.Count}, common={common.Count}");

			if (solWinTrain.Count == 0)
				throw new InvalidOperationException ("[daily-rows] aligned SOL 6h window is empty after intersection.");

			// Индикаторные ряды должны покрывать весь диапазон
			var fngDict = indicatorsUpdater.LoadFngDict (histFrom.ToCausalDateUtc (), toUtc.ToCausalDateUtc ());
			var dxyDict = indicatorsUpdater.LoadDxyDict (histFrom.ToCausalDateUtc (), toUtc.ToCausalDateUtc ());

			var build = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: sol1m,
				fngHistory: fngDict,
				dxySeries: dxyDict,
				extraDaily: null,
				nyTz: NyTz
			);

			var rows = build.LabeledRows;

			Console.WriteLine ($"[rows] total built = {rows.Count}");
			DumpNyHourHistogram (rows);

            var mornings = rows
                .Where(r => r.Causal.IsMorning)
                .OrderBy(r => r.Causal.EntryUtc)
                .ToList();

            Console.WriteLine ($"[rows] mornings total (all history) = {mornings.Count}");

			var lastSolTime = solWinTrain.Max (c => c.OpenTimeUtc);
			Console.WriteLine ($"[rows] last SOL 6h = {lastSolTime:O}");

			var rowsList = rows is List<LabeledCausalRow> rl ? rl : rows.ToList ();
			var morningsList = mornings is List<LabeledCausalRow> ml ? ml : mornings.ToList ();

			return await Task.FromResult (new DailyRowsBundle
				{
				AllRows = rowsList,
				Mornings = morningsList
				});
			}

		private static void DumpNyHourHistogram ( IReadOnlyList<LabeledCausalRow> rows )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (rows.Count == 0) return;

			var hist = new Dictionary<int, int> ();

			for (int i = 0; i < rows.Count; i++)
				{
                var entryUtc = rows[i].Causal.EntryUtc.Value;

                if (entryUtc.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"[rows] Causal.DateUtc must be UTC, got Kind={entryUtc.Kind}, t={entryUtc:O}.");

                var ny = TimeZoneInfo.ConvertTimeFromUtc(entryUtc, NyTz);

                if (!hist.TryGetValue (ny.Hour, out var cnt))
					cnt = 0;

				hist[ny.Hour] = cnt + 1;
				}

			Console.WriteLine (
				"[rows] NY hour histogram (all 6h rows, до утреннего фильтра): " +
				string.Join (", ", hist
					.OrderBy (kv => kv.Key)
					.Select (kv => $"{kv.Key:D2}:{kv.Value}")));
			}
		}
	}
