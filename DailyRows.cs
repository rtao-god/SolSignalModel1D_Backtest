using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;

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
			var histFrom = fromUtc.AddDays (-90);

			var solWinTrainRaw = solAll6h.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc).ToList ();
			var btcWinTrainRaw = btcAll6h.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc).ToList ();
			var paxgWinTrainRaw = paxgAll6h.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc).ToList ();

			Console.WriteLine ($"[win6h:raw] sol={solWinTrainRaw.Count}, btc={btcWinTrainRaw.Count}, paxg={paxgWinTrainRaw.Count}");

			// Тройное выравнивание: SOL ∩ BTC ∩ PAXG
			var common = solWinTrainRaw.Select (c => c.OpenTimeUtc)
				.Intersect (btcWinTrainRaw.Select (c => c.OpenTimeUtc))
				.Intersect (paxgWinTrainRaw.Select (c => c.OpenTimeUtc))
				.ToHashSet ();

			var solWinTrain = solWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var btcWinTrain = btcWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var paxgWinTrain = paxgWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();

			Console.WriteLine ($"[win6h:aligned] sol={solWinTrain.Count}, btc={btcWinTrain.Count}, paxg={paxgWinTrain.Count}, common={common.Count}");

			// Индикаторы
			var fngDict = indicatorsUpdater.LoadFngDict (histFrom.Date, toUtc.Date);
			var dxyDict = indicatorsUpdater.LoadDxyDict (histFrom.Date, toUtc.Date);
			indicatorsUpdater.EnsureCoverageOrFail (histFrom.Date, toUtc.Date);

			// Все 6h-строки (для SL-датасета и path-based labels).
			// Здесь используется общий NyTz, чтобы в одном месте контролировать правила NY-времени.
			var rows = RowBuilder.BuildRowsDaily (
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

			Console.WriteLine ($"[rows] total built = {rows.Count}");
			DumpNyHourHistogram (rows);

			var mornings = rows
				.Where (r => r.IsMorning && r.Date >= fromUtc && r.Date < toUtc)
				.OrderBy (r => r.Date)
				.ToList ();

			Console.WriteLine ($"[rows] mornings after filter = {mornings.Count}");

			var lastSolTime = solWinTrain.Max (c => c.OpenTimeUtc);
			Console.WriteLine ($"[rows] last SOL 6h = {lastSolTime:O}");

			return await Task.FromResult (new DailyRowsBundle
				{
				AllRows = rows,
				Mornings = mornings
				});
			}

		private static void DumpNyHourHistogram ( List<DataRow> rows )
			{
			if (rows.Count == 0) return;

			var hist = new Dictionary<int, int> ();
			foreach (var r in rows)
				{
				// Для расчёта часов по Нью-Йорку используется единая таймзона NyTz.
				// Это избавляет от локальных дубликатов TimeZones.NewYork и снижает риск рассинхронизации.
				var ny = TimeZoneInfo.ConvertTimeFromUtc (r.Date, NyTz);
				if (!hist.TryGetValue (ny.Hour, out var cnt)) cnt = 0;
				hist[ny.Hour] = cnt + 1;
				}

			Console.WriteLine ("[rows] NY hour histogram (all 6h rows, до утреннего фильтра): " +
				string.Join (", ", hist.OrderBy (kv => kv.Key).Select (kv => $"{kv.Key:D2}:{kv.Value}")));
			}
		}
	}
